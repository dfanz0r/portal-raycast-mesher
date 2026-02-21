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
    private System.Collections.Generic.List<TerrainTool.Data.Triangle>? _cachedMesh = null;

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
            _cachedMesh = null;
            if (ChkShowMesh != null)
            {
                ChkShowMesh.IsChecked = false;
            }
            Viewport?.LoadMesh(null);
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
            _cachedMesh = null;
            if (ChkShowMesh != null)
            {
                ChkShowMesh.IsChecked = false;
            }
            Viewport?.LoadMesh(null);
        }
    }

    private async void BtnGenerateMesh_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorTask != null)
        {
            Log("[ERROR] Stop monitor before generating mesh.");
            return;
        }

        if (string.IsNullOrEmpty(_dbPath))
        {
            Log("[ERROR] Select DB file first.");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Mesh",
            DefaultExtension = ".obj",
            SuggestedFileName = Path.GetFileNameWithoutExtension(_dbPath) + ".obj",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("OBJ File") { Patterns = new[] { "*.obj" } },
                new FilePickerFileType("GLB File") { Patterns = new[] { "*.glb" } },
                new FilePickerFileType("XYZ Point Cloud") { Patterns = new[] { "*.xyz" } }
            }
        });

        if (file == null) return;

        string outPath = file.Path.LocalPath;

        BtnGenerateMesh.IsEnabled = false;
        PrgProcessing.IsVisible = true;
        try
        {
            await Task.Run(() =>
            {
                Log($"[DB] Loading {_dbPath}");
                DatabaseIO.LoadDatabase(_dbPath, out var masterPoints, out var masterMisses);

                if (masterPoints.Count < 3)
                {
                    Log("[ERROR] Not enough points to generate a mesh.");
                    return;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();

                Log("[MESH] Building Adaptive Mesh...");

                System.Collections.Generic.List<TerrainTool.Data.Triangle> allTriangles;
                allTriangles = DelaunayMesher.GenerateMesh(masterPoints, (progressTriangles) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (ChkShowMesh.IsChecked == true)
                        {
                            Viewport.LoadMesh(progressTriangles);
                        }
                    });
                });

                if (masterMisses.Count > 0)
                {
                    Log("[MESH] Building Triangle Quadtree for acceleration...");

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
                    var meshBounds = new TerrainTool.Data.Bounds { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ, MinY = minY, MaxY = maxY };

                    var quadtree = TerrainTool.Data.TriangleQuadtree.Build(allTriangles, meshBounds);

                    Log($"[CARVE] Raycasting {masterMisses.Count} miss rays against the mesh...");
                    int removed = SpaceCarver.CarveMesh(quadtree, masterMisses);
                    Log($"[CARVE] Pruned {removed} triangles intersecting empty space.");

                    allTriangles = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(allTriangles, t => !t.IsDeleted));
                }

                Log($"[MESH] Final Triangle Count: {allTriangles.Count}");
                sw.Stop();
                Log($"[DONE] Total Processing Time: {sw.Elapsed.TotalSeconds:F2}s");

                _cachedMesh = allTriangles;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (ChkShowMesh.IsChecked == true)
                    {
                        Viewport.LoadMesh(_cachedMesh);
                    }
                });

                if (outPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                {
                    GlbExporter.ExportGlb(allTriangles, outPath);
                }
                else if (outPath.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[EXPORT] Generating XYZ point cloud for {masterPoints.Count} points...");
                    XyzExporter.ExportXyz(masterPoints, outPath);
                }
                else
                {
                    ObjExporter.ExportObj(masterPoints, allTriangles, outPath);
                }
                Log($"[EXPORT] Saved to {outPath}");
            });
        }
        catch (Exception ex)
        {
            Log($"[ERROR] {ex.Message}");
        }
        finally
        {
            BtnGenerateMesh.IsEnabled = true;
            PrgProcessing.IsVisible = false;
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
            BtnMesh.IsEnabled = true;
            BtnBrowseDb.IsEnabled = true;
            ChkShowMesh.IsEnabled = true;
            return;
        }

        if (string.IsNullOrEmpty(_dbPath))
        {
            Log("[ERROR] Select DB file first.");
            return;
        }

        BtnMonitor.Content = "Stop Monitor";
        BtnMesh.IsEnabled = false;
        BtnBrowseDb.IsEnabled = false;
        ChkShowMesh.IsChecked = false;
        ChkShowMesh.IsEnabled = false;

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
                BtnMesh.IsEnabled = true;
                BtnBrowseDb.IsEnabled = true;
                ChkShowMesh.IsEnabled = true;
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
                BtnMesh.IsEnabled = true;
                BtnBrowseDb.IsEnabled = true;
                ChkShowMesh.IsEnabled = true;
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
        Viewport.ShowMissRays = ChkShowMissRays.IsChecked ?? true;
        Viewport.ShowNormalRays = ChkShowNormals.IsChecked ?? true;
        Viewport.ShowGrid = ChkShowGrid.IsChecked ?? true;
        Viewport.Invalidate();
    }

    private async void ChkShowMesh_Changed(object? sender, RoutedEventArgs e)
    {
        bool showMesh = ChkShowMesh.IsChecked ?? false;
        Viewport.ShowMesh = showMesh;

        if (showMesh && _cachedMesh == null && !string.IsNullOrEmpty(_dbPath))
        {
            ChkShowMesh.IsEnabled = false;
            PrgProcessing.IsVisible = true;
            try
            {
                await Task.Run(() =>
                {
                    Log($"[DB] Loading {_dbPath} for mesh preview...");
                    DatabaseIO.LoadDatabase(_dbPath, out var masterPoints, out var masterMisses);

                    if (masterPoints.Count < 3)
                    {
                        Log("[ERROR] Not enough points to generate a mesh.");
                        return;
                    }

                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    Log("[MESH] Building Adaptive Mesh...");

                    System.Collections.Generic.List<TerrainTool.Data.Triangle> allTriangles;
                    allTriangles = DelaunayMesher.GenerateMesh(masterPoints, (progressTriangles) =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (ChkShowMesh.IsChecked == true)
                            {
                                Viewport.LoadMesh(progressTriangles);
                            }
                        });
                    });

                    if (masterMisses.Count > 0)
                    {
                        Log("[MESH] Building Triangle Quadtree for acceleration...");

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
                        var meshBounds = new TerrainTool.Data.Bounds { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ, MinY = minY, MaxY = maxY };

                        var quadtree = TerrainTool.Data.TriangleQuadtree.Build(allTriangles, meshBounds);

                        Log($"[CARVE] Raycasting {masterMisses.Count} miss rays against the mesh...");
                        int removed = SpaceCarver.CarveMesh(quadtree, masterMisses);
                        Log($"[CARVE] Pruned {removed} triangles intersecting empty space.");

                        allTriangles = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(allTriangles, t => !t.IsDeleted));
                    }

                    Log($"[MESH] Final Triangle Count: {allTriangles.Count}");
                    sw.Stop();
                    Log($"[DONE] Total Processing Time: {sw.Elapsed.TotalSeconds:F2}s");

                    _cachedMesh = allTriangles;
                });

                if (_cachedMesh != null)
                {
                    Viewport.LoadMesh(_cachedMesh);
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] {ex.Message}");
            }
            finally
            {
                ChkShowMesh.IsEnabled = true;
                PrgProcessing.IsVisible = false;
            }
        }
        else if (showMesh && _cachedMesh != null)
        {
            Viewport.LoadMesh(_cachedMesh);
        }

        Viewport.Invalidate();
    }

    private void SldSurfelSize_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        Viewport.SurfelScale = (float)SldSurfelSize.Value;
        Viewport.Invalidate();
    }

}
