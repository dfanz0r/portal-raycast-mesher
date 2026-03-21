using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MeshTool.Core.Algorithms;
using MeshTool.Core.Config;
using MeshTool.Core.Data;
using MeshTool.Core.IO;
using MeshTool.UI.Controls;

namespace MeshTool.UI.Controllers;

public sealed class MeshWorkflowController
{
    private readonly OpenGlViewport _viewport;
    private readonly RenderSettingsPanel _renderSettingsPanel;
    private readonly ActionButtonsPanel _actionButtonsPanel;
    private readonly Action<string> _log;
    private readonly Func<string> _getDbPath;
    private readonly Func<bool> _isMonitorRunning;
    private readonly Func<IStorageProvider?> _getStorageProvider;

    public MeshWorkflowController(
        OpenGlViewport viewport,
        RenderSettingsPanel renderSettingsPanel,
        ActionButtonsPanel actionButtonsPanel,
        Func<string> getDbPath,
        Func<bool> isMonitorRunning,
        Func<IStorageProvider?> getStorageProvider,
        Action<string> log)
    {
        _viewport = viewport;
        _renderSettingsPanel = renderSettingsPanel;
        _actionButtonsPanel = actionButtonsPanel;
        _getDbPath = getDbPath;
        _isMonitorRunning = isMonitorRunning;
        _getStorageProvider = getStorageProvider;
        _log = log;
    }

    public List<Triangle>? CachedMesh { get; private set; }

    public bool IsGenerationInProgress { get; private set; }

    public void ClearMesh()
    {
        CachedMesh = null;
        _viewport.LoadMesh(null);
        UpdateMeshUiState();
    }

    public void UpdateMeshUiState()
    {
        bool hasMesh = CachedMesh != null;
        _renderSettingsPanel.ShowMeshCheckBox.IsEnabled = hasMesh;
        if (!hasMesh)
        {
            _renderSettingsPanel.ShowMeshCheckBox.IsChecked = false;
        }

        _actionButtonsPanel.SaveMeshButton.IsEnabled = hasMesh;
    }

    public async Task GenerateFromDatabaseAsync(string dbPath)
    {
        IsGenerationInProgress = true;
        _renderSettingsPanel.ShowMeshCheckBox.IsChecked = true;
        _viewport.ShowMesh = true;

        _actionButtonsPanel.GenerateMeshButton.IsEnabled = false;
        _renderSettingsPanel.ShowMeshCheckBox.IsEnabled = false;
        _actionButtonsPanel.ProcessingProgressBar.IsVisible = true;
        try
        {
            List<Triangle>? mesh = null;
            await Task.Run(() =>
            {
                _log($"[DB] Loading {dbPath}");
                DatabaseIO.LoadDatabase(dbPath, out var masterPoints, out var masterMisses);

                if (masterPoints.Count < 3)
                {
                    _log("[ERROR] Not enough points to generate a mesh.");
                    return;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                _log("[MESH] Building Adaptive Mesh...");

                var allTriangles = DelaunayMesher.GenerateMesh(masterPoints, (buffer, vertexCount) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _viewport.LoadMeshRaw(buffer, vertexCount);
                        _viewport.ShowMesh = true;
                    });
                }, () => _viewport.IsMeshUpdatePending);

                if (masterMisses.Count > 0)
                {
                    _log("[MESH] Building Triangle Quadtree for acceleration...");

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

                    var meshBounds = new Bounds { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ, MinY = minY, MaxY = maxY };
                    var quadtree = TriangleQuadtree.Build(allTriangles, meshBounds);

                    _log($"[CARVE] Raycasting {masterMisses.Count} miss rays against the mesh...");
                    int removed = SpaceCarver.CarveMesh(quadtree, masterMisses);
                    _log($"[CARVE] Pruned {removed} triangles intersecting empty space.");

                    allTriangles = allTriangles.Where(t => !t.IsDeleted).ToList();
                }

                allTriangles = DelaunayMesher.CullWeakBoundaryTriangles(
                    allTriangles,
                    masterPoints,
                    Settings.BOUNDARY_EDGE_SPACING_MULTIPLIER,
                    Settings.BOUNDARY_MIN_NORMALIZED_SUPPORT,
                    Settings.BOUNDARY_HEIGHT_TOL_MULTIPLIER,
                    Settings.BOUNDARY_MIN_HEIGHT_TOL,
                    out int removedBoundary);
                if (removedBoundary > 0)
                {
                    _log($"[MESH] Removed {removedBoundary} weak boundary triangles.");
                }

                _log($"[MESH] Final Triangle Count: {allTriangles.Count}");
                sw.Stop();
                _log($"[DONE] Total Processing Time: {sw.Elapsed.TotalSeconds:F2}s");
                mesh = allTriangles;
            });

