using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MeshTool.Core.Algorithms;
using MeshTool.Core.Config;
using MeshTool.Core.Data;

namespace MeshTool.Core.IO
{
    /// <summary>
    /// Options for configuring monitor run behavior.
    /// </summary>
    public sealed class MonitorRunOptions
    {
        /// <summary>
        /// Whether to start tailing at the end of the file. Default is true.
        /// </summary>
        public bool StartAtEnd { get; init; } = true;

        /// <summary>
        /// Maximum number of lines to buffer in the channel. Default is 8192.
        /// </summary>
        public int ChannelCapacity { get; init; } = 8192;

        /// <summary>
        /// Interval for flushing pending data. Default is 200ms.
        /// </summary>
        public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// Number of pending items that triggers an immediate flush. Default is 500.
        /// </summary>
        public int FlushThreshold { get; init; } = 500;

        /// <summary>
        /// Minimum idle time before a save is allowed. Default is 1 second.
        /// </summary>
        public TimeSpan SaveDebounce { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Minimum interval between saves. Default is 5 seconds.
        /// </summary>
        public TimeSpan SaveMinInterval { get; init; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Maximum interval between saves. Default is 30 seconds.
        /// </summary>
        public TimeSpan SaveMaxInterval { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Whether to include full snapshots in updates. Default is false.
        /// </summary>
        public bool IncludeSnapshots { get; init; } = false;

        /// <summary>
        /// Minimum interval between snapshots. Default is 500ms.
        /// </summary>
        public TimeSpan SnapshotMinInterval { get; init; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Optional logging callback. Defaults to Logger.Info if not specified.
        /// </summary>
        public Action<string>? Log { get; init; }

        /// <summary>
        /// Callback for receiving monitor updates.
        /// </summary>
        public Action<MonitorUpdate>? OnUpdate { get; init; }
    }

    /// <summary>
    /// Represents an update from the monitor runner.
    /// </summary>
    /// <param name="AddedPoints">Number of points added in this update.</param>
    /// <param name="AddedMisses">Number of miss rays added in this update.</param>
    /// <param name="TotalPoints">Total number of points in the database.</param>
    /// <param name="TotalMisses">Total number of miss rays in the database.</param>
    /// <param name="ProcessedLines">Number of lines processed since monitoring started.</param>
    /// <param name="ApproxFileLine">Approximate current line number in the file.</param>
    /// <param name="AvgDistance">Estimated average distance between points.</param>
    /// <param name="PointsSnapshot">Full snapshot of points (if requested).</param>
    /// <param name="MissesSnapshot">Full snapshot of miss rays (if requested).</param>
    /// <param name="NewPoints">Array of newly added points.</param>
    /// <param name="NewMisses">Array of newly added miss rays.</param>
    /// <param name="IsFinal">Whether this is the final update.</param>
    public readonly record struct MonitorUpdate(
        int AddedPoints,
        int AddedMisses,
        int TotalPoints,
        int TotalMisses,
        long ProcessedLines,
        long ApproxFileLine,
        float AvgDistance,
        Vertex[]? PointsSnapshot,
        Ray[]? MissesSnapshot,
        Vertex[]? NewPoints,
        Ray[]? NewMisses,
        bool IsFinal);

    /// <summary>
    /// Runs monitoring of a log file, parsing and accumulating points and miss rays.
    /// </summary>
    public static class MonitorRunner
    {
        /// <summary>
        /// Runs the monitor with default options.
        /// </summary>
        /// <param name="logPath">Path to the log file to monitor.</param>
        /// <param name="dbPath">Path to the database file for persistence.</param>
        /// <param name="cancellationToken">Token to cancel monitoring.</param>
        public static async Task RunAsync(string logPath, string dbPath, CancellationToken cancellationToken)
        {
            await RunAsync(logPath, dbPath, cancellationToken, null);
        }

        /// <summary>
        /// Runs the monitor with specified options.
        /// </summary>
        /// <param name="logPath">Path to the log file to monitor.</param>
        /// <param name="dbPath">Path to the database file for persistence.</param>
        /// <param name="cancellationToken">Token to cancel monitoring.</param>
        /// <param name="options">Optional configuration options.</param>
        public static async Task RunAsync(string logPath, string dbPath, CancellationToken cancellationToken, MonitorRunOptions? options)
        {
            if (string.IsNullOrEmpty(logPath))
                throw new ArgumentNullException(nameof(logPath));
            if (string.IsNullOrEmpty(dbPath))
                throw new ArgumentNullException(nameof(dbPath));

            options ??= new MonitorRunOptions();
            Action<string> log = options.Log ?? Logger.Info;

            // Load DB snapshot
            List<Vertex> masterPoints;
            List<Ray> masterMisses;

            if (File.Exists(dbPath))
            {
                try
                {
                    DatabaseIO.LoadDatabase(dbPath, out masterPoints, out masterMisses);
                    log($"[DB] Loaded: {masterPoints.Count} points, {masterMisses.Count} rays");
                }
                catch (Exception ex)
                {
                    log($"[DB] Error loading DB: {ex.Message}. Starting fresh.");
                    masterPoints = new List<Vertex>();
                    masterMisses = new List<Ray>();
                }
            }
            else
            {
                masterPoints = new List<Vertex>();
                masterMisses = new List<Ray>();
                log("[DB] No existing database found. Starting fresh.");
            }

            var gate = new object();

            var pointIndex = new IncrementalPointIndex(masterPoints, Settings.MIN_MERGE_DISTANCE, refreshExistingSpawnTime: false);

            long processedLines = 0;
            long baselineFileLines = 0;
            int totalHits = 0;
            int totalMisses = 0;
            int totalMergedPoints = 0;

            bool dirty = false;
            DateTime lastMutationUtc = DateTime.MinValue;
            DateTime lastSaveUtc = DateTime.MinValue;
            DateTime lastSnapshotUtc = DateTime.MinValue;

            // Calculate initial bounds and spacing
            var initialBounds = PointAnalysis.CalculateBoundsXZ(masterPoints, out bool hasBounds);
            float avgDistance = PointAnalysis.EstimateSpacingFromDensity(masterPoints.Count, in initialBounds);

            var lineChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(options.ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            var tailer = new LogTailer(logPath, lineChannel.Writer, startAtEnd: options.StartAtEnd);

            // Establish a baseline line count that matches the file at the moment we start tailing.
            // This makes the displayed "fileLine" approximate the editor's line count.
            baselineFileLines = CountFileLinesExact(logPath);
            tailer.Reset += e =>
            {
                // For new file/rotation/truncation, baseline should reflect the new file state.
                // If we're tailing from end, baseline is the full file's current line count.
                // If tailing from start, baseline is 0.
                baselineFileLines = e.StartAtEnd ? CountFileLinesExact(logPath) : 0;
                Interlocked.Exchange(ref processedLines, 0);
            };

            EmitUpdate(0, 0, null, null, isFinal: false, forceSnapshot: true);

            Task tailTask = Task.Run(() => tailer.RunAsync(cancellationToken), cancellationToken);

            Task consumeTask = Task.Run(async () =>
            {
                var pendingHits = new List<Vertex>(512);
                var pendingMisses = new List<Ray>(512);
                DateTime lastFlushUtc = DateTime.UtcNow;
                TimeSpan flushInterval = options.FlushInterval;
                int flushThreshold = options.FlushThreshold;

                void FlushBatchesIfAny()
                {
                    if (pendingHits.Count == 0 && pendingMisses.Count == 0) return;

                    int addedPoints = 0;
                    int addedMisses = pendingMisses.Count;
                    Vertex[]? newPointsArray = null;
                    Ray[]? newMissesArray = null;

                    lock (gate)
                    {
                        if (pendingHits.Count > 0)
                        {
                            int startCount = masterPoints.Count;
                            addedPoints = pointIndex.AddRange(masterPoints, pendingHits);
                            totalMergedPoints += addedPoints;
                            totalHits += pendingHits.Count;

                            if (addedPoints > 0)
                            {
                                newPointsArray = new Vertex[addedPoints];
                                masterPoints.CopyTo(startCount, newPointsArray, 0, addedPoints);

                                dirty = true;
                                lastMutationUtc = DateTime.UtcNow;

                                if (newPointsArray.Length > 0)
                                {
                                    for (int i = 0; i < newPointsArray.Length; i++)
                                    {
                                        var p = newPointsArray[i].Position;
                                        if (!hasBounds)
                                        {
                                            initialBounds = new Bounds
                                            {
                                                MinX = p.X,
                                                MaxX = p.X,
                                                MinZ = p.Z,
                                                MaxZ = p.Z
                                            };
                                            hasBounds = true;
                                        }
                                        else
                                        {
                                            if (p.X < initialBounds.MinX) initialBounds.MinX = p.X;
                                            if (p.X > initialBounds.MaxX) initialBounds.MaxX = p.X;
                                            if (p.Z < initialBounds.MinZ) initialBounds.MinZ = p.Z;
                                            if (p.Z > initialBounds.MaxZ) initialBounds.MaxZ = p.Z;
                                        }
                                    }
                                    avgDistance = PointAnalysis.EstimateSpacingFromDensity(masterPoints.Count, in initialBounds);
                                }
                            }
                        }

                        if (pendingMisses.Count > 0)
                        {
                            int startCount = masterMisses.Count;
                            masterMisses.AddRange(pendingMisses);
                            newMissesArray = new Ray[pendingMisses.Count];
                            masterMisses.CopyTo(startCount, newMissesArray, 0, pendingMisses.Count);

                            totalMisses += pendingMisses.Count;
                            dirty = true;
                            lastMutationUtc = DateTime.UtcNow;
                        }
                    }

                    if (addedPoints > 0 || addedMisses > 0)
                    {
                        EmitUpdate(addedPoints, addedMisses, newPointsArray, newMissesArray, isFinal: false, forceSnapshot: false);
                    }

                    pendingHits.Clear();
                    pendingMisses.Clear();
                }

                await foreach (var line in lineChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    processedLines++;

                    if (LogParser.TryParseLine(line, out var hit, out var miss, out bool isMiss))
                    {
                        if (isMiss)
                            pendingMisses.Add(miss);
                        else if (hit != null)
                            pendingHits.Add(hit);
                    }

                    // Flush on time or size to keep up with spikes.
                    if ((pendingHits.Count + pendingMisses.Count) >= flushThreshold || (DateTime.UtcNow - lastFlushUtc) >= flushInterval)
                    {
                        FlushBatchesIfAny();
                        lastFlushUtc = DateTime.UtcNow;
                    }

                    // lightweight progress
                    if ((processedLines % 250) == 0)
                    {
                        int p, r;
                        lock (gate) { p = masterPoints.Count; r = masterMisses.Count; }
                        long approxFileLine = baselineFileLines + processedLines;
                        log($"[MON] processed={processedLines} fileLine~={approxFileLine} points={p} rays={r} (+{totalMergedPoints} merged)");
                    }
                }

                // Drain any remaining work if the channel completes.
                FlushBatchesIfAny();
            }, cancellationToken);

            Task saveTask = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(500, cancellationToken);

                    bool didSave = false;
                    int p = 0, r = 0;
                    Vertex[]? pointsCopy = null;
                    Ray[]? raysCopy = null;

                    lock (gate)
                    {
                        if (!dirty) continue;

                        var now = DateTime.UtcNow;
                        bool debounced = (now - lastMutationUtc) >= options.SaveDebounce;
                        bool minIntervalOk = lastSaveUtc == DateTime.MinValue || (now - lastSaveUtc) >= options.SaveMinInterval;
                        bool maxIntervalHit = lastSaveUtc != DateTime.MinValue && (now - lastSaveUtc) >= options.SaveMaxInterval;

                        if ((debounced && minIntervalOk) || maxIntervalHit)
                        {
                            pointsCopy = masterPoints.ToArray();
                            raysCopy = masterMisses.ToArray();
                            dirty = false;
                            lastSaveUtc = now;
                            p = masterPoints.Count;
                            r = masterMisses.Count;
                            didSave = true;
                        }
                    }

                    if (didSave && pointsCopy != null && raysCopy != null)
                    {
                        SaveDatabaseAtomic(pointsCopy, raysCopy, dbPath);
                        log($"[DB] Saved: {p} points, {r} rays ({lastSaveUtc:T})");
                    }
                }
            }, cancellationToken);

            try
            {
                await Task.WhenAll(tailTask, consumeTask, saveTask);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            finally
            {
                int finalPoints;
                int finalRays;
                Vertex[]? pointsCopy = null;
                Ray[]? raysCopy = null;
                lock (gate)
                {
                    pointsCopy = masterPoints.ToArray();
                    raysCopy = masterMisses.ToArray();
                    finalPoints = masterPoints.Count;
                    finalRays = masterMisses.Count;
                }

                if (pointsCopy != null && raysCopy != null)
                {
                    SaveDatabaseAtomic(pointsCopy, raysCopy, dbPath);
                }

                EmitUpdate(0, 0, null, null, isFinal: true, forceSnapshot: true);
                log($"[DB] Final save: {finalPoints} points, {finalRays} rays");
                log($"[MONITOR] Done. processed={processedLines} fileLine~={baselineFileLines + processedLines} hits={totalHits} misses={totalMisses} mergedPoints={totalMergedPoints}");
            }

            void EmitUpdate(int addedPoints, int addedMisses, Vertex[]? newPointsArray, Ray[]? newMissesArray, bool isFinal, bool forceSnapshot)
            {
                if (options.OnUpdate == null) return;

                Vertex[]? points = null;
                Ray[]? misses = null;

                int totalPoints;
                int totalMisses;
                float spacing;

                lock (gate)
                {
                    totalPoints = masterPoints.Count;
                    totalMisses = masterMisses.Count;
                    spacing = avgDistance;

                    bool snapshotDue = options.IncludeSnapshots && (forceSnapshot || (DateTime.UtcNow - lastSnapshotUtc) >= options.SnapshotMinInterval);
                    if (snapshotDue)
                    {
                        points = masterPoints.ToArray();
                        misses = masterMisses.ToArray();
                        lastSnapshotUtc = DateTime.UtcNow;
                    }
                }

                long lines = Interlocked.Read(ref processedLines);
                var update = new MonitorUpdate(
                    addedPoints,
                    addedMisses,
                    totalPoints,
                    totalMisses,
                    lines,
                    baselineFileLines + lines,
                    spacing,
                    points,
                    misses,
                    newPointsArray,
                    newMissesArray,
                    isFinal);

                options.OnUpdate(update);
            }
        }

        /// <summary>
        /// Counts the exact number of lines in a file.
        /// </summary>
        /// <param name="path">Path to the file.</param>
        /// <returns>The number of lines, or 0 if the file doesn't exist or cannot be read.</returns>
        private static long CountFileLinesExact(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;

                long lines = 0;
                byte[] buffer = new byte[1024 * 1024];
                int read;
                bool sawAnyByte = false;
                byte lastByte = 0;

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sawAnyByte = true;
                    for (int i = 0; i < read; i++)
                    {
                        if (buffer[i] == (byte)'\n') lines++;
                    }
                    lastByte = buffer[read - 1];
                }

                if (sawAnyByte && lastByte != (byte)'\n') lines++;
                return lines;
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }

        /// <summary>
        /// Atomically saves the database to disk.
        /// </summary>
        /// <param name="points">Points to save.</param>
        /// <param name="rays">Rays to save.</param>
        /// <param name="path">Destination path.</param>
        private static void SaveDatabaseAtomic(IReadOnlyList<Vertex> points, IReadOnlyList<Ray> rays, string path)
        {
            string tmp = path + ".tmp";

            try
            {
                DatabaseIO.SaveDatabase(points, rays, tmp);
            }
            catch (Exception)
            {
                // Clean up temp file if save failed
                try { File.Delete(tmp); } catch { }
                throw;
            }

            try
            {
                if (File.Exists(path))
                {
                    // Atomic replace on Windows where possible.
                    File.Replace(tmp, path, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            catch (IOException)
            {
                // Fallback: best-effort move
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                }
                catch (IOException)
                {
                    // give up - data is in temp file at least
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Fallback: best-effort move
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                }
                catch (IOException)
                {
                    // give up
                }
            }
        }
    }
}
