using System;

namespace TerrainTool.Data
{
    public struct Vector3
    {
        public double X, Y, Z;

        public Vector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3 operator *(Vector3 a, double d) => new Vector3(a.X * d, a.Y * d, a.Z * d);
        
        public double Dot(Vector3 other) => X * other.X + Y * other.Y + Z * other.Z;
        
        public Vector3 Cross(Vector3 other) => new Vector3(
            Y * other.Z - Z * other.Y,
            Z * other.X - X * other.Z,
            X * other.Y - Y * other.X
        );

        public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
        
        public Vector3 Normalized()
        {
            double len = Length();
            return len > 1e-9 ? this * (1.0 / len) : new Vector3(0, 0, 0);
        }
    }
}
