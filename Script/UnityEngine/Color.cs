using System;

namespace UnityEngine
{
    public struct Color : IEquatable<Color>
    {
        public float r, g, b, a;
        public float R { readonly get => r; set => r = value; } public float G { readonly get => g; set => g = value; }
        public float B { readonly get => b; set => b = value; } public float A { readonly get => a; set => a = value; }
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new(1, 1, 1); public static Color black => new(0, 0, 0);
        public static Color red => new(1, 0, 0); public static Color green => new(0, 1, 0);
        public static Color blue => new(0, 0, 1); public static Color clear => new(0, 0, 0, 0);
        public static Color gray => new(0.5f, 0.5f, 0.5f); public static Color grey => gray;
        public static Color yellow => new(1, 1, 0); public static Color cyan => new(0, 1, 1);
        public static Color magenta => new(1, 0, 1); public static Color orange => new(1, 0.5f, 0);
        public static bool operator ==(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        public static bool operator !=(Color a, Color b) => !(a == b);
        public override readonly bool Equals(object? o) => o is Color c && Equals(c);
        public readonly bool Equals(Color o) => this == o;
        public override readonly int GetHashCode() => HashCode.Combine(r, g, b, a);
    }
    public struct Color32 : IEquatable<Color32>
    {
        public byte r, g, b, a;
        public byte R { readonly get => r; set => r = value; } public byte G { readonly get => g; set => g = value; }
        public byte B { readonly get => b; set => b = value; } public byte A { readonly get => a; set => a = value; }
        public Color32(byte r, byte g, byte b, byte a = 255) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) => new(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        public static implicit operator Color32(Color c) => new(
            (byte)(c.r * 255f + 0.5f), (byte)(c.g * 255f + 0.5f),
            (byte)(c.b * 255f + 0.5f), (byte)(c.a * 255f + 0.5f));
        public static bool operator ==(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        public static bool operator !=(Color32 a, Color32 b) => !(a == b);
        public override readonly bool Equals(object? o) => o is Color32 c && Equals(c);
        public readonly bool Equals(Color32 o) => this == o;
        public override readonly int GetHashCode() => HashCode.Combine(r, g, b, a);
    }
}
