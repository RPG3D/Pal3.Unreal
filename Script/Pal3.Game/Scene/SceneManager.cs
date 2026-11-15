// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.Scene
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Actor.Controllers;
    using Command;
    using Command.Extensions;
    using Core.Command;
    using Core.Command.SceCommands;
    using Core.Contract.Constants;
    using Core.DataReader.Sce;
    using Core.DataReader.Scn;
    using Core.Utilities;
    using Data;
    using Engine.Core.Abstraction;
    using Engine.Core.Implementation;
    using Engine.Extensions;
    using Engine.Logging;
    using GameSystems.Combat.Scene;
    using Newtonsoft.Json;
    using Script;
    using Settings;
    using State;
    using UnityEngine;

    public sealed class SceneManager : IDisposable,
        ICommandExecutor<SceneLoadCommand>,
        ICommandExecutor<SceneObjectDoNotLoadFromSaveStateCommand>,
        ICommandExecutor<ResetGameStateCommand>
    {
        private readonly GameResourceProvider _resourceProvider;
        private readonly ISceneObjectFactory _sceneObjectFactory;
        private readonly SceneStateManager _sceneStateManager;
        private readonly ScriptManager _scriptManager;
        private readonly GameSettings _gameSettings;
        private readonly IGameEntity _cameraEntity;

        private IGameEntity _currentSceneRoot;
        private IGameEntity _currentCombatSceneRoot;

        private GameScene _currentGameScene;
        private CombatScene _currentCombatScene;

        private readonly HashSet<int> _sceneObjectIdsToNotLoadFromSaveState = new ();
        private const double LoadBudgetMs = 8;
        private SceneLoadJob? _load;

        private sealed record SceneLoadJob(IEnumerator<SceneLoadStep> Batches, Action? OnReady,
            Action<Exception>? OnFailure, Stopwatch Elapsed)
        {
            public int Frames;
            public double SlowestBatchMs;
            public string SlowestPhase = "";
            public Action? ResumeScript;
            public Action<Exception>? FailScript;
        }

        public bool IsLoading => _load != null;
        public string LoadingPhase { get; private set; } = "";
        public float LoadingProgress { get; private set; }
        public string? LastLoadError { get; private set; }

        public SceneManager(GameResourceProvider resourceProvider,
            ISceneObjectFactory sceneObjectFactory,
            SceneStateManager sceneStateManager,
            ScriptManager scriptManager,
            GameSettings gameSettings,
            IGameEntity cameraEntity)
        {
            _resourceProvider = Requires.IsNotNull(resourceProvider, nameof(resourceProvider));
            _sceneObjectFactory = Requires.IsNotNull(sceneObjectFactory, nameof(sceneObjectFactory));
            _sceneStateManager = Requires.IsNotNull(sceneStateManager, nameof(sceneStateManager));
            _scriptManager = Requires.IsNotNull(scriptManager, nameof(scriptManager));
            _gameSettings = Requires.IsNotNull(gameSettings, nameof(gameSettings));
            _cameraEntity = Requires.IsNotNull(cameraEntity, nameof(cameraEntity));
            CommandExecutorRegistry<ICommand>.Instance.Register(this);
        }

        public void Dispose()
        {
            CancelLoad();
            DisposeCurrentScene();
            CommandExecutorRegistry<ICommand>.Instance.UnRegister(this);
        }

        public GameScene GetCurrentScene()
        {
            return _currentGameScene;
        }

        public CombatScene GetCurrentCombatScene()
        {
            return _currentCombatScene;
        }

        public IGameEntity GetSceneRootGameEntity()
        {
            if (_currentCombatSceneRoot != null)
            {
                return _currentCombatSceneRoot;
            }
            else if (_currentSceneRoot != null)
            {
                return _currentSceneRoot;
            }

            return null;
        }

        internal void LoadTitleScene(string sceneCityName, string sceneName)
        {
            foreach (var _ in LoadBatches(sceneCityName, sceneName)) { }
        }

        internal void BeginLoadScene(string city, string scene, Action? onReady = null, Action<Exception>? onFailure = null)
        {
            if (IsLoading) throw new InvalidOperationException("A PAL3 scene is already loading.");
            LastLoadError = null;
            LoadingPhase = "正在准备场景";
            LoadingProgress = 0;
            _load = new SceneLoadJob(LoadBatches(city, scene).GetEnumerator(), onReady, onFailure, Stopwatch.StartNew());
        }

        internal bool TryWaitForLoad(Action onReady, Action<Exception> onFailure)
        {
            if (_load is not { } load) return false;
            load.ResumeScript += onReady;
            load.FailScript += onFailure;
            return true;
        }

        internal void RunSceneCommands(IReadOnlyList<ICommand> commands, Action<Exception>? onFailure = null)
        {
            ExecuteFrom(0);

            void ExecuteFrom(int first)
            {
                for (int i = first; i < commands.Count; i++)
                {
                    if (commands[i] is SceneLoadCommand load)
                    {
                        int next = i + 1;
                        BeginLoadScene(load.SceneCityName, load.SceneName, () => ExecuteFrom(next), onFailure);
                        return;
                    }
                    Pal3.Instance.Execute(commands[i]);
                }
            }
        }

        internal void TickLoad()
        {
            if (_load is not { } load) return;
            // Let the loading widget present before the first batch runs.
            if (load.Frames++ == 0) return;
            long frameStart = Stopwatch.GetTimestamp();
            try
            {
                do
                {
                    long batchStart = Stopwatch.GetTimestamp();
                    bool more = load.Batches.MoveNext();
                    double batchMs = Stopwatch.GetElapsedTime(batchStart).TotalMilliseconds;
                    if (batchMs > load.SlowestBatchMs)
                    {
                        load.SlowestBatchMs = batchMs;
                        load.SlowestPhase = more ? load.Batches.Current.Phase : "完成场景";
                    }
                    if (!more)
                    {
                        CancelLoad();
                        load.OnReady?.Invoke();
                        if (_load is { } next)
                        {
                            next.ResumeScript += load.ResumeScript;
                            next.FailScript += load.FailScript;
                        }
                        else
                        {
                            load.ResumeScript?.Invoke();
                        }
                        EngineLogger.Log($"Scene loaded across {load.Frames} frames in {load.Elapsed.ElapsedMilliseconds} ms, slowest batch {load.SlowestBatchMs:F1} ms ({load.SlowestPhase})");
                        return;
                    }
                    LoadingPhase = load.Batches.Current.Phase;
                    LoadingProgress = load.Batches.Current.Progress;
                } while (Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds < LoadBudgetMs);
            }
            catch (Exception error)
            {
                if (ReferenceEquals(_load, load)) CancelLoad();
                LastLoadError = error.GetBaseException().Message;
                EngineLogger.LogException(error);
                try
                {
                    load.OnFailure?.Invoke(error);
                }
                finally
                {
                    load.FailScript?.Invoke(error);
                }
            }
        }

        private void CancelLoad()
        {
            var load = _load;
            _load = null;
            LoadingPhase = "";
            LoadingProgress = 0;
            load?.Batches.Dispose();
        }

        private IEnumerable<SceneLoadStep> LoadBatches(string sceneCityName, string sceneName)
        {
            ScnFile scnFile = _resourceProvider.GetGameResourceFile<ScnFile>(
                FileConstants.GetScnFileVirtualPath(sceneCityName, sceneName));
            SceFile sceFile = _resourceProvider.GetGameResourceFile<SceFile>(
                FileConstants.GetSceneSceFileVirtualPath(sceneCityName));
            yield return new("正在读取场景描述", .04f);

            if (_currentGameScene != null) Pal3.Instance.Execute(new SceneLeavingCurrentSceneNotification());
            try
            {
                if (_currentSceneRoot is GameEntity root)
                {
                    foreach (var _ in root.DestroyDescendantsInBatches())
                    {
                        yield return new("正在卸载旧场景", .08f);
                    }
                }
            }
            finally
            {
                DisposeCurrentScene(notifyLeaving: false);
            }
            yield return new("正在卸载旧场景", .14f);
            bool committed = false;
            try
            {
                Pal3.Instance.Execute(new ScenePreLoadingNotification(scnFile.SceneInfo));
                EngineLogger.Log($"Loading scene: {JsonConvert.SerializeObject(scnFile.SceneInfo)}");

                _currentSceneRoot = GameEntityFactory.Create($"Scene_{sceneCityName}_{sceneName}",
                    null, worldPositionStays: false);
                _currentGameScene = _currentSceneRoot.AddComponent<GameScene>();
                _currentGameScene.Init(_resourceProvider,
                    _sceneObjectFactory, _sceneStateManager, _gameSettings.IsRealtimeLightingAndShadowsEnabled,
                    _cameraEntity, _sceneObjectIdsToNotLoadFromSaveState);
                foreach (var phase in _currentGameScene.LoadBatches(scnFile, _currentSceneRoot))
                {
                    yield return phase;
                }

                _sceneObjectIdsToNotLoadFromSaveState.Clear();
                _scriptManager.TryAddSceneScript(sceFile, $"_{sceneCityName}_{sceneName}", out var sceneScriptId);
                Pal3.Instance.Execute(new ScenePostLoadingNotification(scnFile.SceneInfo, sceneScriptId));
                committed = true;
            }
            finally
            {
                _sceneObjectIdsToNotLoadFromSaveState.Clear();
                if (!committed) DisposeCurrentScene(notifyLeaving: false);
            }
        }

        public CombatScene LoadCombatScene(string combatSceneName)
        {
            Stopwatch timer = Stopwatch.StartNew();

            HideCurrentScene();

            _currentCombatSceneRoot = GameEntityFactory.Create($"CombatScene_{combatSceneName}",
                null, worldPositionStays: false);

            _currentCombatScene = _currentCombatSceneRoot.AddComponent<CombatScene>();
            _currentCombatScene.Init(_resourceProvider);
            _currentCombatScene.Load(_currentCombatSceneRoot,
                combatSceneName,
                _gameSettings.IsRealtimeLightingAndShadowsEnabled);

            EngineLogger.Log($"CombatScene loaded in {timer.ElapsedMilliseconds} ms");

            // Also a good time to collect garbage
            System.GC.Collect();

            return _currentCombatScene;
        }

        public void UnloadCombatScene()
        {
            if (_currentCombatScene != null)
            {
                _currentCombatScene.Destroy();
                _currentCombatScene = null;
            }

            if (_currentCombatSceneRoot != null)
            {
                _currentCombatSceneRoot.Destroy();
                _currentCombatSceneRoot = null;
            }

            ShowCurrentScene();
        }

        private readonly IList<Renderer> _temporarilyDisabledRenderers = new List<Renderer>();
        private readonly IList<GameObject> _temporarilyDisabledGameObjects = new List<GameObject>();
        private void HideCurrentScene()
        {
            _temporarilyDisabledRenderers.Clear();
            _temporarilyDisabledGameObjects.Clear();

            foreach (ActorMovementController movementController in
                     _currentSceneRoot.GetComponentsInChildren<ActorMovementController>())
            {
                movementController.PauseMovement();
            }

            foreach (Renderer renderer in _currentSceneRoot.GetComponentsInChildren<Renderer>())
            {
                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    _temporarilyDisabledRenderers.Add(renderer);
                }
            }

            foreach (ParticleSystem particleSystem in _currentSceneRoot.GetComponentsInChildren<ParticleSystem>())
            {
                GameObject particleGo = particleSystem.gameObject;
                if (particleGo.activeInHierarchy)
                {
                    particleGo.SetActive(false);
                    _temporarilyDisabledGameObjects.Add(particleGo);
                }
            }

            // Move the scene root to a lower position just to be safe.
            _currentSceneRoot.Transform.LocalPosition = Vector3.down * 100f;
        }

        private void ShowCurrentScene()
        {
            // Move the scene root back to the original position.
            _currentSceneRoot.Transform.LocalPosition = Vector3.zero;

            foreach (GameObject disabledGo in _temporarilyDisabledGameObjects)
            {
                if (disabledGo != null) disabledGo.SetActive(true);
            }

            foreach (Renderer renderer in _temporarilyDisabledRenderers)
            {
                if (renderer != null) renderer.enabled = true;
            }

            foreach (ActorMovementController movementController in
                     _currentSceneRoot.GetComponentsInChildren<ActorMovementController>())
            {
                movementController.ResumeMovement();
            }

            _temporarilyDisabledRenderers.Clear();
            _temporarilyDisabledGameObjects.Clear();
        }

        private void DisposeCurrentScene(bool notifyLeaving = true)
        {
            bool hadScene = _currentSceneRoot != null || _currentCombatSceneRoot != null;
            _temporarilyDisabledRenderers.Clear();
            _temporarilyDisabledGameObjects.Clear();

            if (_currentGameScene != null)
            {
                if (notifyLeaving) Pal3.Instance.Execute(new SceneLeavingCurrentSceneNotification());
                _currentGameScene.Destroy();
                _currentGameScene = null;
            }

            if (_currentCombatScene != null)
            {
                _currentCombatScene.Destroy();
                _currentCombatScene = null;
            }

            if (_currentCombatSceneRoot != null)
            {
                _currentCombatSceneRoot.Destroy();
                _currentCombatSceneRoot = null;
            }

            if (_currentSceneRoot != null)
            {
                _currentSceneRoot.Destroy();
                _currentSceneRoot = null;
            }

            _resourceProvider.ClearSceneCaches();
            if (hadScene) Pal3TransientAssets.CollectAfterSceneUnload();
        }

        public void Execute(SceneLoadCommand command) => BeginLoadScene(command.SceneCityName, command.SceneName);

        public void Execute(ResetGameStateCommand command)
        {
            CancelLoad();
            DisposeCurrentScene();
        }

        public void Execute(SceneObjectDoNotLoadFromSaveStateCommand command)
        {
            _sceneObjectIdsToNotLoadFromSaveState.Add(command.ObjectId);
        }
    }
}
