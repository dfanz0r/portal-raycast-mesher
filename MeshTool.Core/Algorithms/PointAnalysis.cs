using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeshTool.Core.Data;
using MeshTool.Core.Config;

namespace MeshTool.Core.Algorithms
{
    /// <summary>
    /// Utility methods for analyzing point cloud data.
    /// </summary>
    public static class PointAnalysis
    {
        /// <summary>
        /// Estimates the average spacing between points using spatial hashing.
        /// </summary>
        /// <param name="points">The points to analyze.</param>
        /// <param name="sampleCount">Number of points to sample for estimation.</param>
        /// <returns>The estimated average nearest-neighbor distance.</returns>
        public static float EstimateAverageSpacing(IReadOnlyList<Vertex> points, int sampleCount = 5000)
        {
            if (points.Count < 2) return 1.0f;

            var bounds = CalculateBounds(points);
            double cellSize = Math.Max(Math.Max(bounds.Width, bounds.Depth) / 256.0, 0.01);

            var grid = new SpatialHashGrid<Vertex>(cellSize, v => v.Position);
            foreach (var p in points)
            {
                grid.Add(p);
            }

            int actualSampleCount = Math.Min(sampleCount, points.Count);
            var rand = new Random(42);
            double totalDist = 0;
            int validSamples = 0;

            for (int i = 0; i < actualSampleCount; i++)
            {
                var p1 = points[rand.Next(points.Count)];
                double minDistSq = double.MaxValue;

                foreach (var p2 in grid.QueryNeighborhood(p1.Position))
                {
                    if (ReferenceEquals(p1, p2)) continue;

                    double distSq = (p1.Position - p2.Position).LengthSquared();
                    if (distSq > 0.001 && distSq < minDistSq)
                    {
                        minDistSq = distSq;
                    }
                }

                if (minDistSq < double.MaxValue)
                {
                    totalDist += Math.Sqrt(minDistSq);
                    validSamples++;
                }
            }

            return validSamples > 0 ? (float)(totalDist / validSamples) : 1.0f;
        }

        /// <summary>
        /// Estimates average spacing in parallel for large point clouds.
        /// </summary>
        /// <param name="points">The points to analyze.</param>
        /// <param name="sampleCount">Number of points to sample for estimation.</param>
        /// <returns>The estimated average nearest-neighbor distance.</returns>
        public static float EstimateAverageSpacingParallel(IReadOnlyList<Vertex> points, int sampleCount = 5000)
        {
            if (points.Count < 2) return 1.0f;

            var bounds = CalculateBounds(points);
            double cellSize = Math.Max(Math.Max(bounds.Width, bounds.Depth) / 256.0, 0.01);

            // Build grid
            var gridDict = new Dictionary<long, List<Vertex>>();
            foreach (var p in points)
            {
                int gx = (int)Math.Floor(p.Position.X / cellSize);
                int gy = (int)Math.Floor(p.Position.Y / cellSize);
                int gz = (int)Math.Floor(p.Position.Z / cellSize);
                long h = ((long)gx * SpatialIndexing.HashPrimeX) ^
                         ((long)gy * SpatialIndexing.HashPrimeY) ^
                         ((long)gz * SpatialIndexing.HashPrimeZ);

                if (!gridDict.TryGetValue(h, out var bucket))
                {
                    bucket = new List<Vertex>();
                    gridDict[h] = bucket;
                }
                bucket.Add(p);
            }

            int actualSampleCount = Math.Min(sampleCount, points.Count);
            var rand = new Random(42);
            var samplePoints = new Vertex[actualSampleCount];
            for (int i = 0; i < actualSampleCount; i++)
            {
                samplePoints[i] = points[rand.Next(points.Count)];
            }

            object lockObj = new object();
            double totalDist = 0;
            int validSamples = 0;

            Parallel.ForEach(samplePoints, p1 =>
            {
                double minDistSq = double.MaxValue;

                int cx = (int)Math.Floor(p1.Position.X / cellSize);
                int cy = (int)Math.Floor(p1.Position.Y / cellSize);
                int cz = (int)Math.Floor(p1.Position.Z / cellSize);

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            long h = ((long)(cx + dx) * SpatialIndexing.HashPrimeX) ^
                                     ((long)(cy + dy) * SpatialIndexing.HashPrimeY) ^
                                     ((long)(cz + dz) * SpatialIndexing.HashPrimeZ);

                            if (gridDict.TryGetValue(h, out var bucket))
                            {
                                foreach (var p2 in bucket)
                                {
                                    if (ReferenceEquals(p1, p2)) continue;
                                    double distSq = (p1.Position - p2.Position).LengthSquared();
                                    if (distSq > 0.001 && distSq < minDistSq)
                                    {
                                        minDistSq = distSq;
                                    }
                                }
                            }
                        }
                    }
                }

