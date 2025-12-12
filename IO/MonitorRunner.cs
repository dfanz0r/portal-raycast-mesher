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
    public static class MonitorRunner
    {
        public static async Task RunAsync(string logPath, string dbPath, CancellationToken cancellationToken)
        {
            // Load DB snapshot
            List<Vertex> masterPoints;
            List<Ray> masterMisses;

            if (File.Exists(dbPath))
            {
                try
                {
                    DatabaseIO.LoadDatabase(dbPath, out masterPoints, out masterMisses);
                    Console.WriteLine($"[DB] Loaded: {masterPoints.Count} points, {masterMisses.Count} rays");
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

            // Save throttling
            TimeSpan saveDebounce = TimeSpan.FromSeconds(1);
            TimeSpan saveMinInterval = TimeSpan.FromSeconds(5);
            TimeSpan saveMaxInterval = TimeSpan.FromSeconds(30);

            var lineChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(8192)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            var tailer = new LogTailer(logPath, lineChannel.Writer, startAtEnd: true);

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

            Task tailTask = Task.Run(() => tailer.RunAsync(cancellationToken), cancellationToken);

            Task consumeTask = Task.Run(async () =>
            {
                var pendingHits = new List<Vertex>(512);
                var pendingMisses = new List<Ray>(512);
                DateTime lastFlushUtc = DateTime.UtcNow;
                TimeSpan flushInterval = TimeSpan.FromMilliseconds(200);
                const int flushThreshold = 500;

                void FlushBatchesIfAny()
                {
                    if (pendingHits.Count == 0 && pendingMisses.Count == 0) return;

                    lock (gate)
                    {
                        if (pendingHits.Count > 0)
                        {
                            int added = pointIndex.AddRange(masterPoints, pendingHits);
                            totalMergedPoints += added;
                            totalHits += pendingHits.Count;

                            if (added > 0)
                            {
                                dirty = true;
                                lastMutationUtc = DateTime.UtcNow;
                            }
                        }

                        if (pendingMisses.Count > 0)
                        {
                            masterMisses.AddRange(pendingMisses);
                            totalMisses += pendingMisses.Count;
                            dirty = true;
                            lastMutationUtc = DateTime.UtcNow;
                        }
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
                        Console.WriteLine($"[MON] processed={processedLines} fileLine~={approxFileLine} points={p} rays={r} (+{totalMergedPoints} merged)");
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

                    lock (gate)
                    {
                        if (!dirty) continue;

                        var now = DateTime.UtcNow;
                        bool debounced = (now - lastMutationUtc) >= saveDebounce;
                        bool minIntervalOk = lastSaveUtc == DateTime.MinValue || (now - lastSaveUtc) >= saveMinInterval;
                        bool maxIntervalHit = lastSaveUtc != DateTime.MinValue && (now - lastSaveUtc) >= saveMaxInterval;

                        if ((debounced && minIntervalOk) || maxIntervalHit)
                        {
                            SaveDatabaseAtomic(masterPoints, masterMisses, dbPath);
                            dirty = false;
                            lastSaveUtc = now;
                            p = masterPoints.Count;
                            r = masterMisses.Count;
                            didSave = true;
                        }
                    }

                    if (didSave)
                        Console.WriteLine($"[DB] Saved: {p} points, {r} rays ({lastSaveUtc:T})");
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
                lock (gate)
                {
                    SaveDatabaseAtomic(masterPoints, masterMisses, dbPath);
                    finalPoints = masterPoints.Count;
                    finalRays = masterMisses.Count;
                }

                Console.WriteLine($"[DB] Final save: {finalPoints} points, {finalRays} rays");
                Console.WriteLine($"[MONITOR] Done. processed={processedLines} fileLine~={baselineFileLines + processedLines} hits={totalHits} misses={totalMisses} mergedPoints={totalMergedPoints}");
            }
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

        private static void SaveDatabaseAtomic(List<Vertex> points, List<Ray> rays, string path)
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
