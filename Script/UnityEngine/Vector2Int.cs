using System;

namespace UnityEngine
{
    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x, y;
        public int X { readonly get => x; set => x = value; } public int Y { readonly get => y; set => y = value; }
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public static Vector2Int zero => new(0, 0); public static Vector2Int one => new(1, 1);
        public static Vector2Int up => new(0, 1); public static Vector2Int down => new(0, -1);
        public static Vector2Int left => new(-1, 0); public static Vector2Int right => new(1, 0);
        public static implicit operator Vector2(Vector2Int v) => new(v.x, v.y);
        public static float Distance(Vector2Int a, Vector2Int b) { int dx = a.x - b.x, dy = a.y - b.y; return MathF.Sqrt(dx * dx + dy * dy); }
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
        public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new(a.x - b.x, a.y - b.y);
        public static Vector2Int operator -(Vector2Int a) => new(-a.x, -a.y);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2Int a, Vector2Int b) => !(a == b);
        public override readonly bool Equals(object? o) => o is Vector2Int v && Equals(v);
        public readonly bool Equals(Vector2Int o) => x == o.x && y == o.y;
        public override readonly int GetHashCode() => HashCode.Combine(x, y);
        public override readonly string ToString() => $"({x}, {y})";
    }
}
