using System;
using System.Runtime.CompilerServices;
using Pal3.Core.Primitives;
using UnityEngine;

namespace Engine.Extensions
{
    public static class UnrealPrimitivesConvertor
    {
        public const float GameBoxUnitToEngineUnit = 20f;
        public const float GameBoxMv3UnitToEngineUnit = 1270f;
        public const uint GameBoxTicksPerSecond = 4800;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToEnginePosition(this GameBoxVector3 p, float s = GameBoxUnitToEngineUnit)
            => ToEngineVector3(p, s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3[] ToEnginePositions(this GameBoxVector3[] ps, float s = GameBoxUnitToEngineUnit)
        {
            if (ps == null) return null!;
            var r = new Vector3[ps.Length];
            for (int i = 0; i < ps.Length; i++) r[i] = ToEngineVector3(ps[i], s);
            return r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToEngineDistance(this float d) => d / GameBoxUnitToEngineUnit;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToEngineXPosition(this float x) => -x / GameBoxUnitToEngineUnit;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToEngineYPosition(this float y) => y / GameBoxUnitToEngineUnit;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToEngineZPosition(this float z) => z / GameBoxUnitToEngineUnit;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion ToEngineQuaternion(this GameBoxVector3 euler)
            => Quaternion.Euler(euler.X, -euler.Y, euler.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GameBoxVector3 ToGameBoxPosition(this Vector3 p, float s = GameBoxUnitToEngineUnit)
            => new(-p.x * s, p.y * s, p.z * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 CvdPositionToEnginePosition(this GameBoxVector3 p, float s = GameBoxUnitToEngineUnit)
            => new(-p.X / s, p.Z / s, -p.Y / s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 CvdScaleToEngineScale(this GameBoxVector3 s)
            => new(s.X, s.Z, s.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToEngineNormal(this GameBoxVector3 n)
            => new(-n.X, n.Y, n.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3[] ToEngineNormals(this GameBoxVector3[] ns)
        {
            if (ns == null) return null!;
            var r = new Vector3[ns.Length];
            for (int i = 0; i < ns.Length; i++) r[i] = ToEngineNormal(ns[i]);
            return r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion CvdQuaternionToEngineQuaternion(this GameBoxQuaternion q)
            => new(-q.X, q.Z, -q.Y, q.W);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Mv3QuaternionToEngineQuaternion(this GameBoxQuaternion q)
            => new(-q.X, q.Y, q.Z, q.W);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion MovQuaternionToEngineQuaternion(this GameBoxQuaternion q)
            => new(-q.X, q.Y, q.Z, q.W);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion MshQuaternionToEngineQuaternion(this GameBoxQuaternion q)
            => new(-q.X, q.Y, q.Z, q.W);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion LgtQuaternionToEngineQuaternion(this GameBoxQuaternion q)
        {
            var r = new Quaternion(q.X, q.Y, -q.Z, q.W);
            var e = r.eulerAngles;
            return Quaternion.Euler(new Vector3(e.x + 90f, -e.y, e.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion ToEngineQuaternion(float pitch, float yaw, float roll)
            => Quaternion.Euler(pitch - 180, yaw, roll - 180);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int[] ToEngineTriangles(this int[] tris)
        {
            if (tris == null) return null!;
            var r = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++) r[i] = tris[tris.Length - 1 - i];
            return r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 ToEngineMatrix4x4(this GameBoxMatrix4x4 m)
            => new(new Vector4(m.Xx, m.Xy, m.Xz, m.Xw), new Vector4(m.Yx, m.Yy, m.Yz, m.Yw),
                   new Vector4(m.Zx, m.Zy, m.Zz, m.Zw), new Vector4(m.Tx, m.Ty, m.Tz, m.Tw));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToEngineVector2(this GameBoxVector2 v) => new(v.X, v.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int ToEngineVector2Int(this GameBoxVector2Int v) => new(v.X, v.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GameBoxVector2Int ToGameBoxVector2Int(this Vector2Int v) => new(v.x, v.y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint SecondsToGameBoxTick(this float s) => (uint)(s * GameBoxTicksPerSecond);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GameBoxTickToSeconds(this uint t) => (float)t / GameBoxTicksPerSecond;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UnityEngine.Color32 ToEngineColor32(this Pal3.Core.Primitives.Color32 c) => new UnityEngine.Color32(c.R, c.G, c.B, c.A);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UnityEngine.Color ToEngineColor(this Pal3.Core.Primitives.Color c) => new UnityEngine.Color(c.R, c.G, c.B, c.A);

        // --- Old-name compat aliases (Pal3.Game files use ToUnityPosition etc.) ---
        public static Vector3 ToUnityPosition(this GameBoxVector3 p, float s = GameBoxUnitToEngineUnit) => ToEnginePosition(p, s);
        public static float ToUnityDistance(this float d) => ToEngineDistance(d);
        public static float ToUnityXPosition(this float x) => ToEngineXPosition(x);
        public static float ToUnityYPosition(this float y) => ToEngineYPosition(y);
        public static float ToUnityZPosition(this float z) => ToEngineZPosition(z);
        public static Quaternion ToUnityQuaternion(this GameBoxVector3 e) => ToEngineQuaternion(e);
        public static Vector3 ToUnityNormal(this GameBoxVector3 n) => ToEngineNormal(n);
        public static Vector3[] ToUnityNormals(this GameBoxVector3[] ns) => ToEngineNormals(ns);
        public static Quaternion ToUnityQuaternion(this GameBoxQuaternion q) => CvdQuaternionToEngineQuaternion(q);
        public static int[] ToUnityTriangles(this int[] t) => ToEngineTriangles(t);
        public static UnityEngine.Color32 ToUnityColor32(this Pal3.Core.Primitives.Color32 c) => ToEngineColor32(c);
        public static UnityEngine.Color ToUnityColor(this Pal3.Core.Primitives.Color c) => ToEngineColor(c);

        // --- In-place buffer variants (Pal3.Game mesh rendering writes into pooled buffers) ---
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3[] ToUnityPositions(this GameBoxVector3[] ps, Vector3[] buffer, float s = GameBoxUnitToEngineUnit)
        {
            for (int i = 0; i < ps.Length; i++) buffer[i] = ToEngineVector3(ps[i], s);
            return buffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int[] ToUnityTriangles(this int[] tris, int[] buffer)
        {
            for (int i = 0; i < tris.Length; i++) buffer[i] = tris[tris.Length - 1 - i];
            return buffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3[] ToUnityNormals(this GameBoxVector3[] ns, Vector3[] buffer)
        {
            for (int i = 0; i < ns.Length; i++) buffer[i] = ToEngineNormal(ns[i]);
            return buffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2[] ToUnityVector2s(this GameBoxVector2[] vs, Vector2[] buffer)
        {
            for (int i = 0; i < vs.Length; i++) buffer[i] = new Vector2(vs[i].X, vs[i].Y);
            return buffer;
        }

        // --- Allocating variants (mesh builders call these directly) ---
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3[] ToUnityPositions(this GameBoxVector3[] ps, float s = GameBoxUnitToEngineUnit)
            => ToEnginePositions(ps, s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2[] ToUnityVector2s(this GameBoxVector2[] vs)
        {
            if (vs == null) return null!;
            var r = new Vector2[vs.Length];
            for (int i = 0; i < vs.Length; i++) r[i] = ToEngineVector2(vs[i]);
            return r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int[] ToUnityTrianglesInPlace(this int[] tris)
        {
            if (tris == null) return null!;
            Array.Reverse(tris);
            return tris;
        }

        // --- Remaining alias names the ported Pal3.Game code expects ---
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToUnityVector2(this GameBoxVector2 v) => ToEngineVector2(v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int ToUnityVector2Int(this GameBoxVector2Int v) => new(v.X, v.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 CvdPositionToUnityPosition(this GameBoxVector3 p, float s = GameBoxUnitToEngineUnit)
            => CvdPositionToEnginePosition(p, s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 CvdScaleToUnityScale(this GameBoxVector3 s)
            => CvdScaleToEngineScale(s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion CvdQuaternionToUnityQuaternion(this GameBoxQuaternion q)
            => CvdQuaternionToEngineQuaternion(q);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Mv3QuaternionToUnityQuaternion(this GameBoxQuaternion q)
            => Mv3QuaternionToEngineQuaternion(q);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion MovQuaternionToUnityQuaternion(this GameBoxQuaternion q)
            => MovQuaternionToEngineQuaternion(q);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion MshQuaternionToUnityQuaternion(this GameBoxQuaternion q)
            => MshQuaternionToEngineQuaternion(q);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion LgtQuaternionToUnityQuaternion(this GameBoxQuaternion q)
            => LgtQuaternionToEngineQuaternion(q);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion ToUnityQuaternion(float pitch, float yaw, float roll)
            => ToEngineQuaternion(pitch, yaw, roll);

        /// <summary>
        /// Inverse of <see cref="ToEngineQuaternion(GameBoxVector3)"/> for the Y axis:
        /// engine yaw is stored negated, so converting back to GameBox negates again.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToGameBoxYEulerAngle(this float engineYaw) => -engineYaw;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ToEngineVector3(GameBoxVector3 v, float s)
            => new(-v.X / s, v.Y / s, v.Z / s);
    }

    /// <summary>
    /// Old-name facade used by Pal3.Game code (the port historically named this class
    /// UnityPrimitivesConvertor). All members forward to <see cref="UnrealPrimitivesConvertor"/>.
    /// </summary>
    public static class UnityPrimitivesConvertor
    {
        public const float GameBoxUnitToUnityUnit = UnrealPrimitivesConvertor.GameBoxUnitToEngineUnit;
        public const float GameBoxMv3UnitToUnityUnit = UnrealPrimitivesConvertor.GameBoxMv3UnitToEngineUnit;
        public const uint GameBoxTicksPerSecond = UnrealPrimitivesConvertor.GameBoxTicksPerSecond;

        public static Vector3 ToUnityPosition(GameBoxVector3 p, float s = GameBoxUnitToUnityUnit) => p.ToUnityPosition(s);
        public static Vector3[] ToUnityPositions(GameBoxVector3[] ps, float s = GameBoxUnitToUnityUnit)
            => ps.ToUnityPositions(new Vector3[ps.Length], s);
        public static float ToUnityDistance(float d) => d.ToUnityDistance();
        public static float ToUnityXPosition(float x) => x.ToUnityXPosition();
        public static float ToUnityYPosition(float y) => y.ToUnityYPosition();
        public static float ToUnityZPosition(float z) => z.ToUnityZPosition();
        public static Quaternion ToUnityQuaternion(GameBoxVector3 e) => e.ToUnityQuaternion();
        public static Quaternion ToUnityQuaternion(GameBoxQuaternion q) => q.ToUnityQuaternion();
        public static Quaternion ToUnityQuaternion(float pitch, float yaw, float roll) => UnrealPrimitivesConvertor.ToUnityQuaternion(pitch, yaw, roll);
        public static Vector3 ToUnityNormal(GameBoxVector3 n) => n.ToUnityNormal();
        public static Vector3[] ToUnityNormals(GameBoxVector3[] ns) => ns.ToUnityNormals();
        public static int[] ToUnityTriangles(int[] t) => t.ToUnityTriangles();
        public static Vector3 CvdPositionToUnityPosition(GameBoxVector3 p, float s = GameBoxUnitToUnityUnit) => p.CvdPositionToUnityPosition(s);
        public static Vector3 CvdScaleToUnityScale(GameBoxVector3 s) => s.CvdScaleToUnityScale();
        public static Quaternion CvdQuaternionToUnityQuaternion(GameBoxQuaternion q) => q.CvdQuaternionToUnityQuaternion();
        public static Quaternion Mv3QuaternionToUnityQuaternion(GameBoxQuaternion q) => q.Mv3QuaternionToUnityQuaternion();
        public static Quaternion MovQuaternionToUnityQuaternion(GameBoxQuaternion q) => q.MovQuaternionToUnityQuaternion();
        public static Quaternion MshQuaternionToUnityQuaternion(GameBoxQuaternion q) => q.MshQuaternionToUnityQuaternion();
        public static Quaternion LgtQuaternionToUnityQuaternion(GameBoxQuaternion q) => q.LgtQuaternionToUnityQuaternion();
        public static Vector2 ToUnityVector2(GameBoxVector2 v) => v.ToUnityVector2();
        public static Vector2Int ToUnityVector2Int(GameBoxVector2Int v) => v.ToUnityVector2Int();
        public static Matrix4x4 ToUnityMatrix4x4(GameBoxMatrix4x4 m) => m.ToEngineMatrix4x4();
        public static UnityEngine.Color32 ToUnityColor32(Pal3.Core.Primitives.Color32 c) => c.ToUnityColor32();
        public static UnityEngine.Color ToUnityColor(Pal3.Core.Primitives.Color c) => c.ToUnityColor();
        public static float ToGameBoxYEulerAngle(float engineYaw) => engineYaw.ToGameBoxYEulerAngle();
    }
}
