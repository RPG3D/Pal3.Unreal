// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.Script
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Command;
    using Command.Extensions;
    using Core.Command;
    using Core.Command.SceCommands;
    using Core.Contract.Constants;
    using Core.DataReader.Sce;
    using Core.Utilities;
    using Data;
    using Engine.Logging;
    using Engine.Services;
    using Patcher;
    using Scene;
    using Waiter;

    public sealed class ScriptManager : IDisposable,
        ICommandExecutor<ScriptExecuteCommand>,
        ICommandExecutor<ResetGameStateCommand>
    {
        private readonly ISceCommandParser _sceCommandParser;
        private readonly IPalScriptPatcher _scriptPatcher;
        private readonly IUserVariableStore<ushort, int> _userVariableStore;

        private readonly SceFile _systemSceFile;
        private readonly SceFile _worldMapSceFile;
        private SceFile _currentSceFile;

        private readonly Queue<PalScriptRunner> _pendingScripts = new ();
        private readonly List<PalScriptRunner> _runningScripts = new ();
        private readonly List<PalScriptRunner> _finishedScripts = new ();

        private bool _waitingForSceneLoad;

        public ScriptManager(GameResourceProvider resourceProvider,
            IUserVariableStore<ushort, int> userVariableStore,
            ISceCommandParser sceCommandParser,
            IPalScriptPatcher scriptPatcher)
        {
            resourceProvider = Requires.IsNotNull(resourceProvider, nameof(resourceProvider));
            _userVariableStore = Requires.IsNotNull(userVariableStore, nameof(userVariableStore));
            _sceCommandParser = Requires.IsNotNull(sceCommandParser, nameof(sceCommandParser));
            _scriptPatcher = Requires.IsNotNull(scriptPatcher, nameof(scriptPatcher));

            _systemSceFile = resourceProvider.GetGameResourceFile<SceFile>(FileConstants.SystemSceFileVirtualPath);
            _worldMapSceFile = resourceProvider.GetGameResourceFile<SceFile>(FileConstants.WorldMapSceFileVirtualPath);

            CommandExecutorRegistry<ICommand>.Instance.Register(this);
        }

        public void Dispose()
        {
            CommandExecutorRegistry<ICommand>.Instance.UnRegister(this);

            _pendingScripts.Clear();

            foreach (PalScriptRunner scriptRunner in _runningScripts)
            {
                scriptRunner.OnCommandExecutionRequested -= OnCommandExecutionRequested;
                scriptRunner.Dispose();
            }

            _runningScripts.Clear();

            foreach (PalScriptRunner finishedScript in _finishedScripts)
            {
                finishedScript.OnCommandExecutionRequested -= OnCommandExecutionRequested;
                finishedScript.Dispose();
            }

            _finishedScripts.Clear();
        }

        public int GetNumberOfRunningScripts()
        {
            return _pendingScripts.Count + _runningScripts.Count(script => !script.IsDisposed);
        }

        public sealed record ScriptStatus(uint Id, string Type, string Description, bool Pending,
            string? LastCommand, long? LastCommandOffset, string? Waiter);

        public ScriptStatus[] GetScriptStatus()
        {
            return _pendingScripts.Where(script => !script.IsDisposed).Select(script => Describe(script, true))
                .Concat(_runningScripts.Where(script => !script.IsDisposed).Select(script => Describe(script, false)))
                .ToArray();

            static ScriptStatus Describe(PalScriptRunner script, bool pending) =>
                new(script.ScriptId, script.ScriptType.ToString(), script.ScriptDescription, pending,
                    script.LastCommand, script.LastCommandOffset, script.CurrentWaiter);
        }

        public bool AddScript(uint scriptId, bool isWorldMapScript = false)
        {
            if (scriptId == ScriptConstants.InvalidScriptId) return false;

            if (IsScriptScheduled(scriptId))
            {
                EngineLogger.LogError($"Script is already running: {scriptId}");
                return false;
            }

            var scriptType = isWorldMapScript ? PalScriptType.WorldMap : GetScriptType(scriptId);
            var sceFile = scriptType switch
            {
                PalScriptType.WorldMap => _worldMapSceFile,
                PalScriptType.System => _systemSceFile,
                _ => _currentSceFile
            };
            if (sceFile == null || !sceFile.ScriptBlocks.ContainsKey(scriptId))
            {
                EngineLogger.LogError($"Missing {scriptType} script: {scriptId}");
                return false;
            }

            EngineLogger.Log($"Add {scriptType} script id: {scriptId}");
            var scriptRunner = PalScriptRunner.Create(sceFile, scriptType, scriptId,
                _userVariableStore, _sceCommandParser, _scriptPatcher);
            scriptRunner.OnCommandExecutionRequested += OnCommandExecutionRequested;
            _pendingScripts.Enqueue(scriptRunner);
            return true;
        }

        internal static PalScriptType GetScriptType(uint scriptId) =>
            scriptId < ScriptConstants.SystemScriptIdMax ? PalScriptType.System : PalScriptType.Scene;

        /// <summary>
        /// Whether the script is pending or currently running. Callers that need to
        /// wait for a script's completion notification use this to detect the case
        /// where it already finished (the notification is not replayed).
        /// </summary>
        internal bool IsScriptScheduled(uint scriptId) =>
            _pendingScripts.Any(script => script.ScriptId == scriptId) ||
            _runningScripts.Any(script => script.ScriptId == scriptId && !script.IsDisposed);

        private void OnCommandExecutionRequested(object sender, ICommand command)
        {
            Pal3.Instance.Execute(command);
            var scenes = ServiceLocator.Instance.Get<SceneManager>();
            if (scenes.IsLoading && sender is PalScriptRunner runner)
            {
                var waiter = new WaitUntilCanceled();
                scenes.TryWaitForLoad(
                    () =>
                    {
                        _waitingForSceneLoad = false;
                        waiter.CancelWait();
                    },
                    _ =>
                    {
                        _waitingForSceneLoad = false;
                        _runningScripts.Remove(runner);
                        runner.OnCommandExecutionRequested -= OnCommandExecutionRequested;
                        runner.Dispose();
                        Pal3.Instance.Execute(new ScriptFailedToRunNotification(runner.ScriptId, runner.ScriptType));
                    });
                runner.WaitForSceneLoad(waiter);
                _waitingForSceneLoad = true;
            }
        }

        public void Update(float deltaTime)
        {
            if (_waitingForSceneLoad) return;
            while (_pendingScripts.Count > 0)
            {
                _runningScripts.Insert(0, _pendingScripts.Dequeue());
            }

            if (_runningScripts.Count == 0) return;

            foreach (PalScriptRunner script in _runningScripts)
            {
                if (!script.Update(deltaTime))
                {
                    _finishedScripts.Add(script);
                }
                if (_waitingForSceneLoad) break;
            }

            foreach (PalScriptRunner finishedScript in _finishedScripts)
            {
                _runningScripts.Remove(finishedScript);
                if (finishedScript.IsDisposed) continue;
                finishedScript.OnCommandExecutionRequested -= OnCommandExecutionRequested;
                finishedScript.Dispose();

                EngineLogger.Log($"Script [{finishedScript.ScriptId} " +
                          $"{finishedScript.ScriptDescription}] finished running");
                Pal3.Instance.Execute(new ScriptFinishedRunningNotification(
                    finishedScript.ScriptId,
                    finishedScript.ScriptType));
            }

            _finishedScripts.Clear();
        }

        public bool TryAddSceneScript(SceFile sceFile, string sceneScriptDescription, out uint sceneScriptId)
        {
            sceneScriptId = ScriptConstants.InvalidScriptId;

            _currentSceFile = sceFile;

            foreach (KeyValuePair<uint, SceScriptBlock> scriptBlock in _currentSceFile.ScriptBlocks
                         .Where(scriptBlock =>
                             string.Equals(scriptBlock.Value.Description,
                                 sceneScriptDescription,
                                 StringComparison.OrdinalIgnoreCase)))
            {
                sceneScriptId = scriptBlock.Key;
                AddScript(sceneScriptId);
                return true;
            }

            return false;
        }

        public void Execute(ScriptExecuteCommand command)
        {
            uint scriptId = (uint)command.ScriptId;
            // A caller waiting on an existing run should receive that run's outcome.
            if (!IsScriptScheduled(scriptId) && !AddScript(scriptId))
            {
                Pal3.Instance.Execute(new ScriptFailedToRunNotification(scriptId, GetScriptType(scriptId)));
            }
        }

        public void Execute(ResetGameStateCommand command)
        {
            _waitingForSceneLoad = false;

            foreach (PalScriptRunner script in _pendingScripts)
            {
                script.OnCommandExecutionRequested -= OnCommandExecutionRequested;
                script.Dispose();
            }
            _pendingScripts.Clear();

            foreach (PalScriptRunner script in _runningScripts)
            {
                script.OnCommandExecutionRequested -= OnCommandExecutionRequested;
                script.Dispose();
            }

            _finishedScripts.Clear();
        }
    }
}