            CachedMesh = mesh;
            if (CachedMesh != null && _renderSettingsPanel.ShowMeshCheckBox.IsChecked == true)
            {
                _viewport.LoadMesh(CachedMesh);
                _viewport.ShowMesh = true;
            }
        }
        finally
        {
            IsGenerationInProgress = false;
            _actionButtonsPanel.GenerateMeshButton.IsEnabled = true;
            _actionButtonsPanel.ProcessingProgressBar.IsVisible = false;
            UpdateMeshUiState();
        }
    }

    public async Task SaveAsync(string dbPath, IStorageProvider storageProvider)
    {
        if (CachedMesh == null)
        {
            _log("[MESH] Generate mesh first before saving.");
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Mesh",
            DefaultExtension = ".obj",
            SuggestedFileName = Path.GetFileNameWithoutExtension(dbPath) + ".obj",
            FileTypeChoices =
            [
                new FilePickerFileType("OBJ File") { Patterns = ["*.obj"] },
                new FilePickerFileType("GLB File") { Patterns = ["*.glb"] },
                new FilePickerFileType("XYZ Point Cloud") { Patterns = ["*.xyz"] }
            ]
        });

        if (file == null)
        {
            return;
        }

        string outPath = file.Path.LocalPath;
        _actionButtonsPanel.SaveMeshButton.IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                DatabaseIO.LoadDatabase(dbPath, out var masterPoints, out _);
                if (outPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                {
                    GlbExporter.ExportGlb(CachedMesh, outPath);
                }
                else if (outPath.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase))
                {
                    _log($"[EXPORT] Generating XYZ point cloud for {masterPoints.Count} points...");
                    XyzExporter.ExportXyz(masterPoints, outPath);
                }
                else
                {
                    ObjExporter.ExportObj(masterPoints, CachedMesh, outPath);
                }

                _log($"[EXPORT] Saved to {outPath}");
            });
        }
        finally
        {
            _actionButtonsPanel.SaveMeshButton.IsEnabled = CachedMesh != null;
        }
    }

    public async void OnGenerateRequested(object? sender, RoutedEventArgs e)
    {
        if (_isMonitorRunning())
        {
            _log("[ERROR] Stop monitor before generating mesh.");
            return;
        }

        string dbPath = _getDbPath();
        if (string.IsNullOrEmpty(dbPath))
        {
            _log("[ERROR] Select DB file first.");
            return;
        }

        try
        {
            await GenerateFromDatabaseAsync(dbPath);
        }
        catch (Exception ex)
        {
            _log($"[ERROR] {ex.Message}");
        }
    }

    public async void OnSaveRequested(object? sender, RoutedEventArgs e)
    {
        string dbPath = _getDbPath();
        if (CachedMesh == null)
        {
            _log("[MESH] Generate mesh first before saving.");
            return;
        }

        if (string.IsNullOrEmpty(dbPath))
        {
            _log("[ERROR] Select DB file first.");
            return;
        }

        var storageProvider = _getStorageProvider();
        if (storageProvider == null)
        {
            return;
        }

        try
        {
            await SaveAsync(dbPath, storageProvider);
        }
        catch (Exception ex)
        {
            _log($"[ERROR] {ex.Message}");
        }
    }

    public void OnMeshVisibilityChanged(object? sender, RoutedEventArgs e)
    {
        bool showMesh = _renderSettingsPanel.ShowMeshChecked;
        _viewport.ShowMesh = showMesh;

        if (showMesh && CachedMesh == null && !IsGenerationInProgress)
        {
            _log("[MESH] Generate mesh first using 'Generate Mesh'.");
            _renderSettingsPanel.ShowMeshChecked = false;
            _viewport.LoadMesh(null);
            _viewport.Invalidate();
            return;
        }

        if (showMesh && CachedMesh != null)
        {
            _viewport.LoadMesh(CachedMesh);
        }
        else if (!showMesh)
        {
            _viewport.LoadMesh(null);
        }

        _viewport.Invalidate();
    }
}
