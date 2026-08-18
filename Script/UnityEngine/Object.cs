using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;

namespace UnityEngine
{
    public class Object
    {
        public string name = "";

        public static void Destroy(object obj)
        {
            switch (obj)
            {
                case AActor a: a.DestroyActor(); break;
                case UActorComponent c: c.DestroyComponent(c); break;
                case UObject u: break; /* no-op on plain UObject */
            }
        }
        public static void DestroyImmediate(object obj) => Destroy(obj);
        public static void DontDestroyOnLoad(Object obj) { }

        /// <summary>Instance-form destroy (UnityEngine.Object.Destroy instance call sites).</summary>
        public void Destroy() { }

        /// <summary>
        /// Unity-compatible Instantiate. For now returns the original (no deep copy);
        /// real UE spawning is wired through GameEntityFactory.
        /// </summary>
        public static T Instantiate<T>(T original) where T : Object => original;
        public static Object Instantiate(Object original) => original;
        public static T Instantiate<T>(T original, UnityEngine.Transform parent) where T : Object => original;
        public static T Instantiate<T>(T original, UnityEngine.Transform parent, bool worldPositionStays) where T : Object => original;

        public static bool operator ==(Object? a, Object? b) => ReferenceEquals(a, b);
        public static bool operator !=(Object? a, Object? b) => !ReferenceEquals(a, b);
        public override bool Equals(object? o) => ReferenceEquals(this, o);
        public override int GetHashCode() => base.GetHashCode();
    }
}
