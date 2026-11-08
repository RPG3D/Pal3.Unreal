// Smoke bootstrap for the managed runtime layer. Invoked by the UE driver
// actor (APal3Driver.BeginPlay). Verifies the Unity-style lifecycle dispatch
// (Awake/OnEnable/Start/Update/LateUpdate) and every CoroutineScheduler yield
// operand (null / float / Task / nested IEnumerator) actually run per-frame.
// Once the full Pal3 boot (GameResourceInitializer -> Pal3Game) is wired, this
// stays behind as a self-check utility.

using System;
using System.Collections;
using System.Threading.Tasks;
using Engine.Core.Abstraction;
using Engine.Core.Implementation;
using Engine.Services;
using Pal3.Core.Contract.Constants;
using Pal3.Core.DataReader.Scn;
using Pal3.Core.DataReader.Sce;
using Pal3.Core.Command.SceCommands;
using Pal3.Game.Command.Extensions;
using Pal3.Game.Command;
using Pal3.Game.Dev;
using Pal3.Game.Data;
using Pal3.Game.GamePlay;
using Pal3.Game.GameSystems.Team;
using Pal3.Game.Scene;
using Pal3.Game.Script;
using Pal3.Game.State;
using UnityEngine;

namespace Pal3.Game
{
    public static class Pal3Boot
    {
        private sealed class LifecycleProbeScript : GameEntityScript
        {
            public int AwakeCount, OnEnableCount, UpdateCount, LateUpdateCount;

            private void Awake() { AwakeCount++; Debug.Log("[Pal3Boot] Awake"); }
            private void OnEnable() { OnEnableCount++; Debug.Log("[Pal3Boot] OnEnable"); }

            private IEnumerator Start()
            {
                Debug.Log("[Pal3Boot] Start coroutine begin");
                yield return null;                       // next frame
                Debug.Log("[Pal3Boot] after yield null (frame 2)");
                yield return 0.2f;                       // float seconds
                Debug.Log("[Pal3Boot] after yield 0.2f");
                yield return Task.Delay(50);             // Task
                Debug.Log("[Pal3Boot] after Task.Delay(50)");
                yield return Nested();                   // nested IEnumerator
                Debug.Log("[Pal3Boot] after nested coroutine");
                yield return new WaitForSeconds(0.1f);   // CustomYieldInstruction
                Debug.Log("[Pal3Boot] after WaitForSeconds(0.1f) - Start coroutine done");
            }

            private static IEnumerator Nested()
            {
                yield return null;
                yield return null;
            }

            private void Update() { UpdateCount++; }
            private void LateUpdate() { LateUpdateCount++; }
        }

        private static LifecycleProbeScript? _probe;
        private static float _probeLifetime;
        private static bool _summaryLogged;

        /// <summary>
        /// Drops every piece of static state left over from the previous PIE session.
        ///
        /// The UnrealSharp managed runtime is NOT reloaded between PIE runs, so the
        /// boot statics (_fullBootStarted!) and singleton services keep pointing at
        /// actors destroyed with the old world. Without this reset the second PIE run
        /// skips the full boot (no menu, no scene) and its first tick steps a stale
        /// cutscene coroutine -> EXCEPTION_ACCESS_VIOLATION in K2_GetActorLocation.
        /// Called from APal3Driver.BeginPlay, i.e. once per fresh session.
        /// </summary>
        public static void ResetForNewSession()
        {
            // Probe/boot flags.
            _probe = null;
            _probeLifetime = 0f;
            _summaryLogged = false;
            _fullBootStarted = false;
            s_InputActions = null;
            WaitingForMenuStart = false;
            IsGameReady = false;
            AutoDialogueEnabled = false;
            CameraActor = null;

            // Prefab instantiation hook is re-installed by RunFullBoot.
            UnityEngine.Object.InstantiateHook = null;

            // Frame-driven state: coroutines, script registry, entity factory, renderer.
            Engine.Core.Implementation.CoroutineScheduler.ResetForNewSession();
            Engine.Core.Implementation.GameEntityScriptRuntime.ResetForNewSession();
            Engine.Core.Implementation.UEGameEntityFactory.ResetForNewSession();
            Engine.Renderer.StaticMeshRenderer.ResetForNewSession();

            // Static registries/caches owned by the ported game code. The command
            // executor registry matters most: it is process-wide, so without a reset
            // the new Pal3Game registers next to the OLD executors and the previous
            // session's SceneManager also receives SceneLoadCommand (second boot hung).
            UnityEngine.InputSystem.InputSystem.ResetForNewSession();
            IngameDebugConsole.DebugLogManager.ResetForNewSession();
            Pal3.Game.Data.GameResourceProvider.ResetStaticCaches();
            UnityEngine.SceneLighting.Reset();

            // Clear managed presentation state from the previous PIE session.
            Engine.Diagnostics.DebugOverlay.ResetForNewSession();
            Pal3.Game.Dev.DialoguePresentation.Clear();
            Pal3.Game.Dev.DialoguePresentation.ClearChoices();
            try { UnrealSharp.SharpDemo.UPal3AudioHelper.StopAllAudio(); }
            catch (Exception) { }

            // Singleton services last (Pal3Game, ServiceLocator) — they are rebuilt
            // lazily by the upcoming boot.
            Engine.Utilities.SessionStateRegistry.ResetAll();

            Debug.Log("[Pal3Boot] Session state reset (PIE restart safe)");
        }

