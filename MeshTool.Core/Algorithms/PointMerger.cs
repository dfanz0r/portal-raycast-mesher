using System;
using System.Collections.Generic;
using MeshTool.Core.Data;
using MeshTool.Core.Config;

namespace MeshTool.Core.Algorithms
{
    /// <summary>
    /// Provides methods for merging point clouds while removing duplicates.
    /// </summary>
    public static class PointMerger
    {
        /// <summary>
        /// Merges candidate points into a master list, skipping duplicates within a minimum distance.
        /// When a duplicate is found, the spawn time of the existing point is updated if the candidate
        /// has a more recent spawn time.
        /// </summary>
        /// <param name="master">The master list of points to merge into.</param>
        /// <param name="candidates">The candidate points to potentially add.</param>
        /// <param name="minDistance">The minimum distance between points. Points closer than this are considered duplicates.</param>
        /// <returns>The number of points actually added to the master list.</returns>
        public static int MergePoints(List<Vertex> master, List<Vertex> candidates, double minDistance)
        {
            if (minDistance <= 0)
                throw new ArgumentOutOfRangeException(nameof(minDistance), "Minimum distance must be positive.");
            if (master == null)
                throw new ArgumentNullException(nameof(master));
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            double cellSize = minDistance * SpatialIndexing.CellSizeMultiplier;
            double minSq = minDistance * minDistance;
            int addedCount = 0;

            // Build spatial hash grid from master points
            var grid = new Dictionary<long, List<Vertex>>();

            foreach (var p in master)
            {
                AddToGrid(grid, p, cellSize);
            }

            // Process candidates
            foreach (var candidate in candidates)
            {
                if (IsTooClose(grid, candidate, cellSize, minSq, out var existing))
                {
                    // Update spawn time if candidate is newer
                    if (existing != null && candidate.SpawnTime > existing.SpawnTime)
                    {
                        existing.SpawnTime = candidate.SpawnTime;
                    }
                }
                else
                {
                    // Add new point
                    master.Add(candidate);
                    AddToGrid(grid, candidate, cellSize);
                    addedCount++;
                }
            }

            return addedCount;
        }

        /// <summary>
        /// Merges candidate points into a master list using a pre-computed average spacing.
        /// This is useful when the average point spacing is already known.
        /// </summary>
        /// <param name="master">The master list of points to merge into.</param>
        /// <param name="candidates">The candidate points to potentially add.</param>
        /// <param name="averageSpacing">The average spacing between points (used as minimum distance).</param>
        /// <param name="spacingMultiplier">Multiplier applied to average spacing for duplicate detection. Default is 0.5.</param>
        /// <returns>The number of points actually added to the master list.</returns>
        public static int MergePointsWithSpacing(List<Vertex> master, List<Vertex> candidates, double averageSpacing, double spacingMultiplier = 0.5)
        {
            double minDistance = averageSpacing * spacingMultiplier;
            return MergePoints(master, candidates, minDistance);
        }

        private static void AddToGrid(Dictionary<long, List<Vertex>> grid, Vertex p, double cellSize)
        {
            long h = GetHash(p.Position, cellSize);
            if (!grid.TryGetValue(h, out var bucket))
            {
                bucket = new List<Vertex>();
                grid[h] = bucket;
            }
            bucket.Add(p);
        }

        private static bool IsTooClose(Dictionary<long, List<Vertex>> grid, Vertex p, double cellSize, double minSq, out Vertex? existing)
        {
            existing = null;
            int cx = (int)Math.Floor(p.Position.X / cellSize);
            int cy = (int)Math.Floor(p.Position.Y / cellSize);
            int cz = (int)Math.Floor(p.Position.Z / cellSize);

            // Check current cell and all 26 neighbors (3x3x3 grid)
            // This ensures we find close points even if they cross cell boundaries
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        long neighborHash = HashCell(cx + dx, cy + dy, cz + dz);

                        if (grid.TryGetValue(neighborHash, out var bucket))
                        {
                            for (int i = 0; i < bucket.Count; i++)
                            {
                                var existingVertex = bucket[i];
                                double distSq = DistanceSquared(p.Position, existingVertex.Position);

                                if (distSq < minSq)
                                {
                                    existing = existingVertex;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static long GetHash(Vector3 position, double cellSize)
        {
            int gx = (int)Math.Floor(position.X / cellSize);
            int gy = (int)Math.Floor(position.Y / cellSize);
            int gz = (int)Math.Floor(position.Z / cellSize);
            return HashCell(gx, gy, gz);
        }

        private static long HashCell(int gx, int gy, int gz)
        {
            return ((long)gx * SpatialIndexing.HashPrimeX) ^
                   ((long)gy * SpatialIndexing.HashPrimeY) ^
                   ((long)gz * SpatialIndexing.HashPrimeZ);
        }

        private static double DistanceSquared(Vector3 a, Vector3 b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }
    }
}
