using System;
using System.Runtime.CompilerServices;
using UEQuat = UnrealSharp.CoreUObject.FQuat;
using UERotator = UnrealSharp.CoreUObject.FRotator;

namespace UnityEngine
{
    public struct Quaternion : IEquatable<Quaternion>
    {
        public float x, y, z, w;
        public float X { readonly get => x; set => x = value; }
        public float Y { readonly get => y; set => y = value; }
        public float Z { readonly get => z; set => z = value; }
        public float W { readonly get => w; set => w = value; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public static Quaternion identity => new Quaternion(0f, 0f, 0f, 1f);

        public static implicit operator UEQuat(Quaternion q) => new UEQuat { X = q.x, Y = q.y, Z = q.z, W = q.w };
        public static implicit operator Quaternion(UEQuat q) => new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

        public Vector3 eulerAngles
        {
            readonly get
            {
                UERotator rot = ((UEQuat)this).ToRotator;
                return new Vector3((float)rot.Pitch, (float)rot.Yaw, (float)rot.Roll);
            }
            set
            {
                UERotator rot = new UERotator(value.x, value.y, value.z);
                UEQuat q = new UEQuat(rot);
                x = (float)q.X; y = (float)q.Y; z = (float)q.Z; w = (float)q.W;
            }
        }

        public readonly Quaternion normalized
        {
            get
            {
                float mag = MathF.Sqrt(x * x + y * y + z * z + w * w);
                if (mag < 1e-8f) return identity;
                float inv = 1f / mag;
                return new Quaternion(x * inv, y * inv, z * inv, w * inv);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Euler(float x, float y, float z)
        {
            UERotator rot = new UERotator(x, y, z);
            return (Quaternion)new UEQuat(rot);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Euler(Vector3 euler) => Euler(euler.x, euler.y, euler.z);

        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards)
        {
            Vector3 fwd = forward.normalized;
            Vector3 right = Vector3.Cross(upwards, fwd).normalized;
            Vector3 up = Vector3.Cross(fwd, right);

            float m00 = right.x, m01 = right.y, m02 = right.z;
            float m10 = up.x,    m11 = up.y,    m12 = up.z;
            float m20 = fwd.x,   m21 = fwd.y,   m22 = fwd.z;

            float num8 = m00 + m11 + m22;
            if (num8 > 0f)
            {
                float s = MathF.Sqrt(num8 + 1f);
                float inv = 0.5f / s;
                return new Quaternion(
                    (m12 - m21) * inv,
                    (m20 - m02) * inv,
                    (m01 - m10) * inv,
                    s * 0.5f);
            }
            if (m00 >= m11 && m00 >= m22)
            {
                float s = MathF.Sqrt(1f + m00 - m11 - m22);
                float inv = 0.5f / s;
                return new Quaternion(
                    s * 0.5f,
                    (m01 + m10) * inv,
                    (m02 + m20) * inv,
                    (m12 - m21) * inv);
            }
            if (m11 >= m22)
            {
                float s = MathF.Sqrt(1f + m11 - m00 - m22);
                float inv = 0.5f / s;
                return new Quaternion(
                    (m01 + m10) * inv,
                    s * 0.5f,
                    (m12 + m21) * inv,
                    (m20 - m02) * inv);
            }
            {
                float s = MathF.Sqrt(1f + m22 - m00 - m11);
                float inv = 0.5f / s;
                return new Quaternion(
                    (m02 + m20) * inv,
                    (m12 + m21) * inv,
                    s * 0.5f,
                    (m01 - m10) * inv);
            }
        }

        public static Quaternion LookRotation(Vector3 forward) => LookRotation(forward, Vector3.up);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            return (Quaternion)UEQuat.Slerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Lerp(Quaternion a, Quaternion b, float t)
        {
            return (Quaternion)UEQuat.Lerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Inverse(Quaternion q)
        {
            return (Quaternion)UEQuat.Inverse(q);
        }

        public static float Angle(Quaternion a, Quaternion b)
        {
            float dot = Math.Min(MathF.Abs(Dot(a, b)), 1f);
            return MathF.Acos(dot) * 2f * Mathf.Rad2Deg;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Quaternion a, Quaternion b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        }

        public static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegreesDelta)
        {
            float angle = Angle(from, to);
            if (angle < 1e-6f) return to;
            float t = Math.Min(1f, maxDegreesDelta / angle);
            return Slerp(from, to, t);
        }

        public static Quaternion AngleAxis(float angle, Vector3 axis)
        {
            float halfAngle = angle * Mathf.Deg2Rad * 0.5f;
            float s = MathF.Sin(halfAngle);
            float c = MathF.Cos(halfAngle);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, c);
        }

        public static Quaternion FromToRotation(Vector3 fromDirection, Vector3 toDirection)
        {
            Vector3 from = fromDirection.normalized;
            Vector3 to = toDirection.normalized;

            float dot = Vector3.Dot(from, to);

            if (dot > 0.999999f)
                return identity;

            if (dot < -0.999999f)
            {
                Vector3 axis = Vector3.Cross(from, Vector3.up);
                if (axis.sqrMagnitude < 1e-8f)
                    axis = Vector3.Cross(from, Vector3.right);
                return AngleAxis(180f, axis.normalized);
            }

            Vector3 cross = Vector3.Cross(from, to);
            float angle = MathF.Acos(dot) * Mathf.Rad2Deg;
            return AngleAxis(angle, cross.normalized);
        }

        public static Quaternion operator *(Quaternion a, Quaternion b)
        {
            return new Quaternion(
                a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
                a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        }

        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            Vector3 qv = new Vector3(q.x, q.y, q.z);
            Vector3 t = 2f * Vector3.Cross(qv, v);
            return v + q.w * t + Vector3.Cross(qv, t);
        }

        public static bool operator ==(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);

        public override readonly bool Equals(object? obj) => obj is Quaternion other && Equals(other);
        public readonly bool Equals(Quaternion other) => x == other.x && y == other.y && z == other.z && w == other.w;
        public override readonly int GetHashCode() => HashCode.Combine(x, y, z, w);
        public override readonly string ToString() => $"({x:F2}, {y:F2}, {z:F2}, {w:F2})";
    }
}
