using System;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public static class Mathf
    {
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;
        public const float Epsilon = 1.401298E-45f;
        public const float PI = 3.141592653589793f;
        public const float Infinity = float.PositiveInfinity;
        public const float NegativeInfinity = float.NegativeInfinity;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Abs(float f) => MathF.Abs(f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Abs(int f) => Math.Abs(f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Sign(float f) => f >= 0f ? 1f : -1f;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Min(float a, float b) => MathF.Min(a, b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Max(float a, float b) => MathF.Max(a, b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Min(int a, int b) => Math.Min(a, b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Max(int a, int b) => Math.Max(a, b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Clamp(int v, int min, int max) => Math.Clamp(v, min, max);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float InverseLerp(float a, float b, float v) => a != b ? Math.Clamp((v - a) / (b - a), 0f, 1f) : 0f;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Sin(float f) => MathF.Sin(f); public static float Cos(float f) => MathF.Cos(f); public static float Tan(float f) => MathF.Tan(f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Asin(float f) => MathF.Asin(f); public static float Acos(float f) => MathF.Acos(f); public static float Atan(float f) => MathF.Atan(f); public static float Atan2(float y, float x) => MathF.Atan2(y, x);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Sqrt(float f) => MathF.Sqrt(f); public static float Pow(float f, float p) => MathF.Pow(f, p);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Exp(float f) => MathF.Exp(f); public static float Log(float f) => MathF.Log(f); public static float Log10(float f) => MathF.Log10(f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Ceil(float f) => MathF.Ceiling(f); public static float Floor(float f) => MathF.Floor(f); public static float Round(float f) => MathF.Round(f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int CeilToInt(float f) => (int)MathF.Ceiling(f); public static int FloorToInt(float f) => (int)MathF.Floor(f); public static int RoundToInt(float f) => (int)MathF.Round(f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Repeat(float t, float len) => Math.Clamp(t - MathF.Floor(t / len) * len, 0f, len);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float PingPong(float t, float len) { t = Repeat(t, len * 2f); return len - MathF.Abs(t - len); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float SmoothStep(float from, float to, float t) { t = Math.Clamp((t - from) / (to - from), 0f, 1f); return t * t * (3f - 2f * t); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool Approximately(float a, float b) => MathF.Abs(b - a) < MathF.Max(0.000001f * MathF.Max(MathF.Abs(a), MathF.Abs(b)), float.Epsilon * 8f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float MoveTowards(float current, float target, float maxDelta) => MathF.Abs(target - current) <= maxDelta ? target : current + MathF.Sign(target - current) * maxDelta;
    }
}
