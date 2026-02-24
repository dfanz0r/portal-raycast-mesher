using System;

namespace MeshTool.Core.Data
{
    /// <summary>
    /// Represents an axis-aligned bounding box in 3D space.
    /// </summary>
    public struct Bounds
    {
        /// <summary>
        /// The minimum X coordinate.
        /// </summary>
        public double MinX;

        /// <summary>
        /// The minimum Y coordinate.
        /// </summary>
        public double MinY;

        /// <summary>
        /// The minimum Z coordinate.
        /// </summary>
        public double MinZ;

        /// <summary>
        /// The maximum X coordinate.
        /// </summary>
        public double MaxX;

        /// <summary>
        /// The maximum Y coordinate.
        /// </summary>
        public double MaxY;

        /// <summary>
        /// The maximum Z coordinate.
        /// </summary>
        public double MaxZ;

        /// <summary>
        /// Gets the center X coordinate.
        /// </summary>
        public double MidX => (MinX + MaxX) / 2.0;

        /// <summary>
        /// Gets the center Y coordinate.
        /// </summary>
        public double MidY => (MinY + MaxY) / 2.0;

        /// <summary>
        /// Gets the center Z coordinate.
        /// </summary>
        public double MidZ => (MinZ + MaxZ) / 2.0;

        /// <summary>
        /// Gets the width (X extent) of the bounds.
        /// </summary>
        public double Width => MaxX - MinX;

        /// <summary>
        /// Gets the height (Y extent) of the bounds.
        /// </summary>
        public double Height => MaxY - MinY;

        /// <summary>
        /// Gets the depth (Z extent) of the bounds.
        /// </summary>
        public double Depth => MaxZ - MinZ;

        /// <summary>
        /// Creates an inverted bounds structure suitable for expanding to fit points.
        /// </summary>
        /// <returns>A bounds with Min values at MaxValue and Max values at MinValue.</returns>
        public static Bounds Inverted()
        {
            return new Bounds
            {
                MinX = double.MaxValue,
                MaxX = double.MinValue,
                MinY = double.MaxValue,
                MaxY = double.MinValue,
                MinZ = double.MaxValue,
                MaxZ = double.MinValue
            };
        }

        /// <summary>
        /// Creates bounds that encompass three vertices.
        /// </summary>
        /// <param name="a">The first vertex.</param>
        /// <param name="b">The second vertex.</param>
        /// <param name="c">The third vertex.</param>
        /// <returns>The bounding box containing all three vertices.</returns>
        public static Bounds FromPoints(Vertex a, Vertex b, Vertex c)
        {
            return new Bounds
            {
                MinX = Math.Min(a.Position.X, Math.Min(b.Position.X, c.Position.X)),
                MaxX = Math.Max(a.Position.X, Math.Max(b.Position.X, c.Position.X)),
                MinY = Math.Min(a.Position.Y, Math.Min(b.Position.Y, c.Position.Y)),
                MaxY = Math.Max(a.Position.Y, Math.Max(b.Position.Y, c.Position.Y)),
                MinZ = Math.Min(a.Position.Z, Math.Min(b.Position.Z, c.Position.Z)),
                MaxZ = Math.Max(a.Position.Z, Math.Max(b.Position.Z, c.Position.Z))
            };
        }

        /// <summary>
        /// Tests if a vertex is contained within these bounds.
        /// </summary>
        /// <param name="p">The vertex to test.</param>
        /// <returns>True if the vertex is inside or on the boundary.</returns>
        public bool Contains(Vertex p)
        {
            return p.Position.X >= MinX && p.Position.X <= MaxX &&
                   p.Position.Y >= MinY && p.Position.Y <= MaxY &&
                   p.Position.Z >= MinZ && p.Position.Z <= MaxZ;
        }

        /// <summary>
        /// Tests if this bounds intersects another bounds.
        /// </summary>
        /// <param name="other">The other bounds to test.</param>
        /// <returns>True if the bounds overlap.</returns>
        public bool Intersects(Bounds other)
        {
            return !(other.MinX > MaxX || other.MaxX < MinX ||
                     other.MinY > MaxY || other.MaxY < MinY ||
                     other.MinZ > MaxZ || other.MaxZ < MinZ);
        }

        /// <summary>
        /// Expands this bounds to include another bounds.
        /// </summary>
        /// <param name="other">The bounds to include.</param>
        public void Encapsulate(Bounds other)
        {
            MinX = Math.Min(MinX, other.MinX);
            MaxX = Math.Max(MaxX, other.MaxX);
            MinY = Math.Min(MinY, other.MinY);
            MaxY = Math.Max(MaxY, other.MaxY);
            MinZ = Math.Min(MinZ, other.MinZ);
            MaxZ = Math.Max(MaxZ, other.MaxZ);
        }

        /// <summary>
        /// Expands this bounds to include a point.
        /// </summary>
        /// <param name="point">The point to include.</param>
        public void Encapsulate(Vector3 point)
        {
            if (point.X < MinX) MinX = point.X;
            if (point.X > MaxX) MaxX = point.X;
            if (point.Y < MinY) MinY = point.Y;
            if (point.Y > MaxY) MaxY = point.Y;
            if (point.Z < MinZ) MinZ = point.Z;
            if (point.Z > MaxZ) MaxZ = point.Z;
        }

        /// <summary>
        /// Returns a new bounds expanded by a uniform amount in all directions.
        /// </summary>
        /// <param name="amount">The amount to expand.</param>
        /// <returns>The expanded bounds.</returns>
        public Bounds Expand(double amount)
        {
            return new Bounds
            {
                MinX = MinX - amount,
                MaxX = MaxX + amount,
                MinY = MinY - amount,
                MaxY = MaxY + amount,
                MinZ = MinZ - amount,
                MaxZ = MaxZ + amount
            };
        }
    }
}
