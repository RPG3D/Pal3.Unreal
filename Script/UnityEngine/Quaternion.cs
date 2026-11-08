using System;
using System.Runtime.CompilerServices;
using UEQuat = UnrealSharp.CoreUObject.FQuat;
using UERotator = UnrealSharp.CoreUObject.FRotator;

namespace UnityEngine
{
    // Quaternion convention (the "general solution" for Unity↔UE handedness):
    //
    // The shim stores UE FQuat components (same convention as UnrealSharp's FQuat).
    // eulerAngles / Euler() round-trip through FRotator, so a quat built from
    // Euler(x,y,z) renders as FRotator(-x, y, -z) — that extra flip is what makes
    // the shim behave like Unity's Quaternion.Euler, whose pitch is inverted
    // relative to UE (Unity: +pitch looks down; UE: +pitch looks up).
    //
    // Algebraically (from the UE FRotator→FQuat formula), negating the rotator's
    // pitch and roll before construction negates the quat's X and Y components:
    //     applied(q) = (-q.x, -q.y, q.z, q.w)      — "flip"
    // So the rotation a transform write actually applies is flip(q), and every
    // Unity-space operation here is defined to stay consistent with that:
    //     q * v            = MapInv(rot(Map(v), flip(q)))
    //     a * b (stored)   = flip(flip(a) * flip(b))
    //     AngleAxis(θ, ax) = flip(axis-angle(Map(ax), θ))
    //     LookRotation     = Unity-style extraction over mapped basis rows
    // where Map is the basis permutation UE.(x,y,z) = U.(z,x,y).
    // Inverse/Slerp/Lerp/Dot/Angle/RotateTowards/FromToRotation are unchanged:
    // flip commutes with all of them (verified numerically).
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
            // Unity semantics: +Z (forward) → forward, keeping up as close to
            // upwards as possible. Built over UE-mapped basis rows so the stored
            // components match the flip convention (validated numerically).
            Vector3 fwd = forward.normalized;
            Vector3 right = Vector3.Cross(upwards, fwd).normalized;
            Vector3 up = Vector3.Cross(fwd, right);

            // Map to UE axes: UE.(x,y,z) = U.(z,x,y). Each ROW is one mapped
            // basis vector: row0 = fwd', row1 = right', row2 = up'.
            float m00 = fwd.z, m01 = fwd.x, m02 = fwd.y;
            float m10 = right.z, m11 = right.x, m12 = right.y;
            float m20 = up.z, m21 = up.x, m22 = up.y;

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
            // Unity axis-angle, expressed in the flip convention: the applied UE
            // rotation is axis-angle(Map(axis)); stored = flip of that.
            float halfAngle = angle * Mathf.Deg2Rad * 0.5f;
            float s = MathF.Sin(halfAngle);
            float c = MathF.Cos(halfAngle);
            return new Quaternion(-axis.z * s, -axis.x * s, axis.y * s, c);
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
            // Unity composition, expressed in the flip convention:
            // stored(a*b) = flip(flip(a) * flip(b)).
            float ax = -a.x, ay = -a.y, az = a.z, aw = a.w;
            float bx = -b.x, by = -b.y, bz = b.z, bw = b.w;
            return new Quaternion(
                -(aw * bx + ax * bw + ay * bz - az * by),
                -(aw * by - ax * bz + ay * bw + az * bx),
                aw * bz + ax * by - ay * bx + az * bw,
                aw * bw - ax * bx - ay * by - az * bz);
        }

        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            // Unity rotation of a Unity-space vector: map to UE axes, rotate by
            // the applied quat flip(q), map back.
            float ax = -q.x, ay = -q.y, az = q.z, aw = q.w;
            float vx = v.z, vy = v.x, vz = v.y; // Map: UE.(x,y,z) = U.(z,x,y)

            float tx = 2f * (ay * vz - az * vy);
            float ty = 2f * (az * vx - ax * vz);
            float tz = 2f * (ax * vy - ay * vx);

            float rx = vx + aw * tx + (ay * tz - az * ty);
            float ry = vy + aw * ty + (az * tx - ax * tz);
            float rz = vz + aw * tz + (ax * ty - ay * tx);

            return new Vector3(ry, rz, rx); // MapInv: U.(x,y,z) = UE.(y,z,x)
        }

        public static bool operator ==(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);

        public override readonly bool Equals(object? obj) => obj is Quaternion other && Equals(other);
        public readonly bool Equals(Quaternion other) => x == other.x && y == other.y && z == other.z && w == other.w;
        public override readonly int GetHashCode() => HashCode.Combine(x, y, z, w);
        public override readonly string ToString() => $"({x:F2}, {y:F2}, {z:F2}, {w:F2})";
    }
}
