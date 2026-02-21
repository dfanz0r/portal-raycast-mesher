using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MeshTool.Core.Config;
using MeshTool.Core.Data;

namespace MeshTool.Core.Algorithms
{
    public static class SpaceCarver
    {
        public static int CarveMesh(TriangleQuadtree quadtree, List<Ray> rays)
        {
            int deletedCount = 0;

            // Process Rays in parallel
            Parallel.ForEach(rays, ray =>
            {
                Vector3 direction = ray.GetDirection(out double rLen);
                if (rLen <= 1e-6) return;

                // Calculate Ray Bounds for AABB check
                Bounds rayBounds = ray.Bounds;

                var candidates = new HashSet<Triangle>();
                TriangleQuadtree.Query(quadtree, rayBounds, candidates);

                foreach (var tri in candidates)
                {
                    if (tri.IsDeleted) continue;

                    if (tri.Intersects(ray.Start, direction, out double t) && t <= (rLen + 1e-6))
                    {
                        if (tri.TryMarkDeleted())
                        {
                            Interlocked.Increment(ref deletedCount);
                        }
                    }
                }
            });

            return deletedCount;
        }
    }
}
