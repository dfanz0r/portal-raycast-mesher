using System;
using System.Threading;

namespace MeshTool.Core.Data
{
    /// <summary>
    /// Represents a triangle in 3D space with three vertices.
    /// Used for mesh representation and Delaunay triangulation.
    /// </summary>
    public class Triangle
    {
        /// <summary>
        /// The first vertex of the triangle.
        /// </summary>
        public Vertex A, B, C;

        /// <summary>
        /// Pre-computed centroid of the triangle.
        /// </summary>
        public Vertex Centroid;

        private int _isDeleted = 0; // Flag for Carving

        /// <summary>
        /// Gets or sets whether this triangle has been marked as deleted during space carving.
        /// </summary>
        public bool IsDeleted
        {
            get => Volatile.Read(ref _isDeleted) != 0;
            set => Volatile.Write(ref _isDeleted, value ? 1 : 0);
        }

        // Algorithm Fields (Delaunay)

        /// <summary>
        /// Flag used during Delaunay triangulation to mark triangles in the cavity.
        /// </summary>
        public bool IsBad = false;

        /// <summary>
        /// Neighboring triangles opposite to vertices A, B, and C respectively.
        /// Neighbor[0] is opposite A (shares edge BC).
        /// Neighbor[1] is opposite B (shares edge CA).
        /// Neighbor[2] is opposite C (shares edge AB).
        /// </summary>
        public Triangle?[] Neighbors = new Triangle?[3];

        /// <summary>
        /// Pre-computed bounding box for fast rejection during ray intersection tests.
        /// </summary>
        public Bounds Bounds;

        /// <summary>
        /// Creates a new triangle from three vertices.
        /// </summary>
        /// <param name="a">The first vertex.</param>
        /// <param name="b">The second vertex.</param>
        /// <param name="c">The third vertex.</param>
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

        /// <summary>
        /// Tests if a ray intersects this triangle using the Möller–Trumbore algorithm.
        /// </summary>
        /// <param name="origin">The ray origin.</param>
        /// <param name="direction">The normalized ray direction.</param>
        /// <param name="t">Outputs the distance along the ray to the intersection point.</param>
        /// <returns>True if the ray intersects the triangle, false otherwise.</returns>
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
            return t > 1e-6;
        }

        /// <summary>
        /// Atomically marks this triangle as deleted.
        /// </summary>
        /// <returns>True if this call marked the triangle as deleted (was not already deleted).</returns>
        public bool TryMarkDeleted()
        {
            return Interlocked.CompareExchange(ref _isDeleted, 1, 0) == 0;
        }
    }
}
