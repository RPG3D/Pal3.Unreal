// UE-adapted: no MonoBehaviour. Lazy<T> with private ctor support.
using System;

namespace Engine.Utilities
{
    public class Singleton<T> where T : class
    {
        static readonly Lazy<T> _instance = new(() => {
            var c = typeof(T).GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, Type.EmptyTypes);
            if (c == null) throw new InvalidOperationException($"No parameterless ctor on {typeof(T).Name}");
            return (T)c.Invoke(null);
        });
        public static T Instance => _instance.Value;
    }
}
