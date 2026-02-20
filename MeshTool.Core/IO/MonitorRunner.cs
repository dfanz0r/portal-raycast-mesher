using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TerrainTool.Algorithms;
using TerrainTool.Config;
using TerrainTool.Data;

namespace TerrainTool.IO
{
    public sealed class MonitorRunOptions
    {
        public bool StartAtEnd { get; init; } = true;
        public int ChannelCapacity { get; init; } = 8192;
        public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(200);
        public int FlushThreshold { get; init; } = 500;
        public TimeSpan SaveDebounce { get; init; } = TimeSpan.FromSeconds(1);
        public TimeSpan SaveMinInterval { get; init; } = TimeSpan.FromSeconds(5);
        public TimeSpan SaveMaxInterval { get; init; } = TimeSpan.FromSeconds(30);
        public bool IncludeSnapshots { get; init; } = false;
        public TimeSpan SnapshotMinInterval { get; init; } = TimeSpan.FromMilliseconds(500);
        public Action<string>? Log { get; init; }
        public Action<MonitorUpdate>? OnUpdate { get; init; }
    }

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

    public static class MonitorRunner
    {
        public static async Task RunAsync(string logPath, string dbPath, CancellationToken cancellationToken)
        {
            await RunAsync(logPath, dbPath, cancellationToken, null);
        }

        public static async Task RunAsync(string logPath, string dbPath, CancellationToken cancellationToken, MonitorRunOptions? options)
        {
            options ??= new MonitorRunOptions();
            Action<string> log = options.Log ?? Console.WriteLine;

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

            var pointIndex = new IncrementalPointIndex(masterPoints, Settings.MIN_MERGE_DISTANCE);

            long processedLines = 0;
            long baselineFileLines = 0;
            int totalHits = 0;
            int totalMisses = 0;
            int totalMergedPoints = 0;

            bool dirty = false;
            DateTime lastMutationUtc = DateTime.MinValue;
            DateTime lastSaveUtc = DateTime.MinValue;
            DateTime lastSnapshotUtc = DateTime.MinValue;
            float avgDistance = EstimateAverageSpacing(masterPoints);

            var lineChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(options.ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            var tailer = new LogTailer(logPath, lineChannel.Writer, startAtEnd: options.StartAtEnd);

            // Establish a baseline line count that matches the file at the moment we start tailing.
            // This makes the displayed "fileLine" approximate the editor's line count.
            baselineFileLines = CountFileLinesApprox(logPath);
            tailer.Reset += e =>
            {
                // For new file/rotation/truncation, baseline should reflect the new file state.
                // If we're tailing from end, baseline is the full file's current line count.
                // If tailing from start, baseline is 0.
                baselineFileLines = e.StartAtEnd ? CountFileLinesApprox(logPath) : 0;
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
                                // Only recalculate average distance occasionally to prevent lag
                                if (masterPoints.Count % 5000 < pendingHits.Count)
                                {
                                    avgDistance = EstimateAverageSpacing(masterPoints);
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

                    if (line.StartsWith("[FRAGMENT]", StringComparison.Ordinal))
                        continue;

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

        private static float EstimateAverageSpacing(List<Vertex> points)
        {
            if (points.Count < 2) return 1.0f;

            double minX = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxZ = double.MinValue;

            foreach (var p in points)
            {
                if (p.Position.X < minX) minX = p.Position.X;
                if (p.Position.X > maxX) maxX = p.Position.X;
                if (p.Position.Z < minZ) minZ = p.Position.Z;
                if (p.Position.Z > maxZ) maxZ = p.Position.Z;
            }

            double dx = Math.Max(1e-3, maxX - minX);
            double dz = Math.Max(1e-3, maxZ - minZ);
            double area = dx * dz;
            double spacing = Math.Sqrt(area / Math.Max(1, points.Count));

            return (float)Math.Clamp(spacing, 0.02, 500.0);
        }

        private static long CountFileLinesApprox(string path)
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

                // If file isn't empty and doesn't end with a newline, it still has a final line.
                if (sawAnyByte && lastByte != (byte)'\n') lines++;
                return lines;
            }
            catch
            {
                return 0;
            }
        }

        private static void SaveDatabaseAtomic(IReadOnlyList<Vertex> points, IReadOnlyList<Ray> rays, string path)
        {
            string tmp = path + ".tmp";
            DatabaseIO.SaveDatabase(points, rays, tmp);

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
            catch
            {
                // Fallback: best-effort move
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                }
                catch
                {
                    // give up
                }
            }
        }
    }
}
