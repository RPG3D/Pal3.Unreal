using System;
using NumericsMatrix = System.Numerics.Matrix4x4;

namespace UnityEngine
{
    public struct Vector4 : IEquatable<Vector4> { public float x, y, z, w; public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; } public static bool operator ==(Vector4 a, Vector4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w; public static bool operator !=(Vector4 a, Vector4 b) => !(a == b); public override readonly bool Equals(object? o) => o is Vector4 v && Equals(v); public readonly bool Equals(Vector4 o) => this == o; public override readonly int GetHashCode() => HashCode.Combine(x, y, z, w); }
    public struct Matrix4x4 : IEquatable<Matrix4x4>
    {
        public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;
        public Matrix4x4(Vector4 c0, Vector4 c1, Vector4 c2, Vector4 c3)
        {
            m00 = c0.x; m01 = c1.x; m02 = c2.x; m03 = c3.x;
            m10 = c0.y; m11 = c1.y; m12 = c2.y; m13 = c3.y;
            m20 = c0.z; m21 = c1.z; m22 = c2.z; m23 = c3.z;
            m30 = c0.w; m31 = c1.w; m32 = c2.w; m33 = c3.w;
        }
        public static Matrix4x4 identity => new(new Vector4(1, 0, 0, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, 0), new Vector4(0, 0, 0, 1));

        public static Matrix4x4 Translate(Vector3 vector) => new(
            new Vector4(1, 0, 0, 0),
            new Vector4(0, 1, 0, 0),
            new Vector4(0, 0, 1, 0),
            new Vector4(vector.x, vector.y, vector.z, 1));

        public static Matrix4x4 Scale(Vector3 vector) => new(
            new Vector4(vector.x, 0, 0, 0),
            new Vector4(0, vector.y, 0, 0),
            new Vector4(0, 0, vector.z, 0),
            new Vector4(0, 0, 0, 1));

        public static Matrix4x4 Rotate(Quaternion rotation)
        {
            float x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w;
            float xx = x * x, yy = y * y, zz = z * z;
            float xy = x * y, xz = x * z, yz = y * z;
            float wx = w * x, wy = w * y, wz = w * z;
            return new Matrix4x4(
                new Vector4(1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy), 0),
                new Vector4(2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx), 0),
                new Vector4(2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy), 0),
                new Vector4(0, 0, 0, 1));
        }

        public static Matrix4x4 Inverse(Matrix4x4 m)
        {
            var matrix = new NumericsMatrix(
                m.m00, m.m01, m.m02, m.m03,
                m.m10, m.m11, m.m12, m.m13,
                m.m20, m.m21, m.m22, m.m23,
                m.m30, m.m31, m.m32, m.m33);
            if (MathF.Abs(matrix.GetDeterminant()) < 1e-12f || !NumericsMatrix.Invert(matrix, out var inverse))
                return identity;

            return new Matrix4x4(
                new Vector4(inverse.M11, inverse.M21, inverse.M31, inverse.M41),
                new Vector4(inverse.M12, inverse.M22, inverse.M32, inverse.M42),
                new Vector4(inverse.M13, inverse.M23, inverse.M33, inverse.M43),
                new Vector4(inverse.M14, inverse.M24, inverse.M34, inverse.M44));
        }

        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
        {
            return new Matrix4x4(
                new Vector4(
                    a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20 + a.m03 * b.m30,
                    a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20 + a.m13 * b.m30,
                    a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20 + a.m23 * b.m30,
                    a.m30 * b.m00 + a.m31 * b.m10 + a.m32 * b.m20 + a.m33 * b.m30),
                new Vector4(
                    a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21 + a.m03 * b.m31,
                    a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21 + a.m13 * b.m31,
                    a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21 + a.m23 * b.m31,
                    a.m30 * b.m01 + a.m31 * b.m11 + a.m32 * b.m21 + a.m33 * b.m31),
                new Vector4(
                    a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22 + a.m03 * b.m32,
                    a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22 + a.m13 * b.m32,
                    a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22 + a.m23 * b.m32,
                    a.m30 * b.m02 + a.m31 * b.m12 + a.m32 * b.m22 + a.m33 * b.m32),
                new Vector4(
                    a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03 * b.m33,
                    a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13 * b.m33,
                    a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23 * b.m33,
                    a.m30 * b.m03 + a.m31 * b.m13 + a.m32 * b.m23 + a.m33 * b.m33));
        }

        public static Vector4 operator *(Matrix4x4 m, Vector4 v)
        {
            return new Vector4(
                m.m00 * v.x + m.m01 * v.y + m.m02 * v.z + m.m03 * v.w,
                m.m10 * v.x + m.m11 * v.y + m.m12 * v.z + m.m13 * v.w,
                m.m20 * v.x + m.m21 * v.y + m.m22 * v.z + m.m23 * v.w,
                m.m30 * v.x + m.m31 * v.y + m.m32 * v.z + m.m33 * v.w);
        }

        public Vector3 MultiplyPoint3x4(Vector3 point)
        {
            return new Vector3(
                m00 * point.x + m01 * point.y + m02 * point.z + m03,
                m10 * point.x + m11 * point.y + m12 * point.z + m13,
                m20 * point.x + m21 * point.y + m22 * point.z + m23);
        }

        public Vector3 MultiplyPoint(Vector3 point)
        {
            float w = m30 * point.x + m31 * point.y + m32 * point.z + m33;
            if (MathF.Abs(w) > 1e-8f) w = 1f / w;
            return new Vector3(
                (m00 * point.x + m01 * point.y + m02 * point.z + m03) * w,
                (m10 * point.x + m11 * point.y + m12 * point.z + m13) * w,
                (m20 * point.x + m21 * point.y + m22 * point.z + m23) * w);
        }

        public Vector3 MultiplyVector(Vector3 point)
        {
            return new Vector3(
                m00 * point.x + m01 * point.y + m02 * point.z,
                m10 * point.x + m11 * point.y + m12 * point.z,
                m20 * point.x + m21 * point.y + m22 * point.z);
        }

        public static bool operator ==(Matrix4x4 a, Matrix4x4 b) => a.m00 == b.m00 && a.m01 == b.m01 && a.m02 == b.m02 && a.m03 == b.m03 && a.m10 == b.m10 && a.m11 == b.m11 && a.m12 == b.m12 && a.m13 == b.m13 && a.m20 == b.m20 && a.m21 == b.m21 && a.m22 == b.m22 && a.m23 == b.m23 && a.m30 == b.m30 && a.m31 == b.m31 && a.m32 == b.m32 && a.m33 == b.m33;
        public static bool operator !=(Matrix4x4 a, Matrix4x4 b) => !(a == b);
        public override readonly bool Equals(object? o) => o is Matrix4x4 m && Equals(m);
        public readonly bool Equals(Matrix4x4 o) => this == o;
        public override readonly int GetHashCode() => HashCode.Combine(m00, m11, m22, m33);
    }
}
