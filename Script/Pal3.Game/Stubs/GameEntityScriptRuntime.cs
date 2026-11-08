// Runtime host for managed game-entity scripts.
//
// Replaces Unity's two engine-side services the port depends on:
//   1. CoroutineScheduler      - frame-driven replacement for StartCoroutine.
//   2. GameEntityScriptRuntime - Unity MonoBehaviour lifecycle dispatch
//                                (Awake/OnEnable/Start/Update/LateUpdate/OnDisable)
//                                via cached reflection, plus the port's renamed
//                                virtuals (Init/OnUpdateGameEntity/...).
//
// Both are pumped by the UE-side driver actor (APal3Driver.Tick in
// ManagedSharpDemo), once per engine frame on the game thread.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Pal3.Game;

namespace Engine.Core.Implementation
{
    /// <summary>
    /// Frame-driven coroutine scheduler replacing Unity's StartCoroutine engine.
    /// Supported yield operands:
    ///   - null                      -> resume next frame
    ///   - Task                      -> resume when the task completes (polled per frame)
    ///   - float                     -> resume after N (scaled) seconds
    ///   - CustomYieldInstruction    -> resume when keepWaiting turns false
    ///                                   (covers WaitForSeconds, WaitUntilScriptFinished, ...)
    ///   - IEnumerator               -> nested coroutine (caller resumes when it finishes)
    ///   - anything else             -> resume next frame (defensive)
    /// </summary>
    public static class CoroutineScheduler
    {
        private sealed class Routine
        {
            public IEnumerator Current;                              // innermost running enumerator
            public readonly Stack<IEnumerator> Callers = new();      // outer enumerators, nearest caller on top
            public bool Alive = true;
            public float WaitSeconds;                                // > 0: waiting for time
            public Task? WaitTask;                                   // non-null: waiting for task
            public CustomYieldInstruction? WaitInstruction;          // non-null: waiting for keepWaiting == false
            public object? Owner;                                    // originating script (StopCoroutine support)
        }

        private static readonly object _gate = new();
        private static readonly List<Routine> _routines = new();

        /// <summary>Number of live coroutines (diagnostics only).</summary>
        public static int ActiveCount { get { lock (_gate) return _routines.Count; } }
        private static readonly List<Routine> _pending = new();       // registered during Tick; started next Tick
        private static readonly Dictionary<IEnumerator, Routine> _byEnumerator = new();

