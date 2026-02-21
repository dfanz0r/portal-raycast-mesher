using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MeshTool.Core.Data;
using MeshTool.Core.Config;

namespace MeshTool.Core.Algorithms
{
    public static class DelaunayMesher
    {
        public static List<Triangle> GenerateMesh(List<Vertex> inputPoints, Action<float[], int>? onProgressRaw = null, Func<bool>? isRendererBusy = null)
        {
            Console.WriteLine("[MESH] Starting Global Delaunay Triangulation...");

            // 1. Deduplicate (Critical for stability)
            var points = Deduplicate(inputPoints);
            Console.WriteLine($"[MESH] Processing {points.Count} unique points.");

            if (points.Count < 3) return new List<Triangle>();

            // 2. Setup Super Triangle
            Bounds bounds = GetBounds(points);
            double maxDim = Math.Max(bounds.Width, bounds.Depth);
            double midX = bounds.MidX;
            double midZ = bounds.MidZ;

            // Large super triangle
            Vertex s1 = new Vertex { Position = new Vector3(midX - 20 * maxDim, 0, midZ - maxDim) };
            Vertex s2 = new Vertex { Position = new Vector3(midX, 0, midZ + 20 * maxDim) };
            Vertex s3 = new Vertex { Position = new Vector3(midX + 20 * maxDim, 0, midZ - maxDim) };

            Triangle superTri = new Triangle(s1, s2, s3);
            List<Triangle> triangles = new List<Triangle> { superTri };

            // 3. Incremental Triangulation
            // Sort points by Z-order curve (Morton code) to optimize the "Walk" location strategy
            // This ensures consecutive points are spatially close in both X and Z dimensions,
            // drastically reducing the number of steps in the walk and avoiding O(N) fallback searches.
            points = points.OrderBy(p => GetMortonCode(p.Position.X, p.Position.Z, bounds)).ToList();

            // Optimization: Keep track of the last triangle to start the search
            Triangle lastTri = superTri;

            int count = 0;
            System.Threading.Tasks.Task? progressTask = null;
            foreach (var p in points)
            {
                if (++count % 10000 == 0) Console.Write(".");

                // Step A: Locate the triangle containing point p (or close to it)
                Triangle? startNode = FindTriangleContainingPoint(lastTri, p.Position);

                // Fallback if walk failed (rare)
                if (startNode == null)
                {
                    foreach (var tri in triangles)
                    {
                        if (!tri.IsBad && IsPointInCircumcircle(p.Position, tri))
                        {
                            startNode = tri;
                            break;
                        }
                    }
                }
                else
                {
                    // Verify the walk result is actually valid for starting the search
                    // If the walk returned a boundary triangle but the point is far outside,
                    // IsPointInCircumcircle might be false.
                    if (!IsPointInCircumcircle(p.Position, startNode))
                    {
                        // Force linear search to find a better candidate
                        startNode = null;
                        foreach (var tri in triangles)
                        {
                            if (!tri.IsBad && IsPointInCircumcircle(p.Position, tri))
                            {
                                startNode = tri;
                                break;
                            }
                        }
                    }
                }

                if (startNode == null) continue;

                // Step B: BFS to find all bad triangles (the Cavity)
                List<Triangle> cavity = new List<Triangle>();
                Queue<Triangle> queue = new Queue<Triangle>();
                HashSet<Triangle> visited = new HashSet<Triangle>();

                if (IsPointInCircumcircle(p.Position, startNode))
                {
                    queue.Enqueue(startNode);
                    visited.Add(startNode);
                }

                while (queue.Count > 0)
                {
                    var curr = queue.Dequeue();
                    cavity.Add(curr);
                    curr.IsBad = true;

                    // Check neighbors
                    for (int i = 0; i < 3; i++)
                    {
                        var n = curr.Neighbors[i];
                        if (n != null && !visited.Contains(n) && !n.IsBad)
                        {
                            if (IsPointInCircumcircle(p.Position, n))
                            {
                                visited.Add(n);
                                queue.Enqueue(n);
                            }
                        }
                    }
                }

                // Step C: Build the Polygon (Boundary of the cavity)
                List<Edge> boundary = new List<Edge>();

                foreach (var tri in cavity)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var neighbor = tri.Neighbors[i];
                        // If neighbor is null or not bad, this edge is on the boundary
                        if (neighbor == null || !neighbor.IsBad)
                        {
                            // Get the edge vertices.
                            // Neighbor 0 is edge BC. Neighbor 1 is CA. Neighbor 2 is AB.
                            Vertex u, v;
                            if (i == 0) { u = tri.B; v = tri.C; }
                            else if (i == 1) { u = tri.C; v = tri.A; }
                            else { u = tri.A; v = tri.B; }

                            boundary.Add(new Edge { U = u, V = v, Neighbor = neighbor, OldTri = tri });
                        }
                    }
                }

