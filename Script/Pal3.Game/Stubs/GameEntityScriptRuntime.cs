// Runtime host for managed game-entity scripts.
//
// The UE driver pumps lifecycle callbacks and the Engine coroutine scheduler
// once per frame on the game thread.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Engine.Core.Implementation
{
    /// <summary>
    /// Unity-MonoBehaviour-style lifecycle dispatcher. Primary target is
    /// <see cref="GameEntityScript"/>, but arbitrary "component" objects that keep
    /// Unity messages, such as the PAL3 singleton, are also supported. The
    /// per-type cache resolves inherited private Unity messages.
    /// </summary>
    public static class GameEntityScriptRuntime
    {
        private sealed class ScriptInfo
        {
            public object Script = null!;
            public MethodInfo? OnEnable;
            public MethodInfo? Start;
            public MethodInfo? Update;
            public MethodInfo? LateUpdate;
            public MethodInfo? OnDisable;
            public Action? EnabledChanged;
            public bool BehaviourEnabled;
            public int FirstUpdateFrame;
            public bool Removed;
        }

        private sealed class MethodCache
        {
            public MethodInfo? Awake, OnEnable, Start, Update, LateUpdate, OnDisable, OnDestroy;
        }

        private static readonly object _gate = new();
        private static readonly List<ScriptInfo> _active = new();

        // Crash triage: PAL3_TRACE_UPDATES=1 logs every script Update so the last line
        // before a native access violation identifies the culprit.
        private static readonly bool s_TraceUpdates =
            System.Environment.GetEnvironmentVariable("PAL3_TRACE_UPDATES") == "1";

        /// <summary>Number of active scripts (diagnostics only).</summary>
        public static int ActiveScriptCount { get { lock (_gate) return _active.Count; } }
        private static readonly Dictionary<Type, MethodCache> _cache = new();
        private static bool _tickedOnce;
        private static bool _needsCompaction;

        /// <summary>
        /// Drops all active scripts and the Pal3 tick hooks (PIE session reset).
        /// Does not dispatch OnDisable: the owning UE world is already gone by the time
        /// the next session starts. The method cache is type-keyed and stays valid.
        /// </summary>
        public static void ResetForNewSession()
        {
            lock (_gate)
            {
                foreach (ScriptInfo info in _active)
                {
                    if (info.Script is MonoBehaviour behaviour && info.EnabledChanged != null)
                        behaviour.EnabledChanged -= info.EnabledChanged;
                }
                _active.Clear();
                _tickedOnce = false;
                _needsCompaction = false;
            }
        }

        private const BindingFlags LifecycleFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>
        /// Registers a script/component and runs its Unity-style startup lifecycle:
        /// AutoFillStubFields -> Awake -> OnEnable -> Start (a coroutine Start is
        /// fed to the scheduler).
        /// </summary>
        public static void Activate(object script, GameObject? componentHost = null)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));

            MethodCache cache = GetCache(script.GetType());

            lock (_gate)
            {
                foreach (ScriptInfo info in _active)
                {
                    if (!info.Removed && ReferenceEquals(info.Script, script)) return;
                }

                ScriptInfo si = new()
                {
                    Script = script,
                    OnEnable = cache.OnEnable,
                    Start = cache.Start,
                    Update = cache.Update,
                    LateUpdate = cache.LateUpdate,
                    OnDisable = cache.OnDisable,
                };
                _active.Add(si);

                // Preserve the port's serialized field setup for PAL3 scripts.
                if (script is not MonoBehaviour || script is GameEntityScript)
                    AutoFillStubFields(script, componentHost);
                InvokeSafe(script, cache.Awake, "Awake");

                if (script is MonoBehaviour behaviour)
                {
                    si.FirstUpdateFrame = UnityEngine.Time.frameCount + 1;
                    if (!si.Removed)
                    {
                        si.EnabledChanged = () => UpdateBehaviourEnabled(si, behaviour);
                        behaviour.EnabledChanged += si.EnabledChanged;
                        UpdateBehaviourEnabled(si, behaviour);
                    }
                    return;
                }

                InvokeSafe(script, cache.OnEnable, "OnEnable");
            }

            // Start runs right before the first Update in Unity; when activating
            // from a non-tick context (entity creation) this is close enough.
            // When activating DURING TickAll, defer Start to the next frame to
            // match Unity (Start never fires mid-Update-scan).
            if (cache.Start != null)
            {
                if (_tickedOnce && MonitorIsHeldByCurrentThread())
                {
                    CoroutineScheduler.Run(DeferredStart(script, cache.Start), script);
                }
                else
                {
                    RunStart(script, cache.Start);
                }
            }
        }

        private static void UpdateBehaviourEnabled(ScriptInfo info, MonoBehaviour behaviour)
        {
            bool enabled = behaviour.isActiveAndEnabled;
            if (info.Removed || info.BehaviourEnabled == enabled) return;
            info.BehaviourEnabled = enabled;
            if (enabled) InvokeSafe(behaviour, info.OnEnable, "OnEnable");
            else InvokeSafe(behaviour, info.OnDisable, "OnDisable");
        }

        private static bool MonitorIsHeldByCurrentThread() => System.Threading.Monitor.IsEntered(_gate);

        private static IEnumerator DeferredStart(object script, MethodInfo start)
        {
            RunStart(script, start);
            yield break;
        }

        private static void RunStart(object script, MethodInfo start)
        {
            object? result = InvokeSafe(script, start, "Start");
            if (result is IEnumerator routine)
            {
                if (script is MonoBehaviour behaviour) behaviour.StartCoroutine(routine);
                else CoroutineScheduler.Run(routine, script);
            }
        }

        /// <summary>
        /// Per-frame dispatch: Unity Time state first, then the coroutine pump,
        /// then Update and LateUpdate on every active script.
        /// Call once per frame, game thread only.
        /// </summary>
        public static void TickAll(float deltaTime, float realDeltaTime)
        {
            // UE has already applied World and Actor Time Dilation to the tick.
            UnityEngine.Time.unscaledDeltaTime = realDeltaTime;
            UnityEngine.Time.deltaTime = deltaTime;
            UnityEngine.Time.time += deltaTime;
            UnityEngine.Time.frameCount++;

            CoroutineScheduler.Tick(deltaTime);

            lock (_gate)
            {
                _tickedOnce = true;
                try
                {
                    for (int i = 0; i < _active.Count; i++)
                    {
                        ScriptInfo si = _active[i];

                        if (si.Removed) continue;

                        if (si.Script is MonoBehaviour)
                        {
                            if (!si.BehaviourEnabled || UnityEngine.Time.frameCount < si.FirstUpdateFrame) continue;
                            if (si.Start is { } start)
                            {
                                si.Start = null;
                                RunStart(si.Script, start);
                                if (!si.BehaviourEnabled || si.Removed) continue;
                            }
                        }

                        // Crash triage: with PAL3_TRACE_UPDATES=1 the last logged name
                        // before an access violation identifies the culprit (managed
                        // exceptions are caught, native AVs are not).
                        if (s_TraceUpdates)
                        {
                            UnityEngine.Debug.Log($"[TickTrace] {si.Script.GetType().Name}.Update");
                        }
                        long __t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        InvokeSafe(si.Script, si.Update, "Update");
                        double __ms = (System.Diagnostics.Stopwatch.GetTimestamp() - __t0) * 1000.0
                                      / System.Diagnostics.Stopwatch.Frequency;
                        if (__ms > 100.0)
                        {
                            Engine.Diagnostics.ManagedDiagnostics.ReportSlow(
                                $"{si.Script.GetType().Name}.Update", __ms);
                        }
                    }

                    // Unity runs all LateUpdates after all Updates so the camera
                    // follow (Pal3.LateUpdate -> CameraManager.LateUpdate) sees
                    // every position written this frame.
                    for (int i = 0; i < _active.Count; i++)
                    {
                        ScriptInfo si = _active[i];
                        if (si.Removed || (si.Script is MonoBehaviour &&
                            (!si.BehaviourEnabled || si.Start != null || UnityEngine.Time.frameCount < si.FirstUpdateFrame)))
                        {
                            continue;
                        }
                        InvokeSafe(si.Script, si.LateUpdate, "LateUpdate");
                    }
                }
                finally
                {
                    if (_needsCompaction)
                    {
                        _active.RemoveAll(static info => info.Removed);
                        _needsCompaction = false;
                    }
                    _tickedOnce = false;
                }
            }
        }

        /// <summary>Runs OnDisable, stops the component's coroutines and unregisters it.</summary>
        public static void Deactivate(object script)
        {
            if (script == null) return;

            ScriptInfo? removed = null;
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    ScriptInfo info = _active[i];
                    if (!info.Removed && ReferenceEquals(info.Script, script))
                    {
                        removed = info;
                        info.Removed = true;
                        if (_tickedOnce) _needsCompaction = true;
                        else _active.RemoveAt(i);
                        break;
                    }
                }
            }

            if (script is MonoBehaviour behaviour)
            {
                if (removed == null) return;
                if (removed.EnabledChanged != null) behaviour.EnabledChanged -= removed.EnabledChanged;
                if (removed.BehaviourEnabled) InvokeSafe(behaviour, removed.OnDisable, "OnDisable");
                InvokeSafe(behaviour, GetCache(behaviour.GetType()).OnDestroy, "OnDestroy");
                behaviour.DetachFromGameEntity();
                CoroutineScheduler.StopAll(behaviour);
                return;
            }

            InvokeSafe(script, GetCache(script.GetType()).OnDisable, "OnDisable");
            CoroutineScheduler.StopAll(script);
        }

        /// <summary>Returns the method's return value, or null when it threw (error logged).</summary>
        private static object? InvokeSafe(object script, MethodInfo? method, string name)
        {
            if (method == null) return null;
            try
            {
                return method.Invoke(script, null);
            }
            catch (TargetInvocationException tie)
            {
                UnityEngine.Debug.LogError($"[GameEntityScriptRuntime] {script.GetType().Name}.{name} threw: {tie.InnerException}");
                return null;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[GameEntityScriptRuntime] {script.GetType().Name}.{name} threw: {ex}");
                return null;
            }
        }

        private static MethodCache GetCache(Type type)
        {
            lock (_gate)
            {
                if (_cache.TryGetValue(type, out MethodCache? cached)) return cached;
            }

            MethodCache cache = new()
            {
                Awake = FindZeroArg(type, "Awake"),
                OnEnable = FindZeroArg(type, "OnEnable"),
                Start = FindZeroArg(type, "Start"),
                Update = FindZeroArg(type, "Update"),
                LateUpdate = FindZeroArg(type, "LateUpdate"),
                OnDisable = FindZeroArg(type, "OnDisable"),
                OnDestroy = FindZeroArg(type, "OnDestroy"),
            };

            lock (_gate) { _cache[type] = cache; }
            return cache;
        }

        private static MethodInfo? FindZeroArg(Type type, string name)
        {
            for (Type? current = type; current != null; current = current.BaseType)
            {
                MethodInfo? method = current.GetMethod(name, LifecycleFlags | BindingFlags.DeclaredOnly,
                    binder: null, Type.EmptyTypes, modifiers: null);
                if (method is { IsAbstract: false }) return method;
            }
            return null;
        }

        /// <summary>
        /// Namespaces whose types are stub/compat shims. Null fields of these types
        /// are auto-instantiated before Awake so ported code that assumes a
        /// Unity-[SerializeField] was wired up in the editor doesn't NRE.
        /// </summary>
        private static readonly string[] AutoFillNamespaces =
        {
            "UnityEngine",
            "TMPro",
            "Engine.UI",
        };

        /// <summary>
        /// Reflects over the script's instance fields and news up any null field
        /// whose type lives in a stub namespace and has a parameterless constructor.
        /// Real wired components (already non-null) are left untouched.
        /// </summary>
        private static void AutoFillStubFields(object script, GameObject? componentHost)
        {
            Type type = script.GetType();
            for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly) continue;
                    Type fieldType = field.FieldType;
                    bool isBehaviour = typeof(MonoBehaviour).IsAssignableFrom(fieldType);
                    bool useHost = isBehaviour && componentHost != null && !fieldType.IsAbstract &&
                                   (field.IsPublic || field.IsDefined(typeof(SerializeField), false));
                    if (!useHost && (isBehaviour || !IsAutoFillable(fieldType))) continue;

                    object? current = field.GetValue(script);
                    if (current != null) continue;

                    try
                    {
                        // UnrealSharp portability. Supply Unity-style prefab references via
                        // the component host so their lifecycle starts with the game.
                        object? instance = useHost
                            ? componentHost!.AddComponent(fieldType)
                            : Activator.CreateInstance(fieldType);
                        field.SetValue(script, instance);

                        // The game's main camera is a stub; give it the real actor so
                        // Pal3's camera entity drives the UE camera (Pal3Boot).
                        if (instance is UnityEngine.Camera cameraStub)
                        {
                            cameraStub.gameObject.UeActor = global::Pal3.Game.Pal3Boot.CameraActor;
                        }
                    }
                    catch (Exception)
                    {
                        // No parameterless ctor or similar - leave null, code will
                        // handle it (or the NRE gets logged by InvokeSafe).
                    }
                }
            }
        }

        private static bool IsAutoFillable(Type type)
        {
            if (type.IsPrimitive || type.IsEnum) return false;
            if (type.IsArray) return false;
            if (type.IsAbstract || type.IsInterface) return false;
            if (type.IsGenericType) return false;

            string ns = type.Namespace ?? "";
            foreach (string allowed in AutoFillNamespaces)
            {
                if (ns == allowed || ns.StartsWith(allowed + ".", StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
