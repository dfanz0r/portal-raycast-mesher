using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TerrainTool.Config;
using TerrainTool.Data;

namespace TerrainTool.Algorithms
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

                // Calculate Ray Bounds for AABB check
                Bounds rayBounds = ray.Bounds;

                var candidates = new HashSet<Triangle>();
                TriangleQuadtree.Query(quadtree, rayBounds, candidates);

                foreach (var tri in candidates)
                {
                    if (tri.IsDeleted) continue;

                    if (tri.Intersects(ray.Start, direction, out double t))
                    {
                        lock (tri)
                        {
                            if (!tri.IsDeleted)
                            {
                                tri.IsDeleted = true;
                                Interlocked.Increment(ref deletedCount);
                            }
                        }
                    }
                }
            });

            return deletedCount;
        }
    }
}
