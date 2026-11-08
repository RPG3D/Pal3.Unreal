using System;
using System.Runtime.CompilerServices;
using UEVector = UnrealSharp.CoreUObject.FVector;

namespace UnityEngine
{
    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;
        public float X { readonly get => x; set => x = value; }
        public float Y { readonly get => y; set => y = value; }
        public float Z { readonly get => z; set => z = value; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3(float x, float y) : this(x, y, 0f) { }

        public static implicit operator UEVector(Vector3 v) => new(v.x, v.y, v.z);
        public static implicit operator Vector3(UEVector v) => new((float)v.X, (float)v.Y, (float)v.Z);

        public static Vector3 zero => new(0f, 0f, 0f);
        public static Vector3 one => new(1f, 1f, 1f);
        // Unity semantics (X right / Y up / Z forward) — these MUST match the
        // Quaternion shim's convention (which stores UE FQuat components but speaks
        // Unity euler semantics). The old values had forward/up swapped, which
        // silently rotated every `q * Vector3.forward` computation by 90° and made
        // horizontal orbits dive below the floor.
        public static Vector3 forward => new(0f, 0f, 1f);
        public static Vector3 back => new(0f, 0f, -1f);
        public static Vector3 up => new(0f, 1f, 0f);
        public static Vector3 down => new(0f, -1f, 0f);
        public static Vector3 left => new(-1f, 0f, 0f);
        public static Vector3 right => new(1f, 0f, 0f);

        public readonly float magnitude => MathF.Sqrt(x * x + y * y + z * z);
        public readonly float sqrMagnitude => x * x + y * y + z * z;
        public readonly Vector3 normalized
        {
            get { float mag = magnitude; return mag > 9.999999747378752E-06f ? this / mag : zero; }
        }
        public float this[int index]
        {
            readonly get => index switch { 0 => x, 1 => y, 2 => z, _ => throw new IndexOutOfRangeException() };
            set { switch (index) { case 0: x = value; break; case 1: y = value; break; case 2: z = value; break; default: throw new IndexOutOfRangeException(); } }
        }

        public void Normalize() { float mag = magnitude; if (mag > 9.999999747378752E-06f) { x /= mag; y /= mag; z /= mag; } else { x = y = z = 0f; } }
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDelta) { Vector3 d = target - current; float dist = d.magnitude; return dist <= maxDelta ? target : current + d / dist * maxDelta; }
        public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        public static float Angle(Vector3 from, Vector3 to) { float dot = Dot(from.normalized, to.normalized); return MathF.Acos(Math.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg; }
        public static Vector3 ClampMagnitude(Vector3 vector, float max) => vector.sqrMagnitude > max * max ? vector.normalized * max : vector;

        /// <summary>
        /// Rotates current towards target by at most maxRadiansDelta (radians), while
        /// also moving the magnitude towards the target magnitude by maxMagnitudeDelta.
        /// </summary>
        public static Vector3 RotateTowards(Vector3 current, Vector3 target, float maxRadiansDelta, float maxMagnitudeDelta)
        {
            float currentMag = current.magnitude;
            float targetMag = target.magnitude;
            Vector3 currentDir = currentMag > 1e-6f ? current / currentMag : zero;
            Vector3 targetDir = targetMag > 1e-6f ? target / targetMag : zero;

            float newMag = Mathf.MoveTowards(currentMag, targetMag, maxMagnitudeDelta);

            if (currentDir.sqrMagnitude < 1e-8f || targetDir.sqrMagnitude < 1e-8f)
                return targetDir * newMag;

            float angle = Angle(currentDir, targetDir) * Mathf.Deg2Rad;
            if (angle <= maxRadiansDelta)
                return targetDir * newMag;

            float t = Mathf.Clamp01(maxRadiansDelta / angle);
            return Lerp(currentDir, targetDir, t).normalized * newMag;
        }

        /// <summary>
        /// Smoothly damps current towards target with critical damping (Unity
        /// SmoothDamp behavior). Uses default 0.02s fixed-timestep deltaTime.
        /// </summary>
        public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity,
            float smoothTime, float maxSpeed = float.PositiveInfinity, float deltaTime = 0.02f)
        {
            float omega = 2f / MathF.Max(smoothTime, 0.0001f);
            float x = omega * deltaTime;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            Vector3 change = current - target;
            Vector3 originalTo = target;

            float maxChange = maxSpeed * smoothTime;
            change = ClampMagnitude(change, maxChange);
            target = current - change;

            Vector3 temp = (currentVelocity + omega * change) * deltaTime;
            currentVelocity = (currentVelocity - omega * temp) * exp;
            Vector3 output = target + (change + temp) * exp;

            if (Dot(originalTo - current, output - originalTo) > 0f)
            {
                output = originalTo;
                currentVelocity = (output - originalTo) / deltaTime;
            }
            return output;
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override readonly bool Equals(object? obj) => obj is Vector3 other && Equals(other);
        public readonly bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;
        public override readonly int GetHashCode() => HashCode.Combine(x, y, z);
        public override readonly string ToString() => $"({x:F2}, {y:F2}, {z:F2})";
    }
}
