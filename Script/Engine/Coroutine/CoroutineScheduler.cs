using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

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

        // Unity MonoBehaviours execute the first step on the calling frame.
        // Register before stepping so a callback can stop itself or its owner.
        public static UnityEngine.Coroutine RunImmediately(IEnumerator routine, object owner)
        {
            ArgumentNullException.ThrowIfNull(routine);
            Routine r = new() { Current = routine, Owner = owner };
            lock (_gate)
            {
                _pending.Add(r);
                _byEnumerator[routine] = r;
                Step(r, startNestedImmediately: true);
            }
            return new UnityEngine.Coroutine() { Handle = r };
        }

        public static void Stop(IEnumerator routine)
        {
            if (routine == null) return;
            lock (_gate)
            {
                if (_byEnumerator.TryGetValue(routine, out Routine? r)) r.Alive = false;
                foreach (Routine pending in _pending)
                {
                    if (ReferenceEquals(pending.Current, routine)) pending.Alive = false;
                }
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

                // Reverse iteration keeps new routines out of this pass.
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
                    Step(r);

                    if (!r.Alive) RemoveAt(i, r);
                }
            }
        }

        private static void Step(Routine r, bool startNestedImmediately = false)
        {
            try
            {
                if (r.WaitInstruction is { } instruction && instruction.keepWaiting) return;
                r.WaitInstruction = null;
                Advance(r, startNestedImmediately);
            }
            catch (Exception ex)
            {
                r.Alive = false;
                UnityEngine.Debug.LogError($"[CoroutineScheduler] Coroutine threw and was stopped: {ex}");
            }
        }

        private static void Advance(Routine r, bool startNestedImmediately)
        {
            while (r.Alive)
            {
                bool moved = r.Current.MoveNext();
                if (!r.Alive) return;

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
                        if (startNestedImmediately) continue;
                        return;
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
}
