using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerrainTool.Algorithms;
using TerrainTool.Config;
using TerrainTool.IO;

namespace MeshTool.UI;

public partial class MainWindow : Window
{
    private string _dbPath = string.Empty;
    private readonly string _logPath;

    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _logLines = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _dbFiles = new();

    private bool _isDbLoaded = false;

    public MainWindow()
    {
        InitializeComponent();
        Viewport.OnLog = Log;
        Viewport.OnHoveredCoordinateChanged = OnHoveredCoordinateChanged;
        Viewport.OnMoveSpeedChanged = OnMoveSpeedChanged;
        LstConsole.ItemsSource = _logLines;
        CmbDbPath.ItemsSource = _dbFiles;

        _logPath = ResolvePortalLogPath();
        TxtLogPath.Text = _logPath;

        LoadLocalDbFiles();
    }

    private void OnMoveSpeedChanged(float speed)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            TxtCameraSpeed.Text = $"{speed:F1} m/s";
        });
    }

    private void OnHoveredCoordinateChanged(Silk.NET.Maths.Vector3D<float>? coord)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (coord.HasValue)
            {
                TxtHoveredCoord.Text = $"X: {coord.Value.X:F2}, Y: {coord.Value.Y:F2}, Z: {coord.Value.Z:F2}";
            }
            else
            {
                TxtHoveredCoord.Text = "X: ---, Y: ---, Z: ---";
            }
        });
    }

    private void LoadLocalDbFiles()
    {
        _dbFiles.Clear();
        try
        {
            var files = Directory.GetFiles(Environment.CurrentDirectory, "*.db");
            foreach (var f in files)
            {
                _dbFiles.Add(Path.GetFileName(f));
            }
            if (_dbFiles.Count > 0)
            {
                CmbDbPath.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to load local DB files: {ex.Message}");
        }
    }

    private void CmbDbPath_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CmbDbPath.SelectedItem is string selected)
        {
            if (!Path.IsPathRooted(selected))
            {
                _dbPath = Path.Combine(Environment.CurrentDirectory, selected);
            }
            else
            {
                _dbPath = selected;
            }
            _isDbLoaded = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitorCts?.Cancel();
        base.OnClosed(e);
    }

    private void Log(string message)
    {
        Console.WriteLine(message);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _logLines.Add(message);
            while (_logLines.Count > 200)
            {
                _logLines.RemoveAt(0);
            }
            LstConsole.ScrollIntoView(_logLines[^1]);
        });
    }

    private static string ResolvePortalLogPath()
    {
        return Path.Combine(Path.GetTempPath(), "Battlefieldâ„¢ 6", "PortalLog.txt");
    }

    private async void BtnBrowseDb_Click(object sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Database",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Database Files") { Patterns = new[] { "*.db" } } }
        });

        if (files.Count >= 1)
        {
            _dbPath = files[0].Path.LocalPath;
            if (!_dbFiles.Contains(_dbPath))
            {
                _dbFiles.Add(_dbPath);
            }
            CmbDbPath.SelectedItem = _dbPath;
            _isDbLoaded = false;
        }
    }

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorTask != null)
        {
            Log("[ERROR] Stop monitor before manual update.");
            return;
        }

        if (string.IsNullOrEmpty(_dbPath))
        {
            Log("[ERROR] Select DB first.");
            return;
        }

        BtnUpdate.IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                Log($"[DB] Loading {_dbPath}");
                DatabaseIO.LoadDatabase(_dbPath, out var masterPoints, out var masterMisses);

                Log($"[LOG] Parsing {_logPath}");
                LogParser.ParseLog(_logPath, out var newPoints, out var newMisses);

                if (newPoints.Count > 0 || newMisses.Count > 0)
                {
                    int added = PointMerger.MergePoints(masterPoints, newPoints, Settings.MIN_MERGE_DISTANCE);
                    masterMisses.AddRange(newMisses);
                    DatabaseIO.SaveDatabase(masterPoints, masterMisses, _dbPath);
                    Log($"[MERGE] Added {added} points and {newMisses.Count} misses.");

                    try
                    {
                        File.WriteAllText(_logPath, string.Empty);
                        Log("[LOG] Cleared log file.");
                    }
                    catch
                    {
                        Log("[WARN] Failed to clear log file.");
                    }
                }
                else
                {
                    Log("[LOG] No new data found.");
                }
            });
        }
        catch (Exception ex)
        {
            Log($"[ERROR] {ex.Message}");
        }
        finally
        {
            BtnUpdate.IsEnabled = true;
        }
    }

    private async void BtnMesh_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorTask != null)
        {
            Log("[ERROR] Stop monitor before Load Points & View.");
            return;
        }

        if (string.IsNullOrEmpty(_dbPath))
        {
            Log("[ERROR] Select DB file first.");
            return;
        }

        BtnMesh.IsEnabled = false;
        try
        {
            await LoadDbAsync();
        }
        catch (Exception ex)
        {
            Log($"[ERROR] {ex.Message}");
        }
        finally
        {
            BtnMesh.IsEnabled = true;
        }
    }

    private async Task LoadDbAsync()
    {
        await Task.Run(() =>
        {
            Log($"[DB] Loading {_dbPath}");
            DatabaseIO.LoadDatabase(_dbPath, out var masterPoints, out var masterMisses);

            if (masterPoints.Count < 3)
            {
                Log("[ERROR] Not enough points to view.");
                return;
            }

            Log("[RENDER] Estimating average point distance...");

            // We need to find the minimum distance between points to accurately size the surfels.
            // Since points are often added in a grid pattern, we can sample a subset of points
            // and find their nearest neighbors. We use a spatial hash grid to make this fast.

            int sampleCount = Math.Min(5000, masterPoints.Count);
            double minTotalDist = 0;
            int validSamples = 0;

            // Create a coarse spatial grid for fast neighbor lookup
            double cellSize = 100.0; // Assume points are closer than 100 units
            var grid = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<TerrainTool.Data.Vertex>>();

            long GetHash(double x, double y, double z)
            {
                int gx = (int)Math.Floor(x / cellSize);
                int gy = (int)Math.Floor(y / cellSize);
                int gz = (int)Math.Floor(z / cellSize);
                return ((long)gx * 73856093) ^ ((long)gy * 19349663) ^ ((long)gz * 83492791);
            }

            // Populate grid with all points
            foreach (var p in masterPoints)
            {
                long h = GetHash(p.Position.X, p.Position.Y, p.Position.Z);
                if (!grid.TryGetValue(h, out var list))
                {
                    list = new System.Collections.Generic.List<TerrainTool.Data.Vertex>();
                    grid[h] = list;
                }
                list.Add(p);
            }

            // Sample points and find their nearest neighbor
            var rand = new Random(42);
            var samplePoints = new TerrainTool.Data.Vertex[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                samplePoints[i] = masterPoints[rand.Next(masterPoints.Count)];
            }

            object lockObj = new object();

            Parallel.ForEach(samplePoints, p1 =>
            {
                double minDistSq = double.MaxValue;

                int cx = (int)Math.Floor(p1.Position.X / cellSize);
                int cy = (int)Math.Floor(p1.Position.Y / cellSize);
                int cz = (int)Math.Floor(p1.Position.Z / cellSize);

                // Check 3x3x3 neighborhood
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            long h = ((long)(cx + dx) * 73856093) ^ ((long)(cy + dy) * 19349663) ^ ((long)(cz + dz) * 83492791);
                            if (grid.TryGetValue(h, out var neighbors))
                            {
                                foreach (var p2 in neighbors)
                                {
                                    if (p1 == p2) continue;
                                    double distSq = (p1.Position - p2.Position).LengthSquared();
                                    if (distSq > 0.001 && distSq < minDistSq) // Ignore exact duplicates
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
                    double dist = Math.Sqrt(minDistSq);
                    lock (lockObj)
                    {
                        minTotalDist += dist;
                        validSamples++;
                    }
                }
            });

            float avgDistance = validSamples > 0 ? (float)(minTotalDist / validSamples) : 1.0f;
            Log($"[RENDER] Computed avg point distance: {avgDistance:F4}");

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Viewport.LoadData(masterPoints.ToArray(), masterMisses.ToArray(), avgDistance);
                _isDbLoaded = true;
            });
        });
    }

    private async void BtnMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorTask != null)
        {
            _monitorCts?.Cancel();
            Log("[MONITOR] Stopping...");
            try { await _monitorTask; } catch { }
            _monitorTask = null;
            _monitorCts?.Dispose();
            _monitorCts = null;

            BtnMonitor.Content = "Start Monitor";
            BtnUpdate.IsEnabled = true;
            BtnMesh.IsEnabled = true;
            BtnBrowseDb.IsEnabled = true;
            return;
        }

        if (string.IsNullOrEmpty(_dbPath))
        {
            Log("[ERROR] Select DB file first.");
            return;
        }

        BtnMonitor.Content = "Stop Monitor";
        BtnUpdate.IsEnabled = false;
        BtnMesh.IsEnabled = false;
        BtnBrowseDb.IsEnabled = false;

        if (!_isDbLoaded)
        {
            try
            {
                await LoadDbAsync();
            }
            catch (Exception ex)
            {
                Log($"[ERROR] {ex.Message}");
                BtnMonitor.Content = "Start Monitor";
                BtnUpdate.IsEnabled = true;
                BtnMesh.IsEnabled = true;
                BtnBrowseDb.IsEnabled = true;
                return;
            }
        }

        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;

        var options = new MonitorRunOptions
        {
            StartAtEnd = true,
            IncludeSnapshots = false,
            Log = Log,
            OnUpdate = update =>
            {
                if (update.NewPoints != null || update.NewMisses != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        Viewport.AppendData(update.NewPoints, update.NewMisses, update.AvgDistance);
                    });
                }
            }
        };

        _monitorTask = MonitorRunner.RunAsync(_logPath, _dbPath, token, options);
        _ = _monitorTask.ContinueWith(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                BtnMonitor.Content = "Start Monitor";
                BtnUpdate.IsEnabled = true;
                BtnMesh.IsEnabled = true;
                BtnBrowseDb.IsEnabled = true;
            });

            _monitorTask = null;
            _monitorCts?.Dispose();
            _monitorCts = null;
        });
    }

    private void RenderSettings_Changed(object? sender, RoutedEventArgs e)
    {
        Viewport.ShowPoints = ChkShowPoints.IsChecked ?? true;
        Viewport.ShowSurfels = ChkShowSurfels.IsChecked ?? true;
        Viewport.ShowRays = ChkShowRays.IsChecked ?? true;
        Viewport.Invalidate();
    }

    private void SldSurfelSize_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        Viewport.SurfelScale = (float)SldSurfelSize.Value;
        Viewport.Invalidate();
    }
}