        /// <summary>Starts the lifecycle/coroutine smoke probe. Call from the UE driver actor.</summary>
        public static void RunSmokeProbe()
        {
            if (_probe != null) return; // already running

            Debug.Log("[Pal3Boot] Smoke probe starting");
            _probe = new LifecycleProbeScript();
            GameEntityScriptRuntime.Activate(_probe);
            _probeLifetime = 0f;
            _summaryLogged = false;
        }

        private static Pal3.Game.Input.PlayerInputActions? s_InputActions;

        /// <summary>Input actions for the keyboard bridge (null before game init).</summary>
        public static Pal3.Game.Input.PlayerInputActions? InputActions
        {
            get
            {
                try
                {
                    return s_InputActions ??= Engine.Services.ServiceLocator.Instance
                        .Get<Pal3.Game.Input.PlayerInputActions>();
                }
                catch (Exception)
                {
                    return null; // not registered yet (early boot)
                }
            }
        }


        /// <summary>Call every frame after GameEntityScriptRuntime.TickAll; logs the probe verdict.</summary>
        public static void TickSmokeProbe(float deltaTime)
        {
            if (_probe == null || _summaryLogged) return;

            _probeLifetime += deltaTime;

            // Start coroutine timeline: ~2 frames + 0.2s + 50ms + 2 frames + 0.1s.
            if (_probeLifetime > 2.5f)
            {
                _summaryLogged = true;
                bool ok = _probe.AwakeCount == 1
                       && _probe.OnEnableCount == 1
                       && _probe.UpdateCount > 30
                       && _probe.LateUpdateCount == _probe.UpdateCount;
                Debug.Log(ok
                    ? $"[Pal3Boot] SMOKE PASS - Update x{_probe.UpdateCount}, LateUpdate x{_probe.LateUpdateCount}"
                    : $"[Pal3Boot] SMOKE FAIL - Awake:{_probe.AwakeCount} OnEnable:{_probe.OnEnableCount} "
                      + $"Update:{_probe.UpdateCount} LateUpdate:{_probe.LateUpdateCount}");

                GameEntityScriptRuntime.Deactivate(_probe);
                _probe = null;
            }
        }

        /// <summary>
        /// Default PAL3 game data location (Steam install). Overridable via the
        /// PAL3_DATA_PATH environment variable.
        /// </summary>
        private const string DefaultDataPath = @"E:\Program Files (x86)\Steam\steamapps\common\PAL3\";

        private static bool _fullBootStarted;

        /// <summary>
        /// Boots the real game: GameResourceInitializer mounts the CPK file system
        /// from the PAL3 game data folder, registers engine services and finally
        /// instantiates the starting component (Pal3Game).
        /// </summary>
        /// <summary>Real UE actor that stands in for UnityEngine.Camera.main.</summary>
        public static UnrealSharp.Engine.AActor? CameraActor { get; private set; }

        /// <summary>
        /// Creates the camera actor the ported CameraManager drives. Done before the
        /// resource initializer so AutoFillStubFields can attach it to the Camera stub
        /// before Pal3Game builds its camera entity.
        /// </summary>
        private static void EnsureCameraActor()
        {
            if (CameraActor != null && UnrealSharp.CoreUObject.UObjectExtensions.IsValid(CameraActor)) return;

            if (Engine.Core.Implementation.UEGameEntityFactory.TryCreate("Pal3Camera")
                is not Engine.Core.Implementation.UEGameEntity entity)
            {
                UnrealSharp.Log.UnrealLogger.LogError("Pal3", "[camera] TryCreate(Pal3Camera) failed");
                return;
            }

            CameraActor = entity.Actor;
            Engine.Core.Implementation.UEGameEntityFactory.CameraActor = CameraActor;
            Engine.Core.Implementation.UEGameEntityFactory.Wrap(CameraActor);

            UnrealSharp.Engine.UCameraComponent camera =
                CameraActor.AddComponentByClass<UnrealSharp.Engine.UCameraComponent>(
                    false, new UnrealSharp.CoreUObject.FTransform());
            // Unity's camera is a long lens: fieldOfView = 18.15 (VERTICAL, degrees).
            // UE's UCameraComponent.FieldOfView is HORIZONTAL, so convert:
            //   h = 2*atan(tan(18.15/2) * 16/9) = 31.7 deg
            // (Verified against the Unity build via the Unity MCP probe.)
            camera.FieldOfView = 31.7f;
            CameraActor.SetActorLocation(new UnrealSharp.CoreUObject.FVector(0, 0, 250));
            Engine.Diagnostics.DebugOverlay.Info("[camera] Pal3Camera actor created");
            UnrealSharp.Log.UnrealLogger.Log("Pal3", $"[camera] Pal3Camera actor created at {CameraActor.ActorLocation}");
        }

