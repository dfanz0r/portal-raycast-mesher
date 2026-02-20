using System;

namespace TerrainTool.Data
{
    public class Triangle
    {
        public Vertex A, B, C;
        public Vertex Centroid;
        public bool IsDeleted = false; // Flag for Carving
        
        // Algorithm Fields (Delaunay)
        public bool IsBad = false;
        public Triangle?[] Neighbors = new Triangle?[3]; // Neighbors opposite to A, B, C

        // Bounding Box for fast rejection during carving
        public Bounds Bounds;

        public Triangle(Vertex a, Vertex b, Vertex c)
        {
            A = a;
            B = b;
            C = c;

            Centroid = new Vertex
            {
                Position = (a.Position + b.Position + c.Position) * (1.0 / 3.0)
            };

            // Pre-calc bounds for physics speedup
            Bounds = Bounds.FromPoints(a, b, c);
        }

        public bool Intersects(Vector3 origin, Vector3 direction, out double t)
        {
            t = 0;
            Vector3 vA = A.Position;
            Vector3 vB = B.Position;
            Vector3 vC = C.Position;

            Vector3 edge1 = vB - vA;
            Vector3 edge2 = vC - vA;

            Vector3 h = direction.Cross(edge2);
            double a = edge1.Dot(h);

            if (a > -1e-7 && a < 1e-7) return false; // Parallel

            double f = 1.0 / a;
            Vector3 s = origin - vA;
            double u = f * s.Dot(h);

            if (u < 0.0 || u > 1.0) return false;

            Vector3 q = s.Cross(edge1);
            double v = f * direction.Dot(q);

            if (v < 0.0 || u + v > 1.0) return false;

            t = f * edge2.Dot(q);
            return true;
        }
    }
}
