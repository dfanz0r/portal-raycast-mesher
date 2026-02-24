using System;
using System.Collections.Generic;
using MeshTool.Core.Data;
using MeshTool.Core.Config;

namespace MeshTool.Core.Algorithms
{
    /// <summary>
    /// Incremental spatial index for deduplicating points during streaming ingestion.
    /// </summary>
    public sealed class IncrementalPointIndex
    {
        private readonly Dictionary<long, List<Vertex>> _grid = new Dictionary<long, List<Vertex>>();
        private readonly double _cellSize;
        private readonly double _minSq;
        private readonly bool _refreshExistingSpawnTime;

        /// <summary>
        /// Initializes a new incremental point index.
        /// </summary>
        /// <param name="existingPoints">Points to pre-populate the index.</param>
        /// <param name="minDistance">Minimum distance for duplicate detection.</param>
        /// <param name="refreshExistingSpawnTime">Whether to update spawn times of existing points.</param>
        public IncrementalPointIndex(IEnumerable<Vertex> existingPoints, double minDistance, bool refreshExistingSpawnTime = true)
        {
            if (minDistance <= 0) throw new ArgumentOutOfRangeException(nameof(minDistance));

            _cellSize = minDistance * SpatialIndexing.CellSizeMultiplier;
            _minSq = minDistance * minDistance;
            _refreshExistingSpawnTime = refreshExistingSpawnTime;

            foreach (var p in existingPoints)
                AddToGrid(p);
        }

        /// <summary>
        /// Attempts to add multiple candidates to the master list.
        /// </summary>
        /// <returns>The number of points actually added.</returns>
        public int AddRange(List<Vertex> master, List<Vertex> candidates)
        {
            int added = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (TryAdd(master, candidates[i]))
                    added++;
            }
            return added;
        }

        /// <summary>
        /// Attempts to add a single candidate to the master list.
        /// </summary>
        /// <returns>True if the point was added, false if it was a duplicate.</returns>
        public bool TryAdd(List<Vertex> master, Vertex candidate)
        {
            if (IsTooClose(candidate, out var existing))
            {
                if (_refreshExistingSpawnTime && existing != null && candidate.SpawnTime > existing.SpawnTime)
                {
                    existing.SpawnTime = candidate.SpawnTime;
                }
                return false;
            }

            master.Add(candidate);
            AddToGrid(candidate);
            return true;
        }

        private bool IsTooClose(Vertex p, out Vertex? existingPoint)
        {
            existingPoint = null;
            int cx = (int)Math.Floor(p.Position.X / _cellSize);
            int cy = (int)Math.Floor(p.Position.Y / _cellSize);
            int cz = (int)Math.Floor(p.Position.Z / _cellSize);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        long neighborHash = HashCell(cx + dx, cy + dy, cz + dz);
                        if (!_grid.TryGetValue(neighborHash, out var bucket))
                            continue;

                        for (int i = 0; i < bucket.Count; i++)
                        {
                            var existing = bucket[i];
                            double dxp = p.Position.X - existing.Position.X;
                            double dyp = p.Position.Y - existing.Position.Y;
                            double dzp = p.Position.Z - existing.Position.Z;
                            double distSq = (dxp * dxp) + (dyp * dyp) + (dzp * dzp);

                            if (distSq < _minSq)
                            {
                                existingPoint = existing;
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private void AddToGrid(Vertex p)
        {
            int gx = (int)Math.Floor(p.Position.X / _cellSize);
            int gy = (int)Math.Floor(p.Position.Y / _cellSize);
            int gz = (int)Math.Floor(p.Position.Z / _cellSize);

            long h = HashCell(gx, gy, gz);
            if (!_grid.TryGetValue(h, out var bucket))
            {
                bucket = new List<Vertex>();
                _grid[h] = bucket;
            }
            bucket.Add(p);
        }

        private static long HashCell(int gx, int gy, int gz)
        {
            return ((long)gx * SpatialIndexing.HashPrimeX) ^ ((long)gy * SpatialIndexing.HashPrimeY) ^ ((long)gz * SpatialIndexing.HashPrimeZ);
        }
    }
}
