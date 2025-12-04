using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace TerrainTool
{
    class Program
    {
        // --- CONFIGURATION ---

        // Merge distance for database accumulation (0.01 = 1cm precision)
        const double MIN_MERGE_DISTANCE = 0.01;

        // Quadtree Generation Settings
        // Subdivide chunks until they contain fewer than this many points
        const int TARGET_POINTS_PER_CHUNK = 1000;

        // Minimum physical size of a chunk to prevent infinite recursion on stacked points
        const double MIN_CHUNK_SIZE = 2.0;

        // Gap Fix: How much to expand bounds to find physical neighbors (Meters)
        const double NEIGHBOR_TOUCH_EPSILON = 0.5;

        // --- DATA STRUCTURES ---

        public class Vertex
        {
            public int ID;
            public double X, Y, Z;
            public double NX, NY, NZ;
        }

        public struct Ray
        {
            public double SX, SY, SZ; // Start Position
            public double EX, EY, EZ; // End Position

            public void GetDir(out double dx, out double dy, out double dz, out double len)
            {
                dx = EX - SX;
                dy = EY - SY;
                dz = EZ - SZ;
                len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }

        public class Triangle
        {
            public Vertex A, B, C;
            public Vertex Centroid;
            public bool IsDeleted = false; // Flag for Carving

            // Bounding Box for fast rejection during carving
            public double MinX, MaxX, MinY, MaxY, MinZ, MaxZ;

            public Triangle(Vertex a, Vertex b, Vertex c)
            {
                A = a;
                B = b;
                C = c;

                Centroid = new Vertex
                {
                    X = (a.X + b.X + c.X) / 3.0,
                    Y = (a.Y + b.Y + c.Y) / 3.0,
                    Z = (a.Z + b.Z + c.Z) / 3.0
                };

                // Pre-calc bounds for physics speedup
                MinX = Math.Min(a.X, Math.Min(b.X, c.X));
                MaxX = Math.Max(a.X, Math.Max(b.X, c.X));
                MinY = Math.Min(a.Y, Math.Min(b.Y, c.Y));
                MaxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
                MinZ = Math.Min(a.Z, Math.Min(b.Z, c.Z));
                MaxZ = Math.Max(a.Z, Math.Max(b.Z, c.Z));
            }
        }

        public class Edge
        {
            public Vertex U, V;

            public Edge(Vertex u, Vertex v)
            {
                U = u;
                V = v;
            }

            public bool EqualsEdge(Edge other)
            {
                return (U == other.U && V == other.V) || (U == other.V && V == other.U);
            }
        }

        // --- QUADTREE STRUCTURES ---

        public struct Bounds
        {
            public double MinX, MinZ, MaxX, MaxZ;

            public double MidX => (MinX + MaxX) / 2.0;
            public double MidZ => (MinZ + MaxZ) / 2.0;
            public double Width => MaxX - MinX;
            public double Depth => MaxZ - MinZ;

            public bool Contains(Vertex p)
            {
                return p.X >= MinX && p.X <= MaxX && p.Z >= MinZ && p.Z <= MaxZ;
            }

            public bool Intersects(Bounds other)
            {
                return !(other.MinX > MaxX || other.MaxX < MinX || other.MinZ > MaxZ || other.MaxZ < MinZ);
            }

            public Bounds Expand(double amount)
            {
                return new Bounds
                {
                    MinX = MinX - amount,
                    MaxX = MaxX + amount,
                    MinZ = MinZ - amount,
                    MaxZ = MaxZ + amount
                };
            }
        }

        public class QuadNode
        {
            public Bounds Area;
            public List<Vertex> Points;
            public QuadNode[] Children;

            public QuadNode(Bounds bounds)
            {
                Area = bounds;
                Points = new List<Vertex>();
            }

            public bool IsLeaf => Children == null;
        }

        // --- MAIN PROGRAM ---

        static void Main(string[] args)
        {
            // Database Recovery Tool Mode
            if (args.Length >= 4 && args[0].ToLower() == "merge")
            {
                RunMergeTool(args[1], args[2], args[3]);
                return;
            }

            // Path Detection Logic
            string tempDir = Path.GetTempPath();
            string logPath = Path.Combine(tempDir, "Battlefieldâ„¢ 6", "PortalLog.txt");

            if (!File.Exists(logPath))
            {
                string altPath = Path.Combine(tempDir, "Battlefield™ 6", "PortalLog.txt");
                if (File.Exists(altPath))
                {
                    logPath = altPath;
                }
            }

            string dbPath = "terrain_master.db";
            string missDbPath = "terrain_misses.db";
            string objPath = "terrain_final.obj";

            Console.WriteLine("=== BATTLEFIELD TERRAIN SOLVER (Full Suite) ===");

            // 1. Load Existing Data
            List<Vertex> masterPoints = LoadDatabase(dbPath);
            List<Ray> masterMisses = LoadMissDatabase(missDbPath);

            Console.WriteLine($"[DB] Loaded Points: {masterPoints.Count}");
            Console.WriteLine($"[DB] Loaded Miss Rays: {masterMisses.Count}");

            // 2. Process New Log Data
            if (File.Exists(logPath))
            {
                List<Vertex> newPoints;
                List<Ray> newMisses;

                ParseLog(logPath, out newPoints, out newMisses);

                Console.WriteLine($"[LOG] Found New Points: {newPoints.Count}");
                Console.WriteLine($"[LOG] Found New Misses: {newMisses.Count}");

                if (newPoints.Count > 0 || newMisses.Count > 0)
                {
                    int addedPoints = MergePoints(masterPoints, newPoints, MIN_MERGE_DISTANCE);
                    masterMisses.AddRange(newMisses);

                    Console.WriteLine($"[MERGE] Integrated {addedPoints} unique points.");

                    SaveDatabase(masterPoints, dbPath);
                    SaveMissDatabase(masterMisses, missDbPath);

                    try
                    {
                        File.WriteAllText(logPath, string.Empty);
                        Console.WriteLine("[LOG] Log file cleared successfully.");
                    }
                    catch
                    {
                        Console.WriteLine("[WARN] Could not clear log file.");
                    }
                }
            }

            if (masterPoints.Count < 3)
            {
                Console.WriteLine("[STOP] Not enough points to generate a mesh.");
                return;
            }

            Stopwatch sw = Stopwatch.StartNew();

            // 3. Constructive Geometry (Quadtree Meshing)
            Console.WriteLine("[MESH] Building Adaptive Quadtree...");
            var allTriangles = GenerateMeshQuadtree(masterPoints);
            
            // 4. Destructive Geometry (Space Carving)
            if (masterMisses.Count > 0)
            {
                Console.WriteLine($"[CARVE] Raycasting {masterMisses.Count} miss rays against the mesh...");
                int removed = CarveMesh(allTriangles, masterMisses);
                Console.WriteLine($"[CARVE] Pruned {removed} triangles intersecting empty space.");

                // Remove the deleted triangles from the list
                allTriangles = allTriangles.Where(t => !t.IsDeleted).ToList();
            }
            
            Console.WriteLine($"[MESH] Final Triangle Count: {allTriangles.Count}");

            sw.Stop();
            Console.WriteLine($"[DONE] Total Processing Time: {sw.Elapsed.TotalSeconds:F2}s");

            // 5. Export Result
            ExportObj(masterPoints, allTriangles, objPath);
        }

        // --- QUADTREE MESHING ALGORITHM ---

        static List<Triangle> GenerateMeshQuadtree(List<Vertex> allPoints)
        {
            // 1. Calculate World Bounds
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minZ = double.MaxValue;
            double maxZ = double.MinValue;

            foreach (var p in allPoints)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
            }

            // --- LOGGING RESTORED ---
            Console.WriteLine($"[DATA] World Bounds: X[{minX:F0} : {maxX:F0}] Z[{minZ:F0} : {maxZ:F0}]");
            Console.WriteLine($"[DATA] Dimensions:   Width: {(maxX - minX):F1}m | Depth: {(maxZ - minZ):F1}m");
            // ------------------------

            // Create Root Node
            Bounds rootBounds = new Bounds
            {
                MinX = minX - 1,
                MaxX = maxX + 1,
                MinZ = minZ - 1,
                MaxZ = maxZ + 1
            };

            QuadNode root = new QuadNode(rootBounds);

            // 2. Populate Tree
            foreach (var p in allPoints)
            {
                InsertQuad(root, p);
            }

            // 3. Collect Leaves
            List<QuadNode> leaves = new List<QuadNode>();
            CollectLeaves(root, leaves);

            Console.WriteLine($"[TREE] Created {leaves.Count} adaptive chunks. Processing on {Environment.ProcessorCount} threads...");

            ConcurrentBag<Triangle> finalTriangles = new ConcurrentBag<Triangle>();

            // 4. Parallel Processing
            Parallel.ForEach(leaves, leaf =>
            {
                // NEIGHBOR STRATEGY (Gap Fix):
                // Expand bounds by epsilon to touch direct neighbors.
                Bounds neighborQuery = leaf.Area.Expand(NEIGHBOR_TOUCH_EPSILON);

                List<QuadNode> neighbors = new List<QuadNode>();
                GetIntersectingLeaves(root, neighborQuery, neighbors);

                // Collect all points from self + adjacent neighbors
                List<Vertex> localPoints = new List<Vertex>();
                foreach (var node in neighbors)
                {
                    localPoints.AddRange(node.Points);
                }

                // Triangulate
                if (localPoints.Count > 2)
                {
                    var localTris = BowyerWatson(localPoints);

                    // Centroid Filter (Stitching Logic)
                    // Only keep triangles if their center belongs to THIS leaf.
                    foreach (var t in localTris)
                    {
                        if (leaf.Area.Contains(t.Centroid))
                        {
                            finalTriangles.Add(t);
                        }
                    }
                }
            });

            return finalTriangles.ToList();
        }

        // --- SPACE CARVING ALGORITHM ---

        static int CarveMesh(List<Triangle> triangles, List<Ray> rays)
        {
            int deletedCount = 0;
            object lockObj = new object();

            // Process Rays in parallel
            Parallel.ForEach(rays, ray =>
            {
                double rDx, rDy, rDz, rLen;
                ray.GetDir(out rDx, out rDy, out rDz, out rLen);

                // Normalize Ray Direction
                double invLen = 1.0 / rLen;
                double nDx = rDx * invLen;
                double nDy = rDy * invLen;
                double nDz = rDz * invLen;

                // Calculate Ray Bounds for AABB check
                double rMinX = Math.Min(ray.SX, ray.EX);
                double rMaxX = Math.Max(ray.SX, ray.EX);
                double rMinY = Math.Min(ray.SY, ray.EY);
                double rMaxY = Math.Max(ray.SY, ray.EY);
                double rMinZ = Math.Min(ray.SZ, ray.EZ);
                double rMaxZ = Math.Max(ray.SZ, ray.EZ);

                foreach (var tri in triangles)
                {
                    if (tri.IsDeleted) continue;

                    // 1. FAST AABB CHECK
                    if (tri.MaxX < rMinX || tri.MinX > rMaxX ||
                        tri.MaxY < rMinY || tri.MinY > rMaxY ||
                        tri.MaxZ < rMinZ || tri.MinZ > rMaxZ)
                    {
                        continue;
                    }

                    // 2. Möller–Trumbore Intersection Algorithm
                    double edge1X = tri.B.X - tri.A.X;
                    double edge1Y = tri.B.Y - tri.A.Y;
                    double edge1Z = tri.B.Z - tri.A.Z;

                    double edge2X = tri.C.X - tri.A.X;
                    double edge2Y = tri.C.Y - tri.A.Y;
                    double edge2Z = tri.C.Z - tri.A.Z;

                    double hX = nDy * edge2Z - nDz * edge2Y;
                    double hY = nDz * edge2X - nDx * edge2Z;
                    double hZ = nDx * edge2Y - nDy * edge2X;

                    double a = edge1X * hX + edge1Y * hY + edge1Z * hZ;

                    if (a > -1e-7 && a < 1e-7) continue; // Parallel

                    double f = 1.0 / a;
                    double sX = ray.SX - tri.A.X;
                    double sY = ray.SY - tri.A.Y;
                    double sZ = ray.SZ - tri.A.Z;

                    double u = f * (sX * hX + sY * hY + sZ * hZ);

                    if (u < 0.0 || u > 1.0) continue;

                    double qX = sY * edge1Z - sZ * edge1Y;
                    double qY = sZ * edge1X - sX * edge1Z;
                    double qZ = sX * edge1Y - sY * edge1X;

                    double v = f * (nDx * qX + nDy * qY + nDz * qZ);

                    if (v < 0.0 || u + v > 1.0) continue;

                    double t = f * (edge2X * qX + edge2Y * qY + edge2Z * qZ);

                    // Strict intersection logic
                    // t is distance along the ray. We check if intersection is inside the segment.
                    // We add a buffer (0.05) to avoid deleting geometry that the ray starts/ends on perfectly.
                    if (t > 0.05 && t < rLen - 0.05)
                    {
                        lock (lockObj)
                        {
                            if (!tri.IsDeleted)
                            {
                                tri.IsDeleted = true;
                                deletedCount++;
                            }
                        }
                    }
                }
            });

            return deletedCount;
        }

        // --- QUADTREE HELPERS ---

        static void InsertQuad(QuadNode node, Vertex p)
        {
            if (!node.Area.Contains(p)) return;

            if (node.IsLeaf)
            {
                // STOP CONDITION: Target Points OR Physical Size Limit
                if (node.Points.Count < TARGET_POINTS_PER_CHUNK || node.Area.Width < MIN_CHUNK_SIZE)
                {
                    node.Points.Add(p);
                }
                else
                {
                    Subdivide(node);
                    foreach (var oldP in node.Points)
                    {
                        PushToChildren(node, oldP);
                    }
                    node.Points.Clear();
                    PushToChildren(node, p);
                }
            }
            else
            {
                PushToChildren(node, p);
            }
        }

        static void PushToChildren(QuadNode node, Vertex p)
        {
            foreach (var child in node.Children)
            {
                InsertQuad(child, p);
            }
        }

        static void Subdivide(QuadNode node)
        {
            double mx = node.Area.MidX;
            double mz = node.Area.MidZ;

            node.Children = new QuadNode[4];
            
            // Create 4 children
            node.Children[0] = new QuadNode(new Bounds { MinX = node.Area.MinX, MaxX = mx, MinZ = node.Area.MinZ, MaxZ = mz }); // BL
            node.Children[1] = new QuadNode(new Bounds { MinX = mx, MaxX = node.Area.MaxX, MinZ = node.Area.MinZ, MaxZ = mz }); // BR
            node.Children[2] = new QuadNode(new Bounds { MinX = node.Area.MinX, MaxX = mx, MinZ = mz, MaxZ = node.Area.MaxZ }); // TL
            node.Children[3] = new QuadNode(new Bounds { MinX = mx, MaxX = node.Area.MaxX, MinZ = mz, MaxZ = node.Area.MaxZ }); // TR
        }

        static void CollectLeaves(QuadNode node, List<QuadNode> leaves)
        {
            if (node.IsLeaf)
            {
                // Only process chunks that actually contain data
                if (node.Points.Count > 0)
                {
                    leaves.Add(node);
                }
            }
            else
            {
                foreach (var child in node.Children)
                {
                    CollectLeaves(child, leaves);
                }
            }
        }

        static void GetIntersectingLeaves(QuadNode node, Bounds query, List<QuadNode> results)
        {
            if (!node.Area.Intersects(query)) return;

            if (node.IsLeaf)
            {
                if (node.Points.Count > 0)
                {
                    results.Add(node);
                }
            }
            else
            {
                foreach (var child in node.Children)
                {
                    GetIntersectingLeaves(child, query, results);
                }
            }
        }

        // --- BOWYER-WATSON (Triangulation Logic) ---

        static List<Triangle> BowyerWatson(List<Vertex> pointList)
        {
            // Local sort speeds up the algo
            pointList.Sort((a, b) => a.X.CompareTo(b.X));

            var triangulation = new List<Triangle>();

            // Calculate bounds for super triangle
            double minX = double.MaxValue, maxX = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;

            foreach (var p in pointList)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
            }

            double dx = maxX - minX;
            double dz = maxZ - minZ;
            double deltaMax = Math.Max(dx, dz);
            
            if (deltaMax == 0) deltaMax = 1;

            double midX = (minX + maxX) / 2.0;
            double midZ = (minZ + maxZ) / 2.0;

            Vertex p1 = new Vertex { X = midX - 20 * deltaMax, Z = midZ - deltaMax };
            Vertex p2 = new Vertex { X = midX, Z = midZ + 20 * deltaMax };
            Vertex p3 = new Vertex { X = midX + 20 * deltaMax, Z = midZ - deltaMax };

            triangulation.Add(new Triangle(p1, p2, p3));

            foreach (var point in pointList)
            {
                var badTriangles = new List<Triangle>();

                for (int i = triangulation.Count - 1; i >= 0; i--)
                {
                    if (IsPointInCircumcircle(point, triangulation[i]))
                    {
                        badTriangles.Add(triangulation[i]);
                    }
                }

                var polygon = new List<Edge>();

                foreach (var t in badTriangles)
                {
                    AddEdge(polygon, new Edge(t.A, t.B));
                    AddEdge(polygon, new Edge(t.B, t.C));
                    AddEdge(polygon, new Edge(t.C, t.A));
                }

                foreach (var t in badTriangles)
                {
                    triangulation.Remove(t);
                }

                foreach (var edge in polygon)
                {
                    triangulation.Add(new Triangle(edge.U, edge.V, point));
                }
            }

            triangulation.RemoveAll(t =>
                t.A == p1 || t.A == p2 || t.A == p3 ||
                t.B == p1 || t.B == p2 || t.B == p3 ||
                t.C == p1 || t.C == p2 || t.C == p3
            );

            return triangulation;
        }

        static void AddEdge(List<Edge> edges, Edge e)
        {
            var match = edges.FirstOrDefault(x => x.EqualsEdge(e));
            if (match != null)
            {
                edges.Remove(match);
            }
            else
            {
                edges.Add(e);
            }
        }

        static bool IsPointInCircumcircle(Vertex p, Triangle t)
        {
            double ax = t.A.X; double az = t.A.Z;
            double bx = t.B.X; double bz = t.B.Z;
            double cx = t.C.X; double cz = t.C.Z;

            double D = 2 * (ax * (bz - cz) + bx * (cz - az) + cx * (az - bz));

            if (Math.Abs(D) < 1e-9) return false;

            double centerX = ((ax * ax + az * az) * (bz - cz) + (bx * bx + bz * bz) * (cz - az) + (cx * cx + cz * cz) * (az - bz)) / D;
            double centerZ = ((ax * ax + az * az) * (cx - bx) + (bx * bx + bz * bz) * (ax - cx) + (cx * cx + cz * cz) * (bx - ax)) / D;

            double rSq = (centerX - ax) * (centerX - ax) + (centerZ - az) * (centerZ - az);
            double dSq = (centerX - p.X) * (centerX - p.X) + (centerZ - p.Z) * (centerZ - p.Z);

            return dSq < rSq;
        }

        // --- MERGE & IO ---

        static int MergePoints(List<Vertex> master, List<Vertex> candidates, double minDistance)
        {
            Dictionary<long, List<Vertex>> grid = new Dictionary<long, List<Vertex>>();
            double cellSize = minDistance * 4;
            double minSq = minDistance * minDistance;
            int addedCount = 0;

            long GetHash(double x, double z)
            {
                int gx = (int)Math.Floor(x / cellSize);
                int gz = (int)Math.Floor(z / cellSize);
                return ((long)gx * 73856093) ^ ((long)gz * 19349663);
            }

            // Index master points
            foreach (var p in master)
            {
                long h = GetHash(p.X, p.Z);
                if (!grid.ContainsKey(h))
                {
                    grid[h] = new List<Vertex>();
                }
                grid[h].Add(p);
            }

            // Check candidates
            foreach (var p in candidates)
            {
                long h = GetHash(p.X, p.Z);
                bool tooClose = false;

                if (grid.ContainsKey(h))
                {
                    foreach (var existing in grid[h])
                    {
                        double distSq = ((p.X - existing.X) * (p.X - existing.X)) + ((p.Z - existing.Z) * (p.Z - existing.Z));
                        if (distSq < minSq)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                }

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

        static void RunMergeTool(string pathA, string pathB, string pathOut)
        {
            var setA = LoadDatabase(pathA);
            var setB = LoadDatabase(pathB);
            Console.WriteLine($"[MERGE] Merging {setB.Count} into {setA.Count}...");
            int added = MergePoints(setA, setB, MIN_MERGE_DISTANCE);
            SaveDatabase(setA, pathOut);
            Console.WriteLine($"[MERGE] Integrated {added} points. Total: {setA.Count}. Saved: {pathOut}");
        }

        static void ParseLog(string path, out List<Vertex> points, out List<Ray> misses)
        {
            points = new List<Vertex>();
            misses = new List<Ray>();

            Regex rHit = new Regex(@"HIT\|P:\s*([-+]?\d*\.?\d+),([-+]?\d*\.?\d+),([-+]?\d*\.?\d+)\|N:\s*([-+]?\d*\.?\d+),([-+]?\d*\.?\d+),([-+]?\d*\.?\d+)", RegexOptions.Compiled);
            Regex rMiss = new Regex(@"MISS\|S:\s*([-+]?\d*\.?\d+),([-+]?\d*\.?\d+),([-+]?\d*\.?\d+)\|E:\s*([-+]?\d*\.?\d+),([-+]?\d*\.?\d+),([-+]?\d*\.?\d+)", RegexOptions.Compiled);

            using (StreamReader sr = new StreamReader(path, Encoding.UTF8))
            {
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    Match m = rHit.Match(line);
                    if (m.Success)
                    {
                        points.Add(new Vertex
                        {
                            X = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                            Y = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                            Z = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                            NX = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
                            NY = double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture),
                            NZ = double.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture)
                        });
                        continue;
                    }

                    m = rMiss.Match(line);
                    if (m.Success)
                    {
                        misses.Add(new Ray
                        {
                            SX = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                            SY = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                            SZ = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                            EX = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
                            EY = double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture),
                            EZ = double.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture)
                        });
                    }
                }
            }
        }

        static void SaveDatabase(List<Vertex> points, string path)
        {
            using (BinaryWriter bw = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                bw.Write(points.Count);
                foreach (var p in points)
                {
                    bw.Write(p.X);
                    bw.Write(p.Y);
                    bw.Write(p.Z);
                    bw.Write(p.NX);
                    bw.Write(p.NY);
                    bw.Write(p.NZ);
                }
            }
        }

        static List<Vertex> LoadDatabase(string path)
        {
            var list = new List<Vertex>();
            if (!File.Exists(path)) return list;

            using (BinaryReader br = new BinaryReader(File.Open(path, FileMode.Open)))
            {
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    list.Add(new Vertex
                    {
                        X = br.ReadDouble(),
                        Y = br.ReadDouble(),
                        Z = br.ReadDouble(),
                        NX = br.ReadDouble(),
                        NY = br.ReadDouble(),
                        NZ = br.ReadDouble()
                    });
                }
            }
            return list;
        }

        static void SaveMissDatabase(List<Ray> rays, string path)
        {
            using (BinaryWriter bw = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                bw.Write(rays.Count);
                foreach (var r in rays)
                {
                    bw.Write(r.SX);
                    bw.Write(r.SY);
                    bw.Write(r.SZ);
                    bw.Write(r.EX);
                    bw.Write(r.EY);
                    bw.Write(r.EZ);
                }
            }
        }

        static List<Ray> LoadMissDatabase(string path)
        {
            var list = new List<Ray>();
            if (!File.Exists(path)) return list;

            using (BinaryReader br = new BinaryReader(File.Open(path, FileMode.Open)))
            {
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    list.Add(new Ray
                    {
                        SX = br.ReadDouble(),
                        SY = br.ReadDouble(),
                        SZ = br.ReadDouble(),
                        EX = br.ReadDouble(),
                        EY = br.ReadDouble(),
                        EZ = br.ReadDouble()
                    });
                }
            }
            return list;
        }

        static void ExportObj(List<Vertex> vertices, List<Triangle> triangles, string path)
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i].ID = i + 1;
            }

            using (StreamWriter sw = new StreamWriter(path))
            {
                sw.WriteLine($"# Vertices: {vertices.Count}");
                foreach (var v in vertices)
                {
                    sw.WriteLine(string.Format(CultureInfo.InvariantCulture, "v {0:F4} {1:F4} {2:F4}", v.X, v.Y, v.Z));
                    sw.WriteLine(string.Format(CultureInfo.InvariantCulture, "vn {0:F4} {1:F4} {2:F4}", v.NX, v.NY, v.NZ));
                }
                foreach (var t in triangles)
                {
                    sw.WriteLine($"f {t.A.ID}//{t.A.ID} {t.B.ID}//{t.B.ID} {t.C.ID}//{t.C.ID}");
                }
            }
            Console.WriteLine($"[EXPORT] Saved {Path.GetFullPath(path)}");
        }
    }
}