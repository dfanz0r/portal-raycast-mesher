using System;
using System.Collections.Generic;
using TerrainTool.Data;

namespace TerrainTool.Algorithms
{
    public sealed class IncrementalPointIndex
    {
        private readonly Dictionary<long, List<Vertex>> _grid = new Dictionary<long, List<Vertex>>();
        private readonly double _cellSize;
        private readonly double _minSq;

        public IncrementalPointIndex(IEnumerable<Vertex> existingPoints, double minDistance)
        {
            if (minDistance <= 0) throw new ArgumentOutOfRangeException(nameof(minDistance));

            _cellSize = minDistance * 4;
            _minSq = minDistance * minDistance;

            foreach (var p in existingPoints)
                AddToGrid(p);
        }

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

        public bool TryAdd(List<Vertex> master, Vertex candidate)
        {
            if (IsTooClose(candidate))
                return false;

            master.Add(candidate);
            AddToGrid(candidate);
            return true;
        }

        private bool IsTooClose(Vertex p)
        {
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
                                return true;
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
            return ((long)gx * 73856093) ^ ((long)gy * 19349663) ^ ((long)gz * 83492791);
        }
    }
}