                // Step E: Create new triangles connecting p to the boundary
                List<Triangle> newTriangles = new List<Triangle>();
                foreach (var edge in boundary)
                {
                    Triangle newTri = new Triangle(edge.U, edge.V, p);

                    // Connect to the existing outer neighbor
                    // newTri is (U, V, P). Edge UV is opposite P (Vertex C -> Neighbor 2).
                    newTri.Neighbors[2] = edge.Neighbor;

                    // Update the outer neighbor to point back to newTri
                    if (edge.Neighbor != null)
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            // Strictly check if the neighbor points to the specific OldTri that generated this edge
                            if (edge.Neighbor.Neighbors[k] == edge.OldTri)
                            {
                                edge.Neighbor.Neighbors[k] = newTri;
                                break;
                            }
                        }
                    }
                    newTriangles.Add(newTri);
                }

                // Step F: Link the new triangles together
                int newCount = newTriangles.Count;
                for (int i = 0; i < newCount; i++)
                {
                    Triangle t1 = newTriangles[i];
                    // t1: U, V, P. Neighbors[0] is VP, Neighbors[1] is PU.

                    for (int j = 0; j < newCount; j++)
                    {
                        if (i == j) continue;
                        Triangle t2 = newTriangles[j];

                        // If t1.V == t2.U, then they share the edge P-V.
                        // t1.B is V. t2.A is U.
                        if (t1.B == t2.A)
                        {
                            // t1's Neighbor 0 (VP) connects to t2
                            // t2's Neighbor 1 (PU) connects to t1
                            t1.Neighbors[0] = t2;
                            t2.Neighbors[1] = t1;
                        }
                    }
                }

                if (newTriangles.Count > 0)
                {
                    triangles.AddRange(newTriangles);
                    lastTri = newTriangles[0];
                }

                if (onProgressRaw != null && count % 5000 == 0)
                {
                    bool isBusy = isRendererBusy?.Invoke() ?? false;
                    if (!isBusy && (progressTask == null || progressTask.IsCompleted))
                    {
                        var snapshot = triangles.ToArray();
                        progressTask = System.Threading.Tasks.Task.Run(() =>
                        {
                            int validCount = 0;
                            for (int i = 0; i < snapshot.Length; i++)
                            {
                                var t = snapshot[i];
                                if (!t.IsBad && !IsConnectedTo(t, s1) && !IsConnectedTo(t, s2) && !IsConnectedTo(t, s3))
                                    validCount++;
                            }

                            if (validCount == 0) return;

                            var buffer = System.Buffers.ArrayPool<float>.Shared.Rent(validCount * 18);
                            int idx = 0;
                            for (int i = 0; i < snapshot.Length; i++)
                            {
                                var t = snapshot[i];
                                if (t.IsBad || IsConnectedTo(t, s1) || IsConnectedTo(t, s2) || IsConnectedTo(t, s3)) continue;

                                var edge1 = t.B.Position - t.A.Position;
                                var edge2 = t.C.Position - t.A.Position;
                                var n = edge1.Cross(edge2);
                                double len = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
                                if (len > 1e-7) { n.X /= len; n.Y /= len; n.Z /= len; }
                                else n = new Vector3(0, 1, 0);

                                buffer[idx++] = (float)t.A.Position.X; buffer[idx++] = (float)t.A.Position.Y; buffer[idx++] = (float)t.A.Position.Z;
                                buffer[idx++] = (float)n.X; buffer[idx++] = (float)n.Y; buffer[idx++] = (float)n.Z;
                                buffer[idx++] = (float)t.B.Position.X; buffer[idx++] = (float)t.B.Position.Y; buffer[idx++] = (float)t.B.Position.Z;
                                buffer[idx++] = (float)n.X; buffer[idx++] = (float)n.Y; buffer[idx++] = (float)n.Z;
                                buffer[idx++] = (float)t.C.Position.X; buffer[idx++] = (float)t.C.Position.Y; buffer[idx++] = (float)t.C.Position.Z;
                                buffer[idx++] = (float)n.X; buffer[idx++] = (float)n.Y; buffer[idx++] = (float)n.Z;
                            }
                            onProgressRaw(buffer, validCount * 3);
                        });
                    }
                }
            }
            Console.WriteLine();

            // 4. Final Cleanup
            var finalTriangles = new List<Triangle>();

            foreach (var t in triangles)
            {
                if (t.IsBad) continue;

                // Remove Super Triangle connections
                if (IsConnectedTo(t, s1) || IsConnectedTo(t, s2) || IsConnectedTo(t, s3))
                    continue;

                finalTriangles.Add(t);
            }

            Console.WriteLine($"[MESH] Generated {finalTriangles.Count} triangles.");
            return finalTriangles;
        }

        // --- Helpers ---

        private static bool IsConnectedTo(Triangle t, Vertex v)
        {
            return t.A == v || t.B == v || t.C == v;
        }

        private static Triangle? FindTriangleContainingPoint(Triangle start, Vector3 p)
        {
            Triangle curr = start;
            int safety = 0;
            while (safety++ < 5000)
            {
                if (curr.IsBad) return null;

                // Check edges
                if (IsCounterClockwise(curr.B.Position, curr.C.Position, p))
                {
                    if (curr.Neighbors[0] == null) return curr;
                    curr = curr.Neighbors[0]!;
                    continue;
                }
                if (IsCounterClockwise(curr.C.Position, curr.A.Position, p))
                {
                    if (curr.Neighbors[1] == null) return curr;
                    curr = curr.Neighbors[1]!;
                    continue;
                }
                if (IsCounterClockwise(curr.A.Position, curr.B.Position, p))
                {
                    if (curr.Neighbors[2] == null) return curr;
                    curr = curr.Neighbors[2]!;
                    continue;
                }

                return curr; // Inside
            }
            return null;
        }

        private static bool IsCounterClockwise(Vector3 a, Vector3 b, Vector3 p)
        {
            return (b.X - a.X) * (p.Z - a.Z) - (b.Z - a.Z) * (p.X - a.X) > 0;
        }

        private static bool IsPointInCircumcircle(Vector3 p, Triangle t)
        {
            double ax = t.A.Position.X, az = t.A.Position.Z;
            double bx = t.B.Position.X, bz = t.B.Position.Z;
            double cx = t.C.Position.X, cz = t.C.Position.Z;

            double D = 2 * (ax * (bz - cz) + bx * (cz - az) + cx * (az - bz));
            if (Math.Abs(D) < 1e-9) return false;

            double centerX = ((ax * ax + az * az) * (bz - cz) + (bx * bx + bz * bz) * (cz - az) + (cx * cx + cz * cz) * (az - bz)) / D;
            double centerZ = ((ax * ax + az * az) * (cx - bx) + (bx * bx + bz * bz) * (ax - cx) + (cx * cx + cz * cz) * (bx - ax)) / D;

            double rSq = (centerX - ax) * (centerX - ax) + (centerZ - az) * (centerZ - az);
            double dSq = (centerX - p.X) * (centerX - p.X) + (centerZ - p.Z) * (centerZ - p.Z);

            return dSq < rSq - 1e-10;
        }

        private static List<Vertex> Deduplicate(List<Vertex> input)
        {
            var seen = new HashSet<(long X, long Z)>();
            var output = new List<Vertex>(input.Count);
            double cellSize = Settings.MIN_MERGE_DISTANCE;

            foreach (var p in input)
            {
                long qx = (long)Math.Floor(p.Position.X / cellSize);
                long qz = (long)Math.Floor(p.Position.Z / cellSize);
                var key = (qx, qz);

                if (seen.Add(key))
                {
                    output.Add(p);
                }
            }
            return output;
        }

        private static Bounds GetBounds(List<Vertex> points)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (var p in points)
            {
                if (p.Position.X < minX) minX = p.Position.X;
                if (p.Position.X > maxX) maxX = p.Position.X;
                if (p.Position.Z < minZ) minZ = p.Position.Z;
                if (p.Position.Z > maxZ) maxZ = p.Position.Z;
            }
            return new Bounds { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ };
        }

        private static ulong GetMortonCode(double x, double z, Bounds bounds)
        {
            // Normalize to [0, 1]
            double nx = (x - bounds.MinX) / (bounds.Width + 1e-9);
            double nz = (z - bounds.MinZ) / (bounds.Depth + 1e-9);

            // Scale to 32-bit integer
            uint ix = (uint)(nx * 0xFFFFFFFF);
            uint iz = (uint)(nz * 0xFFFFFFFF);

            return InterleaveBits(ix, iz);
        }

        private static ulong InterleaveBits(uint x, uint y)
        {
            ulong xx = x;
            ulong yy = y;

            xx = (xx | (xx << 16)) & 0x0000FFFF0000FFFF;
            xx = (xx | (xx << 8)) & 0x00FF00FF00FF00FF;
            xx = (xx | (xx << 4)) & 0x0F0F0F0F0F0F0F0F;
            xx = (xx | (xx << 2)) & 0x3333333333333333;
            xx = (xx | (xx << 1)) & 0x5555555555555555;

            yy = (yy | (yy << 16)) & 0x0000FFFF0000FFFF;
            yy = (yy | (yy << 8)) & 0x00FF00FF00FF00FF;
            yy = (yy | (yy << 4)) & 0x0F0F0F0F0F0F0F0F;
            yy = (yy | (yy << 2)) & 0x3333333333333333;
            yy = (yy | (yy << 1)) & 0x5555555555555555;

            return xx | (yy << 1);
        }

        public static List<Triangle> FilterHighAspectRatioTriangles(List<Triangle> triangles, double maxAspectRatio, out int removedCount)
        {
            removedCount = 0;
            if (triangles.Count == 0) return triangles;

            var output = new List<Triangle>(triangles.Count);

            for (int i = 0; i < triangles.Count; i++)
            {
                var t = triangles[i];

                double ab = Distance(t.A.Position, t.B.Position);
                double bc = Distance(t.B.Position, t.C.Position);
                double ca = Distance(t.C.Position, t.A.Position);

                double longestEdge = Math.Max(ab, Math.Max(bc, ca));

                // Twice triangle area from 3D cross product magnitude.
                double e1x = t.B.Position.X - t.A.Position.X;
                double e1y = t.B.Position.Y - t.A.Position.Y;
                double e1z = t.B.Position.Z - t.A.Position.Z;
                double e2x = t.C.Position.X - t.A.Position.X;
                double e2y = t.C.Position.Y - t.A.Position.Y;
                double e2z = t.C.Position.Z - t.A.Position.Z;
                double cx = e1y * e2z - e1z * e2y;
                double cy = e1z * e2x - e1x * e2z;
                double cz = e1x * e2y - e1y * e2x;
                double twiceArea = Math.Sqrt(cx * cx + cy * cy + cz * cz);

                if (twiceArea <= 1e-12)
                {
                    removedCount++;
                    continue;
                }

                double hAB = twiceArea / Math.Max(ab, 1e-12);
                double hBC = twiceArea / Math.Max(bc, 1e-12);
                double hCA = twiceArea / Math.Max(ca, 1e-12);
                double minHeight = Math.Min(hAB, Math.Min(hBC, hCA));

                double aspectRatio = longestEdge / Math.Max(minHeight, 1e-12);

                if (aspectRatio <= maxAspectRatio)
                {
                    output.Add(t);
                }
                else
                {
                    removedCount++;
                }
            }

            return output;
        }

        public static List<Triangle> CullWeakBoundaryTriangles(
            List<Triangle> triangles,
            List<Vertex> supportPoints,
            double edgeSpacingMultiplier,
            double minNormalizedSupport,
            double heightTolMultiplier,
            double minHeightTol,
            out int removedCount)
        {
            removedCount = 0;
            if (triangles.Count == 0 || supportPoints.Count == 0)
            {
                return triangles;
            }

            // Build unique vertex index map (reference equality).
            var vertexIndex = new Dictionary<Vertex, int>(VertexRefComparer.Instance);
            int nextIndex = 0;
            for (int i = 0; i < triangles.Count; i++)
            {
                var t = triangles[i];
                if (!vertexIndex.ContainsKey(t.A)) vertexIndex[t.A] = nextIndex++;
                if (!vertexIndex.ContainsKey(t.B)) vertexIndex[t.B] = nextIndex++;
                if (!vertexIndex.ContainsKey(t.C)) vertexIndex[t.C] = nextIndex++;
            }

            // Build vertex local spacing cache from support points.
            var spacingByVertex = BuildLocalSpacingMap(vertexIndex.Keys.ToList(), supportPoints);

            // Spatial hash of support points for fast triangle support query.
            double avgSpacing = spacingByVertex.Count > 0 ? spacingByVertex.Values.Average() : 1.0;
            double cellSize = Math.Max(avgSpacing * 2.0, 0.01);
            var supportGrid = BuildPointGrid(supportPoints, cellSize);

            // Triangle-edge adjacency.
            var edgeToTriangles = new Dictionary<EdgeKey, List<int>>();
            for (int i = 0; i < triangles.Count; i++)
            {
                var t = triangles[i];
                AddEdge(edgeToTriangles, new EdgeKey(vertexIndex[t.A], vertexIndex[t.B]), i);
                AddEdge(edgeToTriangles, new EdgeKey(vertexIndex[t.B], vertexIndex[t.C]), i);
                AddEdge(edgeToTriangles, new EdgeKey(vertexIndex[t.C], vertexIndex[t.A]), i);
            }

            var alive = new bool[triangles.Count];
            Array.Fill(alive, true);

            var queue = new Queue<int>();
            for (int i = 0; i < triangles.Count; i++)
            {
                if (IsBoundaryTriangle(i, triangles, vertexIndex, edgeToTriangles, alive))
                {
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                int triIndex = queue.Dequeue();
                if (!alive[triIndex]) continue;

                if (!IsBoundaryTriangle(triIndex, triangles, vertexIndex, edgeToTriangles, alive))
                {
                    continue;
                }

                var t = triangles[triIndex];
                double localSpacing = (spacingByVertex[t.A] + spacingByVertex[t.B] + spacingByVertex[t.C]) / 3.0;
                localSpacing = Math.Max(localSpacing, 1e-4);

                double ab = Distance(t.A.Position, t.B.Position);
                double bc = Distance(t.B.Position, t.C.Position);
                double ca = Distance(t.C.Position, t.A.Position);
                double longestEdge = Math.Max(ab, Math.Max(bc, ca));

                bool edgeTooLong = longestEdge > edgeSpacingMultiplier * localSpacing;

                double areaXZ = Math.Abs(TriArea2D(t.A.Position, t.B.Position, t.C.Position));
                int supportCount = 0;
                double normalizedSupport = 0.0;
                bool weakSupport;
                bool veryWeakSupport;

                if (areaXZ <= 1e-9)
                {
                    weakSupport = true;
                    veryWeakSupport = true;
                }
                else
                {
                    supportCount = CountSupportingPoints(t, supportGrid, cellSize, Math.Max(minHeightTol, localSpacing * heightTolMultiplier));
                    normalizedSupport = supportCount * (localSpacing * localSpacing) / areaXZ;
                    weakSupport = normalizedSupport < minNormalizedSupport;
                    veryWeakSupport = supportCount == 0 || normalizedSupport < (minNormalizedSupport * 0.35);
                }

                // Extra-conservative boundary pruning:
                // only trim obvious unsupported skirts. Do NOT remove triangles that
                // still have any real point support, even if they are sparse.
                bool shouldCull = edgeTooLong && supportCount == 0 && veryWeakSupport;

                if (shouldCull)
                {
                    alive[triIndex] = false;
                    removedCount++;

                    // Re-check neighbors that just became boundary.
                    foreach (var n in GetAdjacentTriangles(triIndex, triangles, vertexIndex, edgeToTriangles))
                    {
                        if (alive[n]) queue.Enqueue(n);
                    }
                }
            }

            var filtered = new List<Triangle>(triangles.Count - removedCount);
            for (int i = 0; i < triangles.Count; i++)
            {
                if (alive[i]) filtered.Add(triangles[i]);
            }

            return filtered;
        }

        private static Dictionary<Vertex, double> BuildLocalSpacingMap(List<Vertex> uniqueVerts, List<Vertex> supportPoints)
        {
            double globalAvg = EstimateAverageSpacing(supportPoints);
            double cellSize = Math.Max(globalAvg * 2.0, 0.01);
            var grid = BuildPointGrid(supportPoints, cellSize);
            var spacing = new Dictionary<Vertex, double>(VertexRefComparer.Instance);

            for (int i = 0; i < uniqueVerts.Count; i++)
            {
                var v = uniqueVerts[i];
                var (cx, cz) = QuantizeXZ(v.Position.X, v.Position.Z, cellSize);
                double best = double.MaxValue;

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue((cx + dx, cz + dz), out var bucket)) continue;
                        for (int j = 0; j < bucket.Count; j++)
                        {
                            var p = bucket[j];
                            if (ReferenceEquals(p, v)) continue;
                            double d = Distance(v.Position, p.Position);
                            if (d > 1e-9 && d < best) best = d;
                        }
                    }
                }

                spacing[v] = best < double.MaxValue ? best : globalAvg;
            }

            return spacing;
        }

        private static double EstimateAverageSpacing(List<Vertex> points)
        {
            if (points.Count < 2) return 1.0;

            var bounds = GetBounds(points);
            double cellSize = Math.Max(Math.Max(bounds.Width, bounds.Depth) / 256.0, 0.01);
            var grid = BuildPointGrid(points, cellSize);

            int sampleCount = Math.Min(2000, points.Count);
            double sum = 0;
            int valid = 0;
            int stride = Math.Max(points.Count / sampleCount, 1);

            for (int i = 0; i < points.Count && valid < sampleCount; i += stride)
            {
                var v = points[i];
                var (cx, cz) = QuantizeXZ(v.Position.X, v.Position.Z, cellSize);
                double best = double.MaxValue;

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue((cx + dx, cz + dz), out var bucket)) continue;
                        for (int j = 0; j < bucket.Count; j++)
                        {
                            var p = bucket[j];
                            if (ReferenceEquals(p, v)) continue;
                            double d = Distance(v.Position, p.Position);
                            if (d > 1e-9 && d < best) best = d;
                        }
                    }
                }

                if (best < double.MaxValue)
                {
                    sum += best;
                    valid++;
                }
            }

            return valid > 0 ? sum / valid : 1.0;
        }

        private static Dictionary<(int X, int Z), List<Vertex>> BuildPointGrid(List<Vertex> points, double cellSize)
        {
            var grid = new Dictionary<(int X, int Z), List<Vertex>>();
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var key = QuantizeXZ(p.Position.X, p.Position.Z, cellSize);
                if (!grid.TryGetValue(key, out var bucket))
                {
                    bucket = new List<Vertex>();
                    grid[key] = bucket;
                }
                bucket.Add(p);
            }

            return grid;
        }

        private static int CountSupportingPoints(Triangle t, Dictionary<(int X, int Z), List<Vertex>> grid, double cellSize, double heightTol)
        {
            double minX = Math.Min(t.A.Position.X, Math.Min(t.B.Position.X, t.C.Position.X));
            double maxX = Math.Max(t.A.Position.X, Math.Max(t.B.Position.X, t.C.Position.X));
            double minZ = Math.Min(t.A.Position.Z, Math.Min(t.B.Position.Z, t.C.Position.Z));
            double maxZ = Math.Max(t.A.Position.Z, Math.Max(t.B.Position.Z, t.C.Position.Z));

            var minCell = QuantizeXZ(minX, minZ, cellSize);
            var maxCell = QuantizeXZ(maxX, maxZ, cellSize);

            int count = 0;
            for (int cx = minCell.X; cx <= maxCell.X; cx++)
            {
                for (int cz = minCell.Z; cz <= maxCell.Z; cz++)
                {
                    if (!grid.TryGetValue((cx, cz), out var bucket)) continue;

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var p = bucket[i].Position;
                        if (!PointInTriangleXZ(p.X, p.Z, t.A.Position, t.B.Position, t.C.Position, out double w0, out double w1, out double w2))
                        {
                            continue;
                        }

                        double triY = w0 * t.A.Position.Y + w1 * t.B.Position.Y + w2 * t.C.Position.Y;
                        if (Math.Abs(p.Y - triY) <= heightTol)
                        {
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        private static bool PointInTriangleXZ(double px, double pz, Vector3 a, Vector3 b, Vector3 c, out double w0, out double w1, out double w2)
        {
            double v0x = b.X - a.X; double v0z = b.Z - a.Z;
            double v1x = c.X - a.X; double v1z = c.Z - a.Z;
            double v2x = px - a.X;  double v2z = pz - a.Z;

            double den = v0x * v1z - v1x * v0z;
            if (Math.Abs(den) < 1e-12)
            {
                w0 = w1 = w2 = 0;
                return false;
            }

            double inv = 1.0 / den;
            w1 = (v2x * v1z - v1x * v2z) * inv;
            w2 = (v0x * v2z - v2x * v0z) * inv;
            w0 = 1.0 - w1 - w2;

            const double eps = -1e-6;
            return w0 >= eps && w1 >= eps && w2 >= eps;
        }

        private static double TriArea2D(Vector3 a, Vector3 b, Vector3 c)
        {
            return Math.Abs((b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X)) * 0.5;
        }

        private static (int X, int Z) QuantizeXZ(double x, double z, double cellSize)
        {
            return ((int)Math.Floor(x / cellSize), (int)Math.Floor(z / cellSize));
        }

        private static bool IsBoundaryTriangle(int triIndex, List<Triangle> triangles, Dictionary<Vertex, int> vertexIndex, Dictionary<EdgeKey, List<int>> edgeToTriangles, bool[] alive)
        {
            var t = triangles[triIndex];
            var e0 = new EdgeKey(vertexIndex[t.A], vertexIndex[t.B]);
            var e1 = new EdgeKey(vertexIndex[t.B], vertexIndex[t.C]);
            var e2 = new EdgeKey(vertexIndex[t.C], vertexIndex[t.A]);

            return AliveIncidentCount(edgeToTriangles[e0], alive) == 1 ||
                   AliveIncidentCount(edgeToTriangles[e1], alive) == 1 ||
                   AliveIncidentCount(edgeToTriangles[e2], alive) == 1;
        }

        private static IEnumerable<int> GetAdjacentTriangles(int triIndex, List<Triangle> triangles, Dictionary<Vertex, int> vertexIndex, Dictionary<EdgeKey, List<int>> edgeToTriangles)
        {
            var t = triangles[triIndex];
            var edges = new[]
            {
                new EdgeKey(vertexIndex[t.A], vertexIndex[t.B]),
                new EdgeKey(vertexIndex[t.B], vertexIndex[t.C]),
                new EdgeKey(vertexIndex[t.C], vertexIndex[t.A])
            };

            var outSet = new HashSet<int>();
            for (int i = 0; i < edges.Length; i++)
            {
                var list = edgeToTriangles[edges[i]];
                for (int j = 0; j < list.Count; j++)
                {
                    int n = list[j];
                    if (n != triIndex) outSet.Add(n);
                }
            }

            return outSet;
        }

        private static int AliveIncidentCount(List<int> tris, bool[] alive)
        {
            int count = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                if (alive[tris[i]]) count++;
            }
            return count;
        }

        private static void AddEdge(Dictionary<EdgeKey, List<int>> map, EdgeKey key, int triIndex)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                map[key] = list;
            }
            list.Add(triIndex);
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly int A;
            public readonly int B;

            public EdgeKey(int a, int b)
            {
                if (a < b)
                {
                    A = a;
                    B = b;
                }
                else
                {
                    A = b;
                    B = a;
                }
            }

            public bool Equals(EdgeKey other) => A == other.A && B == other.B;
            public override bool Equals(object? obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(A, B);
        }

        private sealed class VertexRefComparer : IEqualityComparer<Vertex>
        {
            public static readonly VertexRefComparer Instance = new VertexRefComparer();
            public bool Equals(Vertex? x, Vertex? y) => ReferenceEquals(x, y);
            public int GetHashCode(Vertex obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private static double Distance(Vector3 a, Vector3 b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
