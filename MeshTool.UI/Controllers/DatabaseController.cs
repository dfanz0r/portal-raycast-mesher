using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MeshTool.Core.Algorithms;
using MeshTool.Core.Data;
using MeshTool.Core.IO;

namespace MeshTool.UI.Controllers
{
    /// <summary>
    /// Handles database file operations and viewport-oriented data loading.
    /// </summary>
    public class DatabaseController
    {
        /// <summary>
        /// Gets database file entries from the given directory.
        /// </summary>
        public IReadOnlyList<string> GetLocalDatabaseEntries(string directory)
        {
            var entries = new List<string>();
            foreach (var file in Directory.GetFiles(directory, "*.db"))
            {
                entries.Add(Path.GetFileName(file));
            }

            return entries;
        }

        /// <summary>
        /// Resolves a selected combo box entry to a full database path.
        /// </summary>
        public string ResolveSelectedPath(string selectedEntry, string currentDirectory)
        {
            return Path.IsPathRooted(selectedEntry)
                ? selectedEntry
                : Path.Combine(currentDirectory, selectedEntry);
        }

        /// <summary>
        /// Gets a display entry for a database path.
        /// </summary>
        public string GetDisplayEntry(string path, string currentDirectory)
        {
            string fullPath = Path.GetFullPath(path);
            string cwd = Path.GetFullPath(currentDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string dir = Path.GetDirectoryName(fullPath) ?? string.Empty;
            dir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(dir, cwd, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(fullPath)
                : fullPath;
        }

        /// <summary>
        /// Creates an empty database at the specified path.
        /// </summary>
        public void CreateEmptyDatabase(string path)
        {
            DatabaseIO.SaveDatabase(Array.Empty<Vertex>(), Array.Empty<Ray>(), path);
        }

        /// <summary>
        /// Clears all points and rays from the specified database.
        /// </summary>
        public void ClearDatabase(string path)
        {
            DatabaseIO.SaveDatabase(Array.Empty<Vertex>(), Array.Empty<Ray>(), path);
        }

        /// <summary>
        /// Saves the current selection edits to disk.
        /// </summary>
        public Task SaveSelectionAsync(string path, IReadOnlyList<Vertex> points, IReadOnlyList<Ray> rays)
        {
            return Task.Run(() => DatabaseIO.SaveDatabase(points, rays, path));
        }

        /// <summary>
        /// Loads database content prepared for viewport rendering.
        /// </summary>
        public Task<DatabaseLoadResult?> LoadForViewportAsync(string path, Action<string> log)
        {
            return Task.Run<DatabaseLoadResult?>(() =>
            {
                log($"[DB] Loading {path}");
                DatabaseIO.LoadDatabase(path, out var points, out var misses);

                for (int i = 0; i < points.Count; i++)
                {
                    points[i].SpawnTime = 0f;
                }

                for (int i = 0; i < misses.Count; i++)
                {
                    var ray = misses[i];
                    ray.SpawnTime = 0f;
                    misses[i] = ray;
                }

                if (points.Count < 3)
                {
                    log("[ERROR] Not enough points to view.");
                    return null;
                }

                log("[RENDER] Estimating average point distance...");
                float avgDistance = PointAnalysis.EstimateAverageSpacingParallel(points);
                log($"[RENDER] Computed avg point distance: {avgDistance:F4}");

                return new DatabaseLoadResult(points, misses, avgDistance);
            });
        }
    }

    /// <summary>
    /// Loaded database content prepared for viewport rendering.
    /// </summary>
    /// <param name="Points">Loaded point data.</param>
    /// <param name="Misses">Loaded miss ray data.</param>
    /// <param name="AverageDistance">Estimated average point spacing.</param>
    public sealed record DatabaseLoadResult(List<Vertex> Points, List<Ray> Misses, float AverageDistance);
}
