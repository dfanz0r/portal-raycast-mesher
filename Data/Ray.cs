using System;

namespace TerrainTool.Data
{
    public struct Ray
    {
        public Vector3 Start; // Start Position
        public Vector3 End;   // End Position

        public Vector3 GetDirection(out double length)
        {
            Vector3 diff = End - Start;
            length = diff.Length();
            return diff.Normalized();
        }

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