        public static UnityEngine.Coroutine Run(IEnumerator routine, object? owner = null)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));
            Routine r = new() { Current = routine, Owner = owner };
            lock (_gate) { _pending.Add(r); }
            return new UnityEngine.Coroutine() { Handle = r };
        }

        public static void Stop(IEnumerator routine)
        {
            if (routine == null) return;
            lock (_gate)
            {
                if (_byEnumerator.TryGetValue(routine, out Routine? r)) r.Alive = false;
            }
        }

        public static void Stop(UnityEngine.Coroutine coroutine)
        {
            if (coroutine?.Handle is Routine r) r.Alive = false;
        }

        public static void StopAll(object owner)
        {
            if (owner == null) return;
            lock (_gate)
            {
                foreach (Routine r in _routines) if (Equals(owner, r.Owner)) r.Alive = false;
                foreach (Routine r in _pending) if (Equals(owner, r.Owner)) r.Alive = false;
            }
        }

        /// <summary>
        /// Drops every routine (PIE session reset). Stale coroutines capture actors of
        /// the previous world; stepping one after a PIE restart calls into freed memory.
        /// </summary>
        public static void ResetForNewSession()
        {
            lock (_gate)
            {
                _routines.Clear();
                _pending.Clear();
                _byEnumerator.Clear();
            }
        }

        /// <summary>Advances every live routine one step. Call once per frame, game thread only.</summary>
        public static void Tick(float deltaTime)
        {
            long tickStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                TickInternal(deltaTime);
            }
            finally
            {
                double totalMs = (System.Diagnostics.Stopwatch.GetTimestamp() - tickStart) * 1000.0
                                 / System.Diagnostics.Stopwatch.Frequency;
                if (totalMs > 100.0)
                {
                    Engine.Diagnostics.ManagedDiagnostics.ReportSlow("CoroutineScheduler.Tick", totalMs);
                }
            }
        }

        private static void TickInternal(float deltaTime)
        {
            lock (_gate)
            {
                if (_pending.Count > 0)
                {
                    foreach (Routine r in _pending)
                    {
                        _routines.Add(r);
                        _byEnumerator[r.Current] = r;
                    }
                    _pending.Clear();
                }

                // Reverse iteration: routines started during this pass append to
                // _pending and are not stepped until next frame (Unity semantics).
                for (int i = _routines.Count - 1; i >= 0; i--)
                {
                    Routine r = _routines[i];

                    if (!r.Alive)
                    {
                        RemoveAt(i, r);
                        continue;
                    }

                    // Honor active waits.
                    if (r.WaitSeconds > 0f)
                    {
                        r.WaitSeconds -= deltaTime;
                        if (r.WaitSeconds > 0f) continue;
                        r.WaitSeconds = 0f;
                    }
                    if (r.WaitTask is { IsCompleted: false }) continue;
                    r.WaitTask = null;
                    if (r.WaitInstruction != null)
                    {
                        if (r.WaitInstruction.keepWaiting) continue;
                        r.WaitInstruction = null;
                    }

                    try
                    {
                        Advance(r);
                    }
                    catch (Exception ex)
                    {
                        r.Alive = false;
                        UnityEngine.Debug.LogError($"[CoroutineScheduler] Coroutine threw and was stopped: {ex}");
                    }

                    if (!r.Alive) RemoveAt(i, r);
                }
            }
        }

        private static void Advance(Routine r)
        {
            while (true)
            {
                bool moved = r.Current.MoveNext();

                if (moved)
                {
                    object? current = r.Current.Current;

                    if (current == null) return;                       // next frame
                    if (current is Task task) { r.WaitTask = task; return; }
                    if (current is float seconds) { r.WaitSeconds = seconds; return; }
                    if (current is CustomYieldInstruction instruction) { r.WaitInstruction = instruction; return; }
                    if (current is IEnumerator inner)                  // nested coroutine
                    {
                        r.Callers.Push(r.Current);
                        _byEnumerator[inner] = r;
                        r.Current = inner;
                        return;                                        // step the inner one next frame
                    }
                    return;                                            // unknown operand: next frame
                }

                // Current enumerator finished. Resume its caller immediately
                // (Unity semantics: outer resumes the same frame the inner ends).
                if (r.Callers.Count > 0)
                {
                    _byEnumerator.Remove(r.Current);
                    r.Current = r.Callers.Pop();
                    continue;
                }

                r.Alive = false;                                       // whole chain done
                return;
            }
        }

        private static void RemoveAt(int index, Routine r)
        {
            _routines.RemoveAt(index);
            _byEnumerator.Remove(r.Current);
            foreach (IEnumerator e in r.Callers) _byEnumerator.Remove(e);
        }
    }

    /// <summary>
    /// Unity-MonoBehaviour-style lifecycle dispatcher. Primary target is
    /// <see cref="GameEntityScript"/>, but arbitrary "component" objects that keep
    /// the Unity magic methods (private Awake/OnEnable/Start/Update/LateUpdate/
    /// OnDisable - e.g. Pal3Game, which derives from Singleton instead of
    /// GameEntityScript) are dispatched the same way. Methods are located via a
    /// per-type reflection cache; the port's renamed virtuals (Init,
    /// OnUpdateGameEntity, ...) are dispatched as well on GameEntityScript types.
    /// </summary>
    public static class GameEntityScriptRuntime
    {
        private sealed class ScriptInfo
        {
            public object Script = null!;
            public MethodInfo? Awake;
            public MethodInfo? OnEnable;
            public MethodInfo? Start;
            public MethodInfo? Update;
            public MethodInfo? LateUpdate;
            public MethodInfo? OnDisable;
        }

        private sealed class MethodCache
        {
            public MethodInfo? Awake, OnEnable, Start, Update, LateUpdate, OnDisable;
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

        /// <summary>
        /// Drops all active scripts and the Pal3Game tick hooks (PIE session reset).
        /// Does not dispatch OnDisable: the owning UE world is already gone by the time
        /// the next session starts. The method cache is type-keyed and stays valid.
        /// </summary>
        public static void ResetForNewSession()
        {
            lock (_gate)
            {
                _active.Clear();
                _tickedOnce = false;
            }
        }

        private const BindingFlags LifecycleFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>
        /// Registers a script/component and runs its Unity-style startup lifecycle:
        /// AutoFillStubFields -> Awake -> OnEnable -> Start (a coroutine Start is
        /// fed to the scheduler).
        /// </summary>
        public static void Activate(object script)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));

            MethodCache cache = GetCache(script.GetType());

            lock (_gate)
            {
                foreach (ScriptInfo info in _active)
                {
                    if (ReferenceEquals(info.Script, script)) return; // already active
                }

                ScriptInfo si = new()
                {
                    Script = script,
                    Awake = cache.Awake,
                    OnEnable = cache.OnEnable,
                    Start = cache.Start,
                    Update = cache.Update,
                    LateUpdate = cache.LateUpdate,
                    OnDisable = cache.OnDisable,
                };
                _active.Add(si);

                AutoFillStubFields(script);
                InvokeSafe(script, cache.Awake, "Awake");
                InvokeSafe(script, cache.OnEnable, "OnEnable");
                // Unity's OnEnable also drives the GameEntityScript virtual (upstream
                // wires it in a private OnEnable method; the port's base class cannot
                // declare one, so the runtime dispatches it explicitly).
                if (script is GameEntityScript ges) ges.InvokeVirtualOnEnable();
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
                CoroutineScheduler.Run(routine, script);
            }
        }

        /// <summary>
        /// Per-frame dispatch: Unity Time state first, then the coroutine pump,
        /// then Update and LateUpdate on every active script.
        /// Call once per frame, game thread only.
        /// </summary>
        // Low frame rates (e.g. UE throttling PIE to ~3 FPS while the editor is in
        // the background) deliver huge deltas. Feeding them straight into gameplay
        // makes a 4 m/s actor step 1.33 m per frame - a 1.2 m tile path finishes in
        // one frame and the walk action is replaced before a single new pose can be
        // displayed. Clamping the simulation delta under load applies time dilation
        // instead: movement stays proportional to the (clamped) delta and the
        // animation advances one step per rendered frame. At normal frame rates this
        // has no effect. Override with PAL3_MAX_SIM_DELTA (seconds; <=0 disables).
        private static readonly float s_MaxSimulationDelta = ReadMaxSimulationDelta();

        private static float ReadMaxSimulationDelta()
        {
            string? raw = System.Environment.GetEnvironmentVariable("PAL3_MAX_SIM_DELTA");
            if (!string.IsNullOrWhiteSpace(raw) &&
                float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed))
            {
                return parsed;
            }
            return 0.05f; // 20 Hz minimum simulation step
        }

        public static void TickAll(float deltaTime)
        {
            float simulationDelta = deltaTime;
            if (s_MaxSimulationDelta > 0f && simulationDelta > s_MaxSimulationDelta)
            {
                simulationDelta = s_MaxSimulationDelta;
            }

            // Unity semantics: Time.deltaTime is timeScale-scaled.
            float scaledDelta = simulationDelta * UnityEngine.Time.timeScale;
            UnityEngine.Time.unscaledDeltaTime = simulationDelta;
            UnityEngine.Time.deltaTime = scaledDelta;
            UnityEngine.Time.time += scaledDelta;
            UnityEngine.Time.frameCount++;

            CoroutineScheduler.Tick(scaledDelta);

            lock (_gate)
            {
                _tickedOnce = true;
                try
                {
                    for (int i = 0; i < _active.Count; i++)
                    {
                        ScriptInfo si = _active[i];

                        // Disabled GameEntityScripts don't tick (Unity semantics);
                        // plain components always tick.
                        if (si.Script is GameEntityScript { enabled: false })
                        {
                            continue;
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
                        if (si.Script is GameEntityScript script)
                        {
                            script.InvokeVirtualOnUpdate(scaledDelta);
                        }

                    }

                    // Unity runs all LateUpdates after all Updates so the camera
                    // follow (Pal3Game.LateUpdate -> CameraManager.LateUpdate) sees
                    // every position written this frame.
                    for (int i = 0; i < _active.Count; i++)
                    {
                        ScriptInfo si = _active[i];
                        if (si.Script is GameEntityScript { enabled: false })
                        {
                            continue;
                        }
                        InvokeSafe(si.Script, si.LateUpdate, "LateUpdate");
                        if (si.Script is GameEntityScript lateScript)
                        {
                            lateScript.InvokeVirtualOnLateUpdate(scaledDelta);
                        }
                    }
                }
                finally
                {
                    _tickedOnce = false;
                }
            }
        }

        /// <summary>Runs OnDisable, stops the component's coroutines and unregisters it.</summary>
        public static void Deactivate(object script)
        {
            if (script == null) return;

            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (ReferenceEquals(_active[i].Script, script))
                    {
                        _active.RemoveAt(i);
                        break;
                    }
                }
            }

            InvokeSafe(script, GetCache(script.GetType()).OnDisable, "OnDisable");
            if (script is GameEntityScript ges) ges.InvokeVirtualOnDisable();
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
            };

            // Override dispatch for OnUpdateGameEntity/OnLateUpdateGameEntity/OnDisableGameEntity
            // is handled by GameEntityScript's internal bridge methods.

            lock (_gate) { _cache[type] = cache; }
            return cache;
        }

        private static MethodInfo? FindZeroArg(Type type, string name)
        {
            MethodInfo? method = type.GetMethod(name, LifecycleFlags);
            return method != null && method.GetParameters().Length == 0 && !method.IsAbstract
                ? method
                : null;
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
        private static void AutoFillStubFields(object script)
        {
            Type type = script.GetType();
            for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly) continue;
                    Type fieldType = field.FieldType;
                    if (!IsAutoFillable(fieldType)) continue;

                    object? current = field.GetValue(script);
                    if (current != null) continue;

                    try
                    {
                        object instance = Activator.CreateInstance(fieldType)!;
                        field.SetValue(script, instance);

                        // The game's main camera is a stub; give it the real actor so
                        // Pal3Game's camera entity drives the UE camera (Pal3Boot).
                        if (instance is UnityEngine.Camera cameraStub)
                        {
                            cameraStub.gameObject.UeActor = Pal3.Game.Pal3Boot.CameraActor;
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