                if (minDistSq < double.MaxValue)
                {
                    lock (lockObj)
                    {
                        totalDist += Math.Sqrt(minDistSq);
                        validSamples++;
                    }
                }
            });

            return validSamples > 0 ? (float)(totalDist / validSamples) : 1.0f;
        }

        /// <summary>
        /// Calculates the axis-aligned bounding box of a set of points.
        /// </summary>
        /// <param name="points">The points to calculate bounds for.</param>
        /// <returns>The bounding box containing all points.</returns>
        public static Bounds CalculateBounds(IReadOnlyList<Vertex> points)
        {
            var bounds = Bounds.Inverted();

            foreach (var p in points)
            {
                bounds.Encapsulate(p.Position);
            }

            return bounds;
        }

        /// <summary>
        /// Calculates the 2D (XZ) bounds of a set of points.
        /// </summary>
        /// <param name="points">The points to calculate bounds for.</param>
        /// <param name="hasBounds">Outputs whether any points were processed.</param>
        /// <returns>The bounding box containing all points (Y values also included).</returns>
        public static Bounds CalculateBoundsXZ(IReadOnlyList<Vertex> points, out bool hasBounds)
        {
            hasBounds = false;
            double minX = double.MaxValue, maxX = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            foreach (var p in points)
            {
                hasBounds = true;
                if (p.Position.X < minX) minX = p.Position.X;
                if (p.Position.X > maxX) maxX = p.Position.X;
                if (p.Position.Z < minZ) minZ = p.Position.Z;
                if (p.Position.Z > maxZ) maxZ = p.Position.Z;
                if (p.Position.Y < minY) minY = p.Position.Y;
                if (p.Position.Y > maxY) maxY = p.Position.Y;
            }

            return new Bounds
            {
                MinX = minX,
                MaxX = maxX,
                MinY = minY,
                MaxY = maxY,
                MinZ = minZ,
                MaxZ = maxZ
            };
        }

        /// <summary>
        /// Estimates average spacing from bounds and point count (fast approximation).
        /// </summary>
        /// <param name="pointCount">The number of points.</param>
        /// <param name="bounds">The bounding box of the points.</param>
        /// <returns>The estimated average spacing.</returns>
        public static float EstimateSpacingFromDensity(int pointCount, in Bounds bounds)
        {
            if (pointCount < 2) return 1.0f;

            double dx = Math.Max(1e-3, bounds.Width);
            double dz = Math.Max(1e-3, bounds.Depth);
            double area = dx * dz;
            double spacing = Math.Sqrt(area / Math.Max(1, pointCount));

            return (float)Math.Clamp(spacing, 0.02, 500.0);
        }

        /// <summary>
        /// Gets the minimum and maximum Y values from a set of points.
        /// </summary>
        /// <param name="points">The points to analyze.</param>
        /// <returns>A tuple of (minY, maxY).</returns>
        public static (float MinY, float MaxY) GetHeightRange(IReadOnlyList<Vertex> points)
        {
            if (points.Count == 0) return (0f, 0f);

            float minY = float.MaxValue;
            float maxY = float.MinValue;

            foreach (var p in points)
            {
                if (p.Position.Y < minY) minY = (float)p.Position.Y;
                if (p.Position.Y > maxY) maxY = (float)p.Position.Y;
            }

            return (minY, maxY);
        }
    }
}