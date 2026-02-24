using System;

namespace MeshTool.Core.Data
{
    /// <summary>
    /// Represents a 3D vector with double-precision components.
    /// </summary>
    public struct Vector3
    {
        /// <summary>
        /// The X component of the vector.
        /// </summary>
        public double X;

        /// <summary>
        /// The Y component of the vector.
        /// </summary>
        public double Y;

        /// <summary>
        /// The Z component of the vector.
        /// </summary>
        public double Z;

        /// <summary>
        /// Initializes a new instance of the <see cref="Vector3"/> struct.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        /// <param name="z">The Z component.</param>
        public Vector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Adds two vectors together.
        /// </summary>
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        /// <summary>
        /// Subtracts one vector from another.
        /// </summary>
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        /// <summary>
        /// Scales a vector by a scalar value.
        /// </summary>
        public static Vector3 operator *(Vector3 a, double d) => new Vector3(a.X * d, a.Y * d, a.Z * d);

        /// <summary>
        /// Computes the dot product of this vector with another.
        /// </summary>
        /// <param name="other">The other vector.</param>
        /// <returns>The dot product.</returns>
        public double Dot(Vector3 other) => X * other.X + Y * other.Y + Z * other.Z;

        /// <summary>
        /// Computes the cross product of this vector with another.
        /// </summary>
        /// <param name="other">The other vector.</param>
        /// <returns>The cross product vector.</returns>
        public Vector3 Cross(Vector3 other) => new Vector3(
            Y * other.Z - Z * other.Y,
            Z * other.X - X * other.Z,
            X * other.Y - Y * other.X
        );

        /// <summary>
        /// Returns the length (magnitude) of this vector.
        /// </summary>
        /// <returns>The length of the vector.</returns>
        public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);

        /// <summary>
        /// Returns the squared length of this vector.
        /// This is faster than <see cref="Length"/> as it avoids the square root.
        /// </summary>
        /// <returns>The squared length of the vector.</returns>
        public double LengthSquared() => X * X + Y * Y + Z * Z;

        /// <summary>
        /// Returns a normalized (unit length) version of this vector.
        /// </summary>
        /// <returns>A new vector with the same direction but unit length, or a zero vector if this vector is too small.</returns>
        public Vector3 Normalized()
        {
            double len = Length();
            return len > 1e-9 ? this * (1.0 / len) : new Vector3(0, 0, 0);
        }
    }
}
