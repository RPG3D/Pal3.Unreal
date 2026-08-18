using System;
using System.Runtime.CompilerServices;
using UEVector2D = UnrealSharp.CoreUObject.FVector2D;

namespace UnityEngine
{
    public struct Vector2 : IEquatable<Vector2>
    {
        public float x, y;
        public float X { readonly get => x; set => x = value; }
        public float Y { readonly get => y; set => y = value; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static implicit operator UEVector2D(Vector2 v) => new(v.x, v.y);
        public static implicit operator Vector2(UEVector2D v) => new((float)v.X, (float)v.Y);
        public static Vector2 zero => new(0f, 0f); public static Vector2 one => new(1f, 1f);
        public static Vector2 up => new(0f, 1f); public static Vector2 down => new(0f, -1f);
        public static Vector2 left => new(-1f, 0f); public static Vector2 right => new(1f, 0f);
        public readonly float magnitude => MathF.Sqrt(x * x + y * y);
        public readonly float sqrMagnitude => x * x + y * y;
        public readonly Vector2 normalized { get { float m = magnitude; return m > 1E-06f ? this / m : zero; } }
        public float this[int i] { readonly get => i == 0 ? x : (i == 1 ? y : throw new IndexOutOfRangeException()); set { if (i == 0) x = value; else if (i == 1) y = value; else throw new IndexOutOfRangeException(); } }
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDelta) { Vector2 d = target - current; float dist = d.magnitude; return dist <= maxDelta ? target : current + d / dist * maxDelta; }
        public static float Angle(Vector2 from, Vector2 to) { float dot = Math.Clamp(Dot(from.normalized, to.normalized), -1f, 1f); return MathF.Acos(dot) * Mathf.Rad2Deg; }
        public static float SignedAngle(Vector2 from, Vector2 to) { float angle = Angle(from, to); float cross = from.x * to.y - from.y * to.x; return cross >= 0f ? angle : -angle; }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new(a.x / d, a.y / d);
        public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public override readonly bool Equals(object? o) => o is Vector2 v && Equals(v);
        public readonly bool Equals(Vector2 o) => x == o.x && y == o.y;
        public override readonly int GetHashCode() => HashCode.Combine(x, y);
        public override readonly string ToString() => $"({x:F2}, {y:F2})";
    }
}
