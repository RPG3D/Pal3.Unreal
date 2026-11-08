// Unity↔UE coordinate seam. The ported Pal3 game code speaks Unity coordinates
// everywhere (meters, X-right / Y-up / Z-forward, left-handed) — keeping it
// byte-identical to upstream PAL3.Unity. UE is centimeters, X-forward /
// Y-right / Z-up. This is the ONLY place the two conventions meet: UETransform
// (and later the render/audio/camera bridges) route every Unity-space value
// through these helpers, so game logic never needs to know it runs on UE.
//
// Basis mapping (orientation-preserving cyclic permutation, det(M)=+1):
//     UE.X = U.Z   (Unity forward  → UE forward)
//     UE.Y = U.X   (Unity right    → UE right)
//     UE.Z = U.Y   (Unity up       → UE up)
// Translations additionally scale by 100 (m → cm); directions and rotation
// axes do not.
//
// Quaternion convention — see UnityEngine/Quaternion.cs header for the full
// derivation. The shim stores UE FQuat components but gives them Unity euler
// semantics (Euler/eulerAngles round-trip FRotator with the pitch sign
// inverted relative to Unity). The rotation a shim quat represents when
// written through UETransform is:
//     applied(q) = (-q.x, -q.y, q.z, q.w)
// ("flip" — the algebraic form of FRotator(-pitch, yaw, -roll); negating the
// rotator's pitch and roll negates the quat's X and Y components). These two
// helpers are that flip, kept as plain component arithmetic so a write→read
// round trip is exact. The earlier pure component-permutation form was WRONG
// (it disagreed with the euler path and read back a different rotation).

using UnrealSharp.CoreUObject;

namespace Engine.Core.Implementation
{
    public static class UnityUeConvert
    {
        /// <summary>Unity meters → UE centimeters (with axis mapping).</summary>
        public static FVector ToUePosition(UnityEngine.Vector3 v)
            => new(v.z * 100f, v.x * 100f, v.y * 100f);

        /// <summary>UE centimeters → Unity meters (inverse axis mapping).</summary>
        public static UnityEngine.Vector3 ToUnityPosition(FVector v)
            => new((float)v.Y / 100f, (float)v.Z / 100f, (float)v.X / 100f);

        /// <summary>Direction vector: axis mapping only, no unit scale.</summary>
        public static FVector ToUeDirection(UnityEngine.Vector3 v)
            => new(v.z, v.x, v.y);

        public static UnityEngine.Vector3 ToUnityDirection(FVector v)
            => new((float)v.Y, (float)v.Z, (float)v.X);

        /// <summary>
        /// Shim quaternion → the UE rotation UETransform applies for it (flip form).
        /// Equivalent to the euler path FRotator(-e.x, e.y, -e.z) with e = canonical
        /// euler of q, but exact (no euler round trip) and roll-preserving.
        /// </summary>
        public static FQuat ToUeQuaternion(UnityEngine.Quaternion q)
            => new(new FVector(-q.x, -q.y, q.z), q.w);

        /// <summary>
        /// UE quat as stored by the shim — exact inverse of <see cref="ToUeQuaternion"/>
        /// (flip is an involution), so write→read returns the original components.
        /// </summary>
        public static UnityEngine.Quaternion ToUnityQuaternion(FQuat q)
            => new(-(float)q.X, -(float)q.Y, (float)q.Z, (float)q.W);

        /// <summary>Unity Euler degrees (x=pitch, y=yaw, z=roll) → UE FRotator.</summary>
        public static FRotator ToUeRotator(UnityEngine.Vector3 unityEuler)
            => new(unityEuler.x, unityEuler.y, unityEuler.z);

        /// <summary>UE FRotator → Unity Euler degrees (x=pitch, y=yaw, z=roll).</summary>
        public static UnityEngine.Vector3 ToUnityEuler(FRotator r)
            => new((float)r.Pitch, (float)r.Yaw, (float)r.Roll);
    }
}
