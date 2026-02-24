using System;

namespace MeshTool.Core.Data
{
    /// <summary>
    /// Represents a ray in 3D space with start and end points.
    /// Used for raycast miss visualization and space carving operations.
    /// </summary>
    public struct Ray
    {
        /// <summary>
        /// The start position of the ray.
        /// </summary>
        public Vector3 Start;

        /// <summary>
        /// The end position of the ray.
        /// </summary>
        public Vector3 End;

        /// <summary>
        /// The time when this ray was created (for animation purposes).
        /// </summary>
        public float SpawnTime;

        /// <summary>
        /// Gets the normalized direction of the ray.
        /// </summary>
        /// <param name="length">Outputs the length of the ray.</param>
        /// <returns>The normalized direction vector.</returns>
        public Vector3 GetDirection(out double length)
        {
            Vector3 diff = End - Start;
            length = diff.Length();
            return diff.Normalized();
        }

        /// <summary>
        /// Gets the axis-aligned bounding box of this ray.
        /// </summary>
        public Bounds Bounds => new Bounds
        {
            MinX = Math.Min(Start.X, End.X),
            MaxX = Math.Max(Start.X, End.X),
            MinY = Math.Min(Start.Y, End.Y),
            MaxY = Math.Max(Start.Y, End.Y),
            MinZ = Math.Min(Start.Z, End.Z),
            MaxZ = Math.Max(Start.Z, End.Z)
        };
    }
}
