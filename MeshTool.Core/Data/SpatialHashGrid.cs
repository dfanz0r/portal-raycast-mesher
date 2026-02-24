using System;
using System.Collections.Generic;
using MeshTool.Core.Config;

namespace MeshTool.Core.Data
{
    /// <summary>
    /// A generic spatial hash grid for efficient nearest-neighbor queries.
    /// </summary>
    /// <typeparam name="T">The type of items stored in the grid.</typeparam>
    public class SpatialHashGrid<T> where T : class
    {
        private readonly Dictionary<long, List<T>> _grid = new();
        private readonly double _cellSize;
        private readonly Func<T, Vector3> _getPosition;
        private readonly double _minDistanceSq;

        /// <summary>
        /// Initializes a new spatial hash grid.
        /// </summary>
        /// <param name="cellSize">The size of each grid cell.</param>
        /// <param name="getPosition">Function to extract position from an item.</param>
        /// <param name="minDistance">Minimum distance threshold for proximity queries.</param>
        public SpatialHashGrid(double cellSize, Func<T, Vector3> getPosition, double minDistance = 0)
        {
            _cellSize = cellSize;
            _getPosition = getPosition;
            _minDistanceSq = minDistance * minDistance;
        }

        /// <summary>
        /// Computes the hash key for a position.
        /// </summary>
        public long GetHash(Vector3 position)
        {
            int gx = (int)Math.Floor(position.X / _cellSize);
            int gy = (int)Math.Floor(position.Y / _cellSize);
            int gz = (int)Math.Floor(position.Z / _cellSize);
            return HashCell(gx, gy, gz);
        }

        /// <summary>
        /// Computes the hash key for grid coordinates.
        /// </summary>
        public static long HashCell(int gx, int gy, int gz)
        {
            return ((long)gx * SpatialIndexing.HashPrimeX) ^
                   ((long)gy * SpatialIndexing.HashPrimeY) ^
                   ((long)gz * SpatialIndexing.HashPrimeZ);
        }

        /// <summary>
        /// Adds an item to the grid.
        /// </summary>
        public void Add(T item)
        {
            var pos = _getPosition(item);
            long h = GetHash(pos);
            if (!_grid.TryGetValue(h, out var bucket))
            {
                bucket = new List<T>();
                _grid[h] = bucket;
            }
            bucket.Add(item);
        }

        /// <summary>
        /// Removes an item from the grid.
        /// </summary>
        public bool Remove(T item)
        {
            var pos = _getPosition(item);
            long h = GetHash(pos);
            if (_grid.TryGetValue(h, out var bucket))
            {
                return bucket.Remove(item);
            }
            return false;
        }

        /// <summary>
        /// Gets the grid coordinates for a position.
        /// </summary>
        public (int X, int Y, int Z) GetCell(Vector3 position)
        {
            return (
                (int)Math.Floor(position.X / _cellSize),
                (int)Math.Floor(position.Y / _cellSize),
                (int)Math.Floor(position.Z / _cellSize)
            );
        }

        /// <summary>
        /// Queries all items in the 3x3x3 neighborhood of a position.
        /// </summary>
        public IEnumerable<T> QueryNeighborhood(Vector3 position)
        {
            var (cx, cy, cz) = GetCell(position);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        long h = HashCell(cx + dx, cy + dy, cz + dz);
                        if (_grid.TryGetValue(h, out var bucket))
                        {
                            foreach (var item in bucket)
                            {
                                yield return item;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Finds if any existing item is too close to the given position.
        /// </summary>
        /// <param name="position">The position to check.</param>
        /// <param name="closestItem">Outputs the closest item found, if any.</param>
        /// <param name="customMinDistanceSq">Optional custom minimum distance squared.</param>
        /// <returns>True if an item is within the minimum distance.</returns>
        public bool IsTooClose(Vector3 position, out T? closestItem, double? customMinDistanceSq = null)
        {
            closestItem = null;
            double minSq = customMinDistanceSq ?? _minDistanceSq;

            if (minSq <= 0) return false;

            double bestDistSq = double.MaxValue;

            foreach (var item in QueryNeighborhood(position))
            {
                var itemPos = _getPosition(item);
                double dx = position.X - itemPos.X;
                double dy = position.Y - itemPos.Y;
                double dz = position.Z - itemPos.Z;
                double distSq = dx * dx + dy * dy + dz * dz;

                if (distSq < minSq && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    closestItem = item;
                }
            }

            return closestItem != null;
        }

        /// <summary>
        /// Finds the nearest item to the given position.
        /// </summary>
        /// <param name="position">The position to search from.</param>
        /// <param name="maxDistance">Maximum search distance.</param>
        /// <returns>The nearest item, or null if none found within max distance.</returns>
        public T? FindNearest(Vector3 position, double maxDistance = double.MaxValue)
        {
            T? nearest = null;
            double bestDistSq = maxDistance * maxDistance;

            foreach (var item in QueryNeighborhood(position))
            {
                var itemPos = _getPosition(item);
                double dx = position.X - itemPos.X;
                double dy = position.Y - itemPos.Y;
                double dz = position.Z - itemPos.Z;
                double distSq = dx * dx + dy * dy + dz * dz;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    nearest = item;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Clears all items from the grid.
        /// </summary>
        public void Clear()
        {
            _grid.Clear();
        }

        /// <summary>
        /// Gets the total number of items in the grid.
        /// </summary>
        public int Count
        {
            get
            {
                int count = 0;
                foreach (var bucket in _grid.Values)
                {
                    count += bucket.Count;
                }
                return count;
            }
        }

        /// <summary>
        /// Gets the number of cells in the grid.
        /// </summary>
        public int CellCount => _grid.Count;
    }
}