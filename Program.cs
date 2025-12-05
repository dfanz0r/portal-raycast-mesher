using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TerrainTool.Algorithms;
using TerrainTool.Config;
using TerrainTool.Data;
using TerrainTool.IO;

namespace TerrainTool
{
    class Program
    {
        static void Main(string[] args)
        {
            var cmd = CommandLineArgs.Parse(args);

            // Database Recovery Tool Mode
            if (cmd.Command == "merge")
            {
                if (cmd.PositionalArgs.Count >= 3)
                {
                    RunMergeTool(cmd.PositionalArgs[0], cmd.PositionalArgs[1], cmd.PositionalArgs[2]);
                }
                else
                {
                    Console.WriteLine("Usage: merge <pathA> <pathB> <pathOut>");
                }
                return;
            }

            // Path Detection Logic
            string tempDir = Path.GetTempPath();
            string defaultLogPath = Path.Combine(tempDir, "Battlefieldâ„¢ 6", "PortalLog.txt");

            string logPath = cmd.GetOption("log", defaultLogPath);
            string dbPath = cmd.GetOption("db", "terrain.db");
            string objPath = cmd.GetOption("out", "terrain_final.glb");

            Console.WriteLine("=== BATTLEFIELD RAYCAST MESHER ===");

            // 1. Load Data
            List<Vertex> masterPoints;
            List<Ray> masterMisses;

            if (File.Exists(dbPath))
            {
                try
                {
                    DatabaseIO.LoadDatabase(dbPath, out masterPoints, out masterMisses);
                    Console.WriteLine($"[DB] Loaded: {masterPoints.Count} points, {masterMisses.Count} rays from {dbPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB] Error loading DB: {ex.Message}. Starting fresh.");
                    masterPoints = new List<Vertex>();
                    masterMisses = new List<Ray>();
                }
            }
            else
            {
                masterPoints = new List<Vertex>();
                masterMisses = new List<Ray>();
                Console.WriteLine("[DB] No existing database found. Starting fresh.");
            }

            // 2. Process New Log Data
            if (File.Exists(logPath))
            {
                List<Vertex> newPoints;
                List<Ray> newMisses;

                LogParser.ParseLog(logPath, out newPoints, out newMisses);

                Console.WriteLine($"[LOG] Found New Points: {newPoints.Count}");
                Console.WriteLine($"[LOG] Found New Misses: {newMisses.Count}");

                if (newPoints.Count > 0 || newMisses.Count > 0)
                {
                    int addedPoints = PointMerger.MergePoints(masterPoints, newPoints, Settings.MIN_MERGE_DISTANCE);
                    masterMisses.AddRange(newMisses);

                    Console.WriteLine($"[MERGE] Integrated {addedPoints} unique points.");

                    DatabaseIO.SaveDatabase(masterPoints, masterMisses, dbPath);

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

            // 3. Constructive Geometry (Delaunay Meshing)
            Console.WriteLine("[MESH] Building Adaptive Mesh...");
            var allTriangles = DelaunayMesher.GenerateMesh(masterPoints);

            // 4. Destructive Geometry (Space Carving)
            if (masterMisses.Count > 0)
            {
                Console.WriteLine("[MESH] Building Triangle Quadtree for acceleration...");

                // Calculate bounds
                double minX = double.MaxValue, maxX = double.MinValue;
                double minZ = double.MaxValue, maxZ = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;

                foreach (var p in masterPoints)
                {
                    if (p.Position.X < minX) minX = p.Position.X;
                    if (p.Position.X > maxX) maxX = p.Position.X;
                    if (p.Position.Z < minZ) minZ = p.Position.Z;
                    if (p.Position.Z > maxZ) maxZ = p.Position.Z;
                    if (p.Position.Y < minY) minY = p.Position.Y;
                    if (p.Position.Y > maxY) maxY = p.Position.Y;
                }
                Bounds meshBounds = new Bounds { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ, MinY = minY, MaxY = maxY };

                var quadtree = TriangleQuadtree.Build(allTriangles, meshBounds);

                Console.WriteLine($"[CARVE] Raycasting {masterMisses.Count} miss rays against the mesh...");
                int removed = SpaceCarver.CarveMesh(quadtree, masterMisses);
                Console.WriteLine($"[CARVE] Pruned {removed} triangles intersecting empty space.");

                // Remove the deleted triangles from the list
                allTriangles = allTriangles.Where(t => !t.IsDeleted).ToList();
            }

            Console.WriteLine($"[MESH] Final Triangle Count: {allTriangles.Count}");

            sw.Stop();
            Console.WriteLine($"[DONE] Total Processing Time: {sw.Elapsed.TotalSeconds:F2}s");

            // 5. Export Result
            if (objPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            {
                GlbExporter.ExportGlb(allTriangles, objPath);
            }
            else
            {
                ObjExporter.ExportObj(masterPoints, allTriangles, objPath);
            }
        }

        static void RunMergeTool(string pathA, string pathB, string pathOut)
        {
            List<Vertex> pointsA, pointsB;
            List<Ray> raysA, raysB;

            try
            {
                DatabaseIO.LoadDatabase(pathA, out pointsA, out raysA);
            }
            catch
            {
                Console.WriteLine($"[MERGE] Could not load {pathA}. Creating empty.");
                pointsA = new List<Vertex>();
                raysA = new List<Ray>();
            }

            try
            {
                DatabaseIO.LoadDatabase(pathB, out pointsB, out raysB);
            }
            catch
            {
                Console.WriteLine($"[MERGE] Could not load {pathB}. Skipping.");
                return;
            }

            Console.WriteLine($"[MERGE] Merging DB B ({pointsB.Count} pts, {raysB.Count} rays) into DB A ({pointsA.Count} pts, {raysA.Count} rays)...");

            int addedPoints = PointMerger.MergePoints(pointsA, pointsB, Settings.MIN_MERGE_DISTANCE);
            raysA.AddRange(raysB);

            DatabaseIO.SaveDatabase(pointsA, raysA, pathOut);
            Console.WriteLine($"[MERGE] Integrated {addedPoints} points and {raysB.Count} rays. Total: {pointsA.Count} pts, {raysA.Count} rays. Saved: {pathOut}");
        }
    }
}
