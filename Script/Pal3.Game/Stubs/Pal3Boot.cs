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
                        // Singleton-style component: private ctor is allowed.
                        object component = Activator.CreateInstance(scriptType, nonPublic: true)!;
                        GameEntityScriptRuntime.Activate(component);
                    }
                }
                return new GameObject(prefab.name);
            }
            return null;
        }
    }
}