        public static void RunFullBoot()
        {
            if (_fullBootStarted) return;
            _fullBootStarted = true;

            string dataPath = Environment.GetEnvironmentVariable("PAL3_DATA_PATH");
            if (string.IsNullOrEmpty(dataPath)) dataPath = DefaultDataPath;

            Debug.Log($"[Pal3Boot] Full boot starting, game data folder: {dataPath}");

            // PAL3 data files use GBK/Big5 text encodings (codepages 936/950), which
            // are not available in the default .NET encoding provider on this host.
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            EnsureCameraActor();

            Settings.PlayerPrefsStore.SetSeed(nameof(Settings.GameSettings.GameDataFolderPath), dataPath);

            // Install the instantiation hook so cloning the starting-component prefab
            // spawns real managed entities (Unity prefab-instantiate analog).
            UnityEngine.Object.InstantiateHook = InstantiateHook;

            IGameEntity entity = GameEntityFactory.Create("GameResourceInitializer");
            GameResourceInitializer initializer = entity.AddComponent<GameResourceInitializer>();

            // Stand-in for the Unity "starting component" prefab: a stub GameObject
            // carrying Pal3Game, which GameResourceInitializer instantiates once all
            // services are registered.
            GameObject startingPrefab = new("Pal3Game");
            startingPrefab.PrefabScriptTypes.Add(typeof(Pal3Game));
            typeof(GameResourceInitializer)
                .GetField("startingComponent",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(initializer, startingPrefab);
        }

        /// <summary>
        /// Object.Instantiate hook: cloning a stub prefab GameObject spawns real
        /// managed components. GameEntityScript-derived types attach to a new
        /// managed entity; plain component types (e.g. Pal3Game, which derives
        /// from Singleton instead) are instantiated and activated standalone.
        /// Non-prefab objects fall back to default (return original).
        /// </summary>
        private static UnityEngine.Object? InstantiateHook(UnityEngine.Object original)
        {
            if (original is GameObject prefab && prefab.PrefabScriptTypes.Count > 0)
            {
                IGameEntity clone = GameEntityFactory.Create(prefab.name);
                foreach (Type scriptType in prefab.PrefabScriptTypes)
                {
                    if (typeof(GameEntityScript).IsAssignableFrom(scriptType))
                    {
                        clone.AddComponent(scriptType);
                    }
                    else
                    {
                        // Singleton-style component (e.g. Pal3Game : Singleton<Pal3Game>).
                        // Engine.Singleton<T>.Instance constructs its own instance lazily,
                        // so constructing a second one here would leave game code talking
                        // to an unticked object. ADOPT the engine singleton instead.
                        object component = TryGetEngineSingleton(scriptType)
                            ?? Activator.CreateInstance(scriptType, nonPublic: true)!;
                        GameEntityScriptRuntime.Activate(component);
                        if (component is Pal3Game)
                        {
                            OnGameSingletonReady();
                        }
                    }
                }
                return new GameObject(prefab.name);
            }
            return null;
        }

        /// <summary>Resolves <c>Singleton&lt;T&gt;.Instance</c> for a type deriving from it.</summary>
        private static object? TryGetEngineSingleton(Type type)
        {
            for (Type? t = type; t != null; t = t.BaseType)
            {
                if (t.IsGenericType
                    && t.GetGenericTypeDefinition() == typeof(Engine.Utilities.Singleton<>))
                {
                    return t.GetProperty("Instance",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                        ?.GetValue(null);
                }
            }
            return null;
        }

        /// <summary>
        /// Dev autostart (<c>PAL3_AUTOSTART=1</c>): skip the JSON start menu
        /// and enter the game exactly like <c>MainMenu.StartNewGame()</c> does. This is
        /// the headless verifier for the port.
        /// </summary>
        private static void OnGameSingletonReady()
        {
            IsGameReady = true;
            // Pal3Game.Update/LateUpdate are dispatched by GameEntityScriptRuntime's
            // active-script loop (Activate registered the singleton); no extra hook.

            if (Environment.GetEnvironmentVariable("PAL3_AUTOSTART") == "1" && StartNewGameNow())
            {
                AutoDialogueEnabled = true;
                return;
            }

            // APal3Driver presents the JSON UMG start menu when boot is ready.
            WaitingForMenuStart = true;
        }

        /// <summary>Set while the minimal start menu is waiting for input.</summary>
        public static bool WaitingForMenuStart { get; private set; }
        public static bool IsGameReady { get; private set; }
        public static bool AutoDialogueEnabled { get; set; }

        public static bool HasPlayerControl()
        {
            if (!IsGameReady || WaitingForMenuStart) return false;
            var services = ServiceLocator.Instance;
            var player = services.Get<PlayerActorManager>();
            return services.Get<GameStateManager>().GetCurrentState() == GameState.Gameplay &&
                !services.Get<SceneManager>().IsLoading &&
                player.IsPlayerInputEnabled() && player.IsPlayerActorControlEnabled();
        }

        /// <summary>
        /// Same sequence as upstream MainMenu's "新的游戏" button: ResetGameStateCommand
        /// first (clears LastKnownPosition etc. so the camera doesn't keep following the
        /// (77,77,77) parking spot), then main story var reset, init script, team actor,
        /// Cutscene state.
        /// </summary>
        public static bool StartNewGameNow()
        {
            try
            {
                // Mirror the upstream "新的游戏" button handler: HideMenu() first — it
                // clears _isInInitView and cancels the init-view camera orbit, which
                // otherwise keeps firing CameraOrbitHorizontalCommand behind the
                // cutscene's back and yanks the camera to the menu orbit position.
                ServiceLocator.Instance.Get<MainMenu>().HideMenu();

                // Kill any in-flight init-view camera animation (CameraOrbitHorizontal
                // runs 20 s and would otherwise land its final write mid-cutscene).
                Pal3Game.Instance.Execute(new CameraFollowPlayerCommand(0));

                Pal3Game.Instance.Execute(new ResetGameStateCommand());
                ServiceLocator.Instance.Get<IUserVariableStore<ushort, int>>()
                    .Set(ScriptConstants.MainStoryVariableId, 0);
                ServiceLocator.Instance.Get<ScriptManager>()
                    .AddScript(ScriptConstants.InitScriptId);
                ServiceLocator.Instance.Get<TeamManager>().AddActor(0);
                bool ok = ServiceLocator.Instance.Get<GameStateManager>()
                    .TryGoToState(GameState.Cutscene);
                Debug.Log($"[Pal3Boot] start: reset + main story var + init script {ScriptConstants.InitScriptId} "
                          + $"+ team actor 0 -> Cutscene: {ok}");
                return ok;
            }
            catch (Exception e)
            {
                Debug.LogError("[Pal3Boot] start failed: " + e);
                return false;
            }
        }

        public static void StartFromMenu()
        {
            if (!StartNewGameNow())
            {
                throw new InvalidOperationException("无法启动开场剧情，请查看 PAL3 日志。");
            }
            WaitingForMenuStart = false;
        }

        public static void StartFromCheckpoint(string name)
        {
            var commands = CommandText.Parse(DevCommands.StoryJumpPoints[name]);
            foreach (var command in commands)
            {
                if (command is SceneLoadCommand scene)
                {
                    CheckSceneResources(scene.SceneCityName, scene.SceneName);
                }
            }

            var services = ServiceLocator.Instance;
            services.Get<MainMenu>().HideMenu();
            services.Get<SaveManager>().IsAutoSaveEnabled = false;
            Pal3Game.Instance.Execute(new CameraFollowPlayerCommand(0));
            Pal3Game.Instance.Execute(new ResetGameStateCommand());
            services.Get<GameStateManager>().TryGoToState(GameState.Cutscene);
            services.Get<SceneManager>().RunSceneCommands(commands);
            WaitingForMenuStart = false;
        }

        public static void SwitchScene(string city, string scene, int tileX, int tileY)
        {
            CheckSceneResources(city, scene);
            var game = Pal3Game.Instance;
            if (WaitingForMenuStart)
            {
                ServiceLocator.Instance.Get<MainMenu>().HideMenu();
                game.Execute(new CameraFollowPlayerCommand(0));
                game.Execute(new ResetGameStateCommand());
                ServiceLocator.Instance.Get<TeamManager>().AddActor(0);
            }
            game.Execute(new PlayerEnableInputCommand(0));
            ServiceLocator.Instance.Get<SceneManager>().RunSceneCommands(
            [
                new SceneLoadCommand(city, scene),
                new ActorActivateCommand(-1, 1),
                new ActorSetNavLayerCommand(-1, 0),
                new ActorSetTilePositionCommand(-1, tileX, tileY),
                new ActorEnablePlayerControlCommand(-1),
                new CameraFollowPlayerCommand(1),
                new CameraFadeInCommand(),
                new PlayerEnableInputCommand(1)
            ]);
            WaitingForMenuStart = false;
        }

        public static void LoadFromSlot(int slot)
        {
            var services = ServiceLocator.Instance;
            var saves = services.Get<SaveManager>();
            string data = saves.LoadFromSaveSlot(slot)
                ?? throw new InvalidOperationException($"存档 {slot} 为空。");
            saves.Restore(data, () =>
            {
                services.Get<MainMenu>().HideMenu();
                Pal3Game.Instance.Execute(new CameraFollowPlayerCommand(0));
            });
            WaitingForMenuStart = false;
        }

        public static void SaveToSlot(int slot)
        {
            if (slot is < 1 or > SaveManager.ManualSlotCount)
                throw new ArgumentOutOfRangeException(nameof(slot));
            var services = ServiceLocator.Instance;
            var player = services.Get<Pal3.Game.GamePlay.PlayerActorManager>();
            if (services.Get<GameStateManager>().GetCurrentState() != GameState.Gameplay ||
                services.Get<ScriptManager>().GetNumberOfRunningScripts() != 0 ||
                !player.IsPlayerInputEnabled() || !player.IsPlayerActorControlEnabled())
            {
                throw new InvalidOperationException("只能在可操作状态存档。");
            }
            var saves = services.Get<SaveManager>();
            var commands = saves.ConvertCurrentGameStateToCommands(SaveLevel.Full)
                ?? throw new InvalidOperationException("当前没有可存档的 PAL3 场景。");
            if (!saves.SaveGameStateToSlot(slot, commands))
                throw new System.IO.IOException($"写入存档 {slot} 失败。");
        }

        internal static void CheckSceneResources(string city, string scene)
        {
            var resources = ServiceLocator.Instance.Get<GameResourceProvider>();
            resources.GetGameResourceFile<ScnFile>(FileConstants.GetScnFileVirtualPath(city, scene));
            resources.GetGameResourceFile<SceFile>(FileConstants.GetSceneSceFileVirtualPath(city));
        }
    }
}
