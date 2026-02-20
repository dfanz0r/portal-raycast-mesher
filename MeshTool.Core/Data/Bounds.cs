using System;

namespace TerrainTool.Data
{
    public struct Bounds
    {
        public double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

        public double MidX => (MinX + MaxX) / 2.0;
        public double MidY => (MinY + MaxY) / 2.0;
        public double MidZ => (MinZ + MaxZ) / 2.0;
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public double Depth => MaxZ - MinZ;

        public static Bounds Inverted()
        {
            return new Bounds
            {
                MinX = double.MaxValue, MaxX = double.MinValue,
                MinY = double.MaxValue, MaxY = double.MinValue,
                MinZ = double.MaxValue, MaxZ = double.MinValue
            };
        }

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

        public bool Contains(Vertex p)
        {
            return p.Position.X >= MinX && p.Position.X <= MaxX && 
                   p.Position.Y >= MinY && p.Position.Y <= MaxY && 
                   p.Position.Z >= MinZ && p.Position.Z <= MaxZ;
        }

        public bool Intersects(Bounds other)
        {
            return !(other.MinX > MaxX || other.MaxX < MinX || 
                     other.MinY > MaxY || other.MaxY < MinY || 
                     other.MinZ > MaxZ || other.MaxZ < MinZ);
        }

        public void Encapsulate(Bounds other)
        {
            MinX = Math.Min(MinX, other.MinX);
            MaxX = Math.Max(MaxX, other.MaxX);
            MinY = Math.Min(MinY, other.MinY);
            MaxY = Math.Max(MaxY, other.MaxY);
            MinZ = Math.Min(MinZ, other.MinZ);
            MaxZ = Math.Max(MaxZ, other.MaxZ);
        }

        public void Encapsulate(Vector3 point)
        {
            if (point.X < MinX) MinX = point.X;
            if (point.X > MaxX) MaxX = point.X;
            if (point.Y < MinY) MinY = point.Y;
            if (point.Y > MaxY) MaxY = point.Y;
            if (point.Z < MinZ) MinZ = point.Z;
            if (point.Z > MaxZ) MaxZ = point.Z;
        }

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
