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

        /// <summary>Advances every live routine one step. Call once per frame, game thread only.</summary>
        public static void Tick(float deltaTime)
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
    /// Unity-MonoBehaviour-style lifecycle dispatcher for <see cref="GameEntityScript"/>.
    /// Ported scripts keep their original Unity magic methods (private void Awake/OnEnable/
    /// Start/Update/LateUpdate/OnDisable); those are located via a per-type reflection
    /// cache and invoked here. The port's renamed virtuals (Init, OnUpdateGameEntity, ...)
    /// are dispatched as well.
    /// </summary>
    public static class GameEntityScriptRuntime
    {
        private sealed class ScriptInfo
        {
            public GameEntityScript Script = null!;
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
        private static readonly Dictionary<Type, MethodCache> _cache = new();
        private static bool _tickedOnce;

        private const BindingFlags LifecycleFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>
        /// Registers a script and runs its Unity-style startup lifecycle:
        /// Awake -> OnEnable -> Start (a coroutine Start is fed to the scheduler).
        /// </summary>
        public static void Activate(GameEntityScript script)
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

                InvokeSafe(script, cache.Awake, "Awake");
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

        private static bool MonitorIsHeldByCurrentThread() => System.Threading.Monitor.IsEntered(_gate);

        private static IEnumerator DeferredStart(GameEntityScript script, MethodInfo start)
        {
            RunStart(script, start);
            yield break;
        }

        private static void RunStart(GameEntityScript script, MethodInfo start)
        {
            object? result = InvokeSafe(script, start, "Start");
            if (result is IEnumerator routine)
            {
                CoroutineScheduler.Run(routine, script);
            }
        }        /// <summary>
        /// Per-frame dispatch: Unity Time state first, then the coroutine pump,
        /// then Update and LateUpdate on every active script.
        /// Call once per frame, game thread only.
        /// </summary>
        public static void TickAll(float deltaTime)
        {
            // Unity semantics: Time.deltaTime is timeScale-scaled.
            float scaledDelta = deltaTime * UnityEngine.Time.timeScale;
            UnityEngine.Time.unscaledDeltaTime = deltaTime;
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

                        if (!si.Script.enabled)
                        {
                            continue; // disabled scripts don't tick (Unity semantics)
                        }

                        InvokeSafe(si.Script, si.Update, "Update");
                        si.Script.InvokeVirtualOnUpdate(deltaTime);

                        InvokeSafe(si.Script, si.LateUpdate, "LateUpdate");
                        si.Script.InvokeVirtualOnLateUpdate(deltaTime);
                    }
                }
                finally
                {
                    _tickedOnce = false;
                }
            }
        }

        /// <summary>Runs OnDisable, stops the script's coroutines and unregisters it.</summary>
        public static void Deactivate(GameEntityScript script)
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
            script.InvokeVirtualOnDisable();
            CoroutineScheduler.StopAll(script);
        }

        /// <summary>Returns the method's return value, or null when it threw (error logged).</summary>
        private static object? InvokeSafe(GameEntityScript script, MethodInfo? method, string name)
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
    }
}
