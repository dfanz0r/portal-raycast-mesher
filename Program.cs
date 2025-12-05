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

            if (cmd.Command == "help" || cmd.HasFlag("help") || cmd.HasFlag("h") || cmd.HasFlag("?"))
            {
                ShowHelp();
                return;
            }

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

            // Interactive Database Selection
            string dbPath = ResolveDatabasePath(cmd);
            string dbName = Path.GetFileNameWithoutExtension(dbPath);

            // Output defaults to db name .glb, unless overridden
            string objPath = cmd.GetOption("out", $"{dbName}.glb");

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
            if (!cmd.HasFlag("nolog") && File.Exists(logPath))
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

            if (cmd.Command == "update")
            {
                Console.WriteLine("[DONE] Database updated. Meshing skipped.");
                return;
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

        static string ResolveDatabasePath(CommandLineArgs cmd)
        {
            // 1. Explicit Flag
            string? explicitDb = cmd.GetOption("db", null);
            if (!string.IsNullOrEmpty(explicitDb)) return explicitDb;

            // 2. Scan Directory
            var dbFiles = new DirectoryInfo(Directory.GetCurrentDirectory())
                .GetFiles("*.db")
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();

            if (dbFiles.Count == 0)
            {
                return PromptForNewDatabase();
            }

            // 3. Interactive Menu
            Console.WriteLine("Select a database to use (Arrow Keys to Move, Enter to Select):");
            int selection = 0;
            int totalOptions = dbFiles.Count + 1;

            // Reserve space to prevent scrolling artifacts
            for (int i = 0; i < totalOptions; i++) Console.WriteLine();
            int startTop = Console.CursorTop - totalOptions;
            if (startTop < 0) startTop = 0;

            bool selected = false;

            // Hide cursor
            Console.CursorVisible = false;

            while (!selected)
            {
                Console.SetCursorPosition(0, startTop);

                for (int i = 0; i < totalOptions; i++)
                {
                    if (i == selection)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write("> ");
                    }
                    else
                    {
                        Console.Write("  ");
                    }

                    if (i < dbFiles.Count)
                    {
                        Console.WriteLine($"{dbFiles[i].Name,-30} (Last Used: {dbFiles[i].LastWriteTime:g})");
                    }
                    else
                    {
                        Console.WriteLine("[Create New Database]");
                    }
                    Console.ResetColor();
                }

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    selection--;
                    if (selection < 0) selection = totalOptions - 1;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selection++;
                    if (selection >= totalOptions) selection = 0;
                }
                else if (key == ConsoleKey.Enter)
                {
                    selected = true;
                }
            }

            Console.CursorVisible = true;
            Console.WriteLine(); // New line after selection

            if (selection < dbFiles.Count)
            {
                // Update LastWriteTime to mark as recently used
                try { File.SetLastWriteTime(dbFiles[selection].FullName, DateTime.Now); } catch { }
                return dbFiles[selection].Name;
            }
            else
            {
                return PromptForNewDatabase();
            }
        }

        static string PromptForNewDatabase()
        {
            Console.WriteLine();
            Console.Write("Enter name for new database (no extension): ");
            string name = Console.ReadLine()?.Trim() ?? "terrain";
            if (string.IsNullOrEmpty(name)) name = "terrain";
            if (!name.EndsWith(".db")) name += ".db";
            return name;
        }

        static void ShowHelp()
        {
            Console.WriteLine("Battlefield Raycast Mesher - Help");
            Console.WriteLine("=================================");
            Console.WriteLine("Usage: meshtool [command] [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  run (default)   Ingest logs, update database, and generate mesh.");
            Console.WriteLine("  update          Ingest logs and update database ONLY (skips meshing).");
            Console.WriteLine("  merge           Merge two databases together.");
            Console.WriteLine("  help            Show this help message.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -db <path>      Specify database file to use (skips menu).");
            Console.WriteLine("  -log <path>     Specify log file to ingest (default: auto-detected).");
            Console.WriteLine("  -out <path>     Specify output file (default: <dbname>.glb).");
            Console.WriteLine("  -nolog          Skip log ingestion step.");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  meshtool run -db terrain.db");
            Console.WriteLine("  meshtool update -db terrain.db");
            Console.WriteLine("  meshtool merge old.db new.db combined.db");
        }
    }
}
