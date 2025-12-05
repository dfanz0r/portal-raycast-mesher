using System;
using System.Collections.Generic;
using TerrainTool.Data;

namespace TerrainTool.Algorithms
{
    public static class PointMerger
    {
        public static int MergePoints(List<Vertex> master, List<Vertex> candidates, double minDistance)
        {
            Dictionary<long, List<Vertex>> grid = new Dictionary<long, List<Vertex>>();
            double cellSize = minDistance * 4;
            double minSq = minDistance * minDistance;
            int addedCount = 0;

            long GetHash(double x, double y, double z)
            {
                int gx = (int)Math.Floor(x / cellSize);
                int gy = (int)Math.Floor(y / cellSize);
                int gz = (int)Math.Floor(z / cellSize);
                // 3D Spatial Hashing
                return ((long)gx * 73856093) ^ ((long)gy * 19349663) ^ ((long)gz * 83492791);
            }

            // Index master points
            foreach (var p in master)
            {
                long h = GetHash(p.Position.X, p.Position.Y, p.Position.Z);
                if (!grid.ContainsKey(h))
                {
                    grid[h] = new List<Vertex>();
                }
                grid[h].Add(p);
            }

            // Check candidates
            foreach (var p in candidates)
            {
                long h = GetHash(p.Position.X, p.Position.Y, p.Position.Z);
                bool tooClose = false;

                // Check current cell and all 26 neighbors (3x3x3 grid)
                // This ensures we find close points even if they cross cell boundaries.
                int cx = (int)Math.Floor(p.Position.X / cellSize);
                int cy = (int)Math.Floor(p.Position.Y / cellSize);
                int cz = (int)Math.Floor(p.Position.Z / cellSize);

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            long neighborHash = ((long)(cx + dx) * 73856093) ^ ((long)(cy + dy) * 19349663) ^ ((long)(cz + dz) * 83492791);

                            if (grid.ContainsKey(neighborHash))
                            {
                                foreach (var existing in grid[neighborHash])
                                {
                                    double distSq =
                                        ((p.Position.X - existing.Position.X) * (p.Position.X - existing.Position.X)) +
                                        ((p.Position.Y - existing.Position.Y) * (p.Position.Y - existing.Position.Y)) +
                                        ((p.Position.Z - existing.Position.Z) * (p.Position.Z - existing.Position.Z));

                                    if (distSq < minSq)
                                    {
                                        tooClose = true;
                                        goto FoundDuplicate; // Break out of all loops
                                    }
                                }
                            }
                        }
                    }
                }

            FoundDuplicate:
                if (!tooClose)
                {
                    master.Add(p);
                    if (!grid.ContainsKey(h))
                    {
                        grid[h] = new List<Vertex>();
                    }
                    grid[h].Add(p);
                    addedCount++;
                }
            }
            return addedCount;
        }
    }
}
