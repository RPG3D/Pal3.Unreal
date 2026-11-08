// UE-adapted: no MonoBehaviour. Lazy<T> with private ctor support.
using System;

namespace Engine.Utilities
{
    /// <summary>
    /// Session-scoped singleton.
    ///
    /// UnrealSharp keeps the managed runtime alive for the whole editor process, so
    /// statics survive PIE stop/start. A cached singleton would then keep fields that
    /// reference actors of the PREVIOUS world (destroyed after PIE stop), and the
    /// first gameplay tick of the next session calls into freed memory. Every
    /// singleton therefore registers a reset with <see cref="SessionStateRegistry"/>;
    /// APal3Driver.BeginPlay drops all of them before booting a new session.
    /// </summary>
    public class Singleton<T> where T : class
    {
        static Lazy<T> _instance = new(CreateInstance);

        public static T Instance => _instance.Value;

        static Singleton()
        {
            SessionStateRegistry.RegisterReset(() => _instance = new Lazy<T>(CreateInstance));
        }

        static T CreateInstance()
        {
            var c = typeof(T).GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, Type.EmptyTypes);
            if (c == null) throw new InvalidOperationException($"No parameterless ctor on {typeof(T).Name}");
            return (T)c.Invoke(null);
        }
    }

    /// <summary>
    /// Registry of static/singleton state that must be dropped between PIE sessions.
    /// </summary>
    public static class SessionStateRegistry
    {
        private static readonly System.Collections.Generic.List<Action> _resets = new();

        public static void RegisterReset(Action reset)
        {
            if (reset == null) return;
            lock (_resets)
            {
                _resets.Add(reset);
            }
        }

        /// <summary>Instantiates fresh singletons; the next Instance access rebuilds them.</summary>
        public static void ResetAll()
        {
            Action[] copy;
            lock (_resets)
            {
                copy = _resets.ToArray();
            }
            foreach (Action reset in copy)
            {
                try
                {
                    reset();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"[SessionStateRegistry] reset failed: {e.Message}");
                }
            }
        }
    }
}
