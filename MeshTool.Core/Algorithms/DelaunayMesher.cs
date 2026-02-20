using System;
using System.Collections.Generic;
using System.Linq;
using TerrainTool.Data;
using TerrainTool.Config;

namespace TerrainTool.Algorithms
{
    public static class DelaunayMesher
    {
        public static List<Triangle> GenerateMesh(List<Vertex> inputPoints)
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
            // Sort points by X to optimize the "Walk" location strategy
            points.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));

            // Optimization: Keep track of the last triangle to start the search
            Triangle lastTri = superTri;

            int count = 0;
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
            var dict = new Dictionary<long, Vertex>();
            var output = new List<Vertex>(input.Count);
            double cellSize = 0.01; // 1cm

            foreach (var p in input)
            {
                long key = ((long)(p.Position.X / cellSize) * 73856093) ^
                           ((long)(p.Position.Z / cellSize) * 19349663);

                if (!dict.ContainsKey(key))
                {
                    dict[key] = p;
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
    }
}
