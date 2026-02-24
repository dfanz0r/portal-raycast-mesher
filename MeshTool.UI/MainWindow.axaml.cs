using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MeshTool.Core.Algorithms;
using MeshTool.Core.Config;
using MeshTool.Core.Data;
using MeshTool.Core.IO;
using MeshTool.UI.Models;

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
    private System.Collections.Generic.List<MeshTool.Core.Data.Triangle>? _cachedMesh = null;
    private bool _meshGenerationInProgress = false;
    private bool _scanHandleDragging = false;
    private string _scanHandleKey = string.Empty;
    private Point _scanHandleLastPoint;
    private bool _isSyncingScanControls = false;
    private bool _scanBoundsEditingEnabled = true;
    private bool _monitorRenderOverridesActive = false;
    private int _selectedPointCount = 0;
    private readonly System.Collections.Generic.List<Vertex> _loadedPoints = new();
    private readonly System.Collections.Generic.List<Ray> _loadedMisses = new();
    private readonly System.Collections.Generic.List<Vertex> _selectionOriginalPoints = new();
    private readonly System.Collections.Generic.List<Vertex> _selectionWorkingPoints = new();
    private bool _selectionHasPendingChanges = false;
    private float _loadedAvgDistance = 1.0f;
    private bool _preMonitorShowPoints;
    private bool _preMonitorShowSurfels;
    private bool _preMonitorShowMissRays;
    private bool _preMonitorShowDensityPreview;
    private bool _preMonitorShowVolume;
    private const float MinProbeCell = 64f;
    private const float MaxProbeCell = 768f;
    private const float MinFineStep = 8f;
    private const float MaxFineStep = 96f;
    private float _finePhaseTargetStep = 24f;

    public MainWindow()
    {
        InitializeComponent();

        // Initialize logger with file output
        InitializeLogger();

        Viewport.OnLog = Log;
        Viewport.OnHoveredCoordinateChanged = OnHoveredCoordinateChanged;
        Viewport.OnMoveSpeedChanged = OnMoveSpeedChanged;
        Viewport.OnScanVolumeChanged = OnScanVolumeChanged;
        Viewport.OnSelectionCountChanged = OnSelectionCountChanged;
        Viewport.OnDeleteSelectionRequested = OnDeleteSelectionRequested;
        Viewport.OnToggleSelectionModeRequested = OnToggleSelectionModeRequested;
        LstConsole.ItemsSource = _logLines;
        CmbDbPath.ItemsSource = _dbFiles;

        _logPath = ResolvePortalLogPath();

        LoadLocalDbFiles();
        SyncScanControls(Viewport.ScanVolume);
        UpdateMeshUiState();
        ApplyInteractionMode();
    }

    private void InitializeLogger()
    {
        // Subscribe to logger events for UI updates
        Logger.LogAdded += OnLoggerMessage;

        // Enable file logging
        if (Logger.EnableFileLogging())
        {
            Logger.Info("MeshTool UI started");
            Logger.Info($"Log file: {Logger.LogFilePath}");
        }
    }

    private void OnLoggerMessage(object? sender, LogEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _logLines.Add(e.Message);
            while (_logLines.Count > 200)
            {
                _logLines.RemoveAt(0);
            }

            // Update console count
            if (TxtConsoleCount != null)
            {
                TxtConsoleCount.Text = _logLines.Count > 0 ? $"{_logLines.Count} lines" : string.Empty;
            }

            if (_logLines.Count == 0 || !LstConsole.IsVisible)
            {
                return;
            }

            // Defer scrolling to avoid arrange-time virtualization crashes.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_logLines.Count > 0)
                    {
                        LstConsole.ScrollIntoView(_logLines[^1]);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Layout may still be in progress; safe to skip this frame.
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        });
    }

    private void UpdateMeshUiState()
    {
        bool hasMesh = _cachedMesh != null;
        if (ChkShowMesh != null)
        {
            ChkShowMesh.IsEnabled = hasMesh;
            if (!hasMesh)
            {
                ChkShowMesh.IsChecked = false;
            }
        }

        if (BtnSaveMesh != null)
        {
            BtnSaveMesh.IsEnabled = hasMesh;
        }
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

    private void OnScanVolumeChanged(ScanVolumeSettings settings)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => SyncScanControls(settings));
    }

    private void OnSelectionCountChanged(int count)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _selectedPointCount = count;
            TxtSelectedPoints.Text = $"Selected: {count}";
            ApplyInteractionMode();
        });
    }

    private void SyncScanControls(ScanVolumeSettings s)
    {
        _isSyncingScanControls = true;
        TxtScanCenterX.Text = s.CenterX.ToString("F1", CultureInfo.InvariantCulture);
        TxtScanCenterZ.Text = s.CenterZ.ToString("F1", CultureInfo.InvariantCulture);
        TxtScanSizeX.Text = s.SizeX.ToString("F1", CultureInfo.InvariantCulture);
        TxtScanSizeZ.Text = s.SizeZ.ToString("F1", CultureInfo.InvariantCulture);
        TxtScanYTop.Text = s.YTop.ToString("F1", CultureInfo.InvariantCulture);
        TxtScanYBottom.Text = s.YBottom.ToString("F1", CultureInfo.InvariantCulture);
        TxtScanYaw.Text = s.YawDegrees.ToString("F1", CultureInfo.InvariantCulture);
        TxtScanRayTilt.Text = s.RayTiltDegrees.ToString("F1", CultureInfo.InvariantCulture);
        SldScanDensity.Value = CellToDensity(s.ProbeCellSize);
        SldFineDensity.Value = FineStepToDensity(_finePhaseTargetStep);
        UpdateDensityMetersLabels(s.ProbeCellSize, _finePhaseTargetStep);
        Viewport.ScanFineTargetStep = _finePhaseTargetStep;
        _isSyncingScanControls = false;
    }

    private void UpdateDensityMetersLabels(float broadCellMeters, float fineStepMeters)
    {
        TxtBroadDensityMeters.Text = $"{MathF.Round(broadCellMeters)} m";
        TxtFineDensityMeters.Text = $"{MathF.Round(fineStepMeters)} m";
    }

    private static double CellToDensity(float cell)
    {
        float clamped = Math.Clamp(cell, MinProbeCell, MaxProbeCell);
        float t = (clamped - MinProbeCell) / (MaxProbeCell - MinProbeCell);
        return 1.0 - t;
    }

    private static float DensityToCell(double density)
    {
        float d = (float)Math.Clamp(density, 0.0, 1.0);
        float t = 1.0f - d;
        return MinProbeCell + ((MaxProbeCell - MinProbeCell) * t);
    }

    private static double FineStepToDensity(float step)
    {
        float clamped = Math.Clamp(step, MinFineStep, MaxFineStep);
        float t = (clamped - MinFineStep) / (MaxFineStep - MinFineStep);
        return 1.0 - t;
    }

    private static float DensityToFineStep(double density)
    {
        float d = (float)Math.Clamp(density, 0.0, 1.0);
        float t = 1.0f - d;
        return MinFineStep + ((MaxFineStep - MinFineStep) * t);
    }

    private static bool TryParseFloat(TextBox box, out float value)
    {
        return float.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private bool TryReadScanSettingsFromUi(out ScanVolumeSettings settings, bool logErrors = true)
    {
        settings = Viewport.ScanVolume;

        if (!TryParseFloat(TxtScanCenterX, out var cx) ||
            !TryParseFloat(TxtScanCenterZ, out var cz) ||
            !TryParseFloat(TxtScanSizeX, out var sx) ||
            !TryParseFloat(TxtScanSizeZ, out var sz) ||
            !TryParseFloat(TxtScanYTop, out var yTop) ||
            !TryParseFloat(TxtScanYBottom, out var yBottom) ||
            !TryParseFloat(TxtScanYaw, out var yaw) ||
            !TryParseFloat(TxtScanRayTilt, out var tilt))
        {
            if (logErrors)
            {
                Log("[ERROR] Invalid scan volume values. Use numeric values only.");
            }
            return false;
        }

        float cell = Viewport.ScanVolume.ProbeCellSize;
        settings = new ScanVolumeSettings(cx, cz, sx, sz, yTop, yBottom, yaw, tilt, cell).Sanitize();
        return true;
    }

    private void ScanVolumeField_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_scanBoundsEditingEnabled) return;
        if (_isSyncingScanControls) return;
        if (!TryReadScanSettingsFromUi(out var settings, logErrors: false)) return;
        Viewport.SetScanVolume(settings);
    }

    private void SldScanDensity_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_scanBoundsEditingEnabled) return;
        if (_isSyncingScanControls) return;
        var s = Viewport.ScanVolume;
        s = s with { ProbeCellSize = DensityToCell(e.NewValue) };
        Viewport.SetScanVolume(s);
        SyncScanControls(s);
    }

    private void SldFineDensity_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_scanBoundsEditingEnabled) return;
        if (_isSyncingScanControls) return;
        _finePhaseTargetStep = DensityToFineStep(e.NewValue);
        UpdateDensityMetersLabels(Viewport.ScanVolume.ProbeCellSize, _finePhaseTargetStep);
        Viewport.ScanFineTargetStep = _finePhaseTargetStep;
        Viewport.Invalidate();
    }

    private void BtnResetScanVolume_Click(object? sender, RoutedEventArgs e)
    {
        if (!_scanBoundsEditingEnabled) return;
        var defaults = ScanVolumeSettings.Default;
        Viewport.SetScanVolume(defaults);
        Viewport.ReframeToScanVolume();
        SyncScanControls(defaults);
        Log("[SCAN] Volume reset to defaults (12k x 12k).");
    }

    private void ScanHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_scanBoundsEditingEnabled) return;
        if (sender is not Border border) return;
        if (border.Tag is not string key) return;

        _scanHandleDragging = true;
        _scanHandleKey = key;
        _scanHandleLastPoint = e.GetPosition(this);
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void ScanHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_scanBoundsEditingEnabled) return;
        if (!_scanHandleDragging || sender is not Border border) return;

        var current = e.GetPosition(this);
        double dx = current.X - _scanHandleLastPoint.X;
        if (Math.Abs(dx) < 0.01) return;

        bool fine = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool coarse = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        float unit = GetScanHandleStep(_scanHandleKey, fine, coarse);
        float delta = (float)(dx * unit);

        var s = Viewport.ScanVolume;
        switch (_scanHandleKey)
        {
            case "CenterX": s = s with { CenterX = s.CenterX + delta }; break;
            case "CenterZ": s = s with { CenterZ = s.CenterZ + delta }; break;
            case "SizeX": s = s with { SizeX = MathF.Max(10f, s.SizeX + delta) }; break;
            case "SizeZ": s = s with { SizeZ = MathF.Max(10f, s.SizeZ + delta) }; break;
            case "YTop": s = s with { YTop = s.YTop + delta }; break;
            case "YBottom": s = s with { YBottom = s.YBottom + delta }; break;
            case "Yaw": s = s with { YawDegrees = s.YawDegrees + delta }; break;
            case "RayTilt": s = s with { RayTiltDegrees = s.RayTiltDegrees + delta }; break;
            case "ProbeCell": s = s with { ProbeCellSize = MathF.Max(8f, s.ProbeCellSize + delta) }; break;
        }

        s = s.Sanitize();
        Viewport.SetScanVolume(s);
        SyncScanControls(s);

        _scanHandleLastPoint = current;
        e.Handled = true;
    }

    private void ScanHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_scanBoundsEditingEnabled) return;
        _scanHandleDragging = false;
        _scanHandleKey = string.Empty;
        if (sender is Border border && e.Pointer.Captured == border)
        {
            e.Pointer.Capture(null);
        }
        e.Handled = true;
    }

    private static float GetScanHandleStep(string key, bool fine, bool coarse)
    {
        float baseStep = key switch
        {
            "CenterX" or "CenterZ" => 1.0f,
            "SizeX" or "SizeZ" => 2.0f,
            "YTop" or "YBottom" => 1.0f,
            "Yaw" => 0.2f,
            "RayTilt" => 0.1f,
            "ProbeCell" => 0.25f,
            _ => 1.0f
        };

        if (fine) baseStep *= 0.1f;
        if (coarse) baseStep *= 10f;
        return baseStep;
    }

    private async void BtnExportScanTs_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryReadScanSettingsFromUi(out var settings)) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        try
        {
            string template = LoadEmbeddedScanTemplate();
            string generated = GenerateScanScript(template, settings);

            if (topLevel.Clipboard == null)
            {
                Log("[ERROR] Clipboard not available.");
                return;
            }

            await topLevel.Clipboard.SetTextAsync(generated);
            Log("[SCAN] Raycast script copied to clipboard.");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to copy raycast script: {ex.Message}");
        }
    }

    private string GenerateScanScript(string template, ScanVolumeSettings s)
    {
        string output = template;
        float halfX = s.SizeX * 0.5f;
        float halfZ = s.SizeZ * 0.5f;
        float maxHalf = MathF.Max(halfX, halfZ);

        var tokens = new System.Collections.Generic.Dictionary<string, string>
        {
            ["MAP_CENTER_X"] = s.CenterX.ToString("0.###", CultureInfo.InvariantCulture),
            ["MAP_CENTER_Z"] = s.CenterZ.ToString("0.###", CultureInfo.InvariantCulture),
            ["MAP_HALF_SIZE_X"] = halfX.ToString("0.###", CultureInfo.InvariantCulture),
            ["MAP_HALF_SIZE_Z"] = halfZ.ToString("0.###", CultureInfo.InvariantCulture),
            ["SCAN_YAW_DEG"] = s.YawDegrees.ToString("0.###", CultureInfo.InvariantCulture),
            ["SCAN_TILT_DEG"] = s.RayTiltDegrees.ToString("0.###", CultureInfo.InvariantCulture),
            ["Y_TOP"] = s.YTop.ToString("0.###", CultureInfo.InvariantCulture),
            ["Y_BOTTOM"] = s.YBottom.ToString("0.###", CultureInfo.InvariantCulture),
            ["INITIAL_PROBE_CELL_SIZE"] = s.ProbeCellSize.ToString("0.###", CultureInfo.InvariantCulture),
            ["INITIAL_PROBE_RADIUS"] = maxHalf.ToString("0.###", CultureInfo.InvariantCulture),
            ["TARGET_STEP"] = MathF.Round(_finePhaseTargetStep).ToString("0", CultureInfo.InvariantCulture)
        };

        foreach (var kv in tokens)
        {
            output = output.Replace($"{{{{{kv.Key}}}}}", kv.Value, StringComparison.Ordinal);
        }

        if (output.Contains("{{", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("scan.ts.template contains unresolved tokens.");
        }

        return output.TrimEnd() + Environment.NewLine;
    }

    private static string LoadEmbeddedScanTemplate()
    {
        var uri = new Uri("avares://MeshTool.UI/scan.ts.template");
        if (!AssetLoader.Exists(uri))
        {
            throw new InvalidOperationException("Embedded resource 'scan.ts.template' not found.");
        }

        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
            UpdateMeshUiState();
            Viewport?.LoadMesh(null);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitorCts?.Cancel();
        Logger.Info("MeshTool UI closed");
        Logger.Shutdown();
        base.OnClosed(e);
    }

    private void Log(string message)
    {
        // Use the Logger service for all logging
        Logger.Info(message);
    }

    private void BtnToggleConsole_Click(object? sender, RoutedEventArgs e)
    {
        if (ConsolePanel != null)
        {
            ConsolePanel.IsVisible = BtnToggleConsole.IsChecked == true;
        }
    }

    private void BtnClearConsole_Click(object? sender, RoutedEventArgs e)
    {
        _logLines.Clear();
        Logger.ClearRecentLogs();
        if (TxtConsoleCount != null)
        {
            TxtConsoleCount.Text = string.Empty;
        }
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
            string displayEntry = GetDbDisplayEntry(_dbPath);
            if (!_dbFiles.Contains(displayEntry))
            {
                _dbFiles.Add(displayEntry);
            }
            CmbDbPath.SelectedItem = displayEntry;
            _isDbLoaded = false;
            _cachedMesh = null;
            UpdateMeshUiState();
            Viewport?.LoadMesh(null);
        }
    }

    private async void BtnNewDb_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorTask != null)
        {
            Log("[ERROR] Stop monitor before creating a new DB.");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        string suggested = $"map_{DateTime.Now:yyyyMMdd_HHmmss}.db";
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create New Database",
            DefaultExtension = ".db",
            SuggestedFileName = suggested,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Database Files") { Patterns = new[] { "*.db" } }
            }
        });

        if (file == null) return;

        try
        {
            string path = file.Path.LocalPath;
            DatabaseIO.SaveDatabase(Array.Empty<Vertex>(), Array.Empty<Ray>(), path);

            string displayEntry = GetDbDisplayEntry(path);
            if (!_dbFiles.Contains(displayEntry))
            {
                _dbFiles.Add(displayEntry);
            }

            _dbPath = path;
            CmbDbPath.SelectedItem = displayEntry;
            _cachedMesh = null;
            _isDbLoaded = true;
            UpdateMeshUiState();
            Viewport.LoadMesh(null);
            Viewport.LoadData(Array.Empty<Vertex>(), Array.Empty<Ray>(), 1.0f, resetCamera: false);
            Log($"[DB] Created new database: {path}");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to create DB: {ex.Message}");
        }
    }

    private async void BtnClearDb_Click(object? sender, RoutedEventArgs e)
    {
        if (_monitorTask != null)
        {
            Log("[ERROR] Stop monitor before clearing the DB.");
            return;
        }

        if (string.IsNullOrEmpty(_dbPath))
        {
            Log("[ERROR] Select DB file first.");
            return;
        }

        if (!File.Exists(_dbPath))
        {
            Log($"[ERROR] DB file not found: {_dbPath}");
            return;
        }

        bool confirmed = await ConfirmClearDatabaseAsync();
        if (!confirmed)
        {
            Log("[DB] Clear cancelled.");
            return;
        }

        try
        {
            DatabaseIO.SaveDatabase(Array.Empty<Vertex>(), Array.Empty<Ray>(), _dbPath);
            _cachedMesh = null;
            _isDbLoaded = true;
            UpdateMeshUiState();
            Viewport.LoadMesh(null);
            Viewport.LoadData(Array.Empty<Vertex>(), Array.Empty<Ray>(), 1.0f, resetCamera: false);
            Log($"[DB] Cleared database: {_dbPath}");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to clear DB: {ex.Message}");
        }
    }

    private void ChkPointSelectMode_Changed(object? sender, RoutedEventArgs e)
    {
        if ((ChkPointSelectMode.IsChecked ?? false) && !_isDbLoaded)
        {
            Log("[SELECT] Load a DB before entering selection mode.");
            ChkPointSelectMode.IsChecked = false;
        }

        if (ChkPointSelectMode.IsChecked == true)
        {
            BeginSelectionSessionFromLoadedData();
        }

        ApplyInteractionMode();
    }

    private void BtnClearSelectedPoints_Click(object? sender, RoutedEventArgs e)
    {
        Viewport.ClearPointSelection();
        _selectedPointCount = 0;
        TxtSelectedPoints.Text = "Selected: 0";
        ApplyInteractionMode();
    }

    private void OnDeleteSelectionRequested()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => BtnDeleteSelectedPoints_Click(this, new RoutedEventArgs()));
    }

    private void OnToggleSelectionModeRequested()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_monitorTask != null)
            {
                return;
            }

            bool next = !(ChkPointSelectMode.IsChecked ?? false);
            ChkPointSelectMode.IsChecked = next;
            ChkPointSelectMode_Changed(this, new RoutedEventArgs());
        });
    }

    private void BeginSelectionSessionFromLoadedData()
    {
        _selectionOriginalPoints.Clear();
        _selectionOriginalPoints.AddRange(_loadedPoints);
        _selectionWorkingPoints.Clear();
        _selectionWorkingPoints.AddRange(_loadedPoints);
        _selectionHasPendingChanges = false;
    }

    private static (long, long, long) QuantizedPointKey(double x, double y, double z)
    {
        return ((long)Math.Round(x * 10000.0), (long)Math.Round(y * 10000.0), (long)Math.Round(z * 10000.0));
    }

    private void BtnDeleteSelectedPoints_Click(object? sender, RoutedEventArgs e)
    {
        if (_monitorTask != null)
        {
            Log("[ERROR] Stop monitor before deleting selected points.");
            return;
        }

        if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath))
        {
            Log("[ERROR] Select a valid DB file first.");
            return;
        }

        if (!(ChkPointSelectMode.IsChecked ?? false))
        {
            Log("[SELECT] Enable Point Selection Mode first.");
            return;
        }

        var selectedIndices = Viewport.GetSelectedPointIndices();
        if (selectedIndices.Length == 0)
        {
            Log("[SELECT] No points selected.");
            return;
        }

        try
        {
            int removed = 0;
            var selectedSet = new System.Collections.Generic.HashSet<int>(selectedIndices.Length);
            for (int i = 0; i < selectedIndices.Length; i++)
            {
                selectedSet.Add(selectedIndices[i]);
            }

            var kept = new System.Collections.Generic.List<Vertex>(_selectionWorkingPoints.Count);
            for (int i = 0; i < _selectionWorkingPoints.Count; i++)
            {
                if (selectedSet.Contains(i)) removed++;
                else kept.Add(_selectionWorkingPoints[i]);
            }

            _selectionWorkingPoints.Clear();
            _selectionWorkingPoints.AddRange(kept);
            _selectionHasPendingChanges = true;

            Viewport.ClearPointSelection();
            _selectedPointCount = 0;
            TxtSelectedPoints.Text = "Selected: 0";
            _cachedMesh = null;
            UpdateMeshUiState();
            Viewport.LoadMesh(null);
            Viewport.LoadData(_selectionWorkingPoints.ToArray(), _loadedMisses.ToArray(), _loadedAvgDistance, resetCamera: false);
            Viewport.ClearPointSelection();
            _isDbLoaded = true;
            Log($"[SELECT] Removed {removed} points from working set (not saved yet).");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed deleting selected points: {ex.Message}");
        }
        finally
        {
            ApplyInteractionMode();
        }
    }

    private async void BtnSelectionSave_Click(object? sender, RoutedEventArgs e)
    {
        if (!_selectionHasPendingChanges)
        {
            return;
        }

        try
        {
            BtnSelectionSave.IsEnabled = false;
            await Task.Run(() => DatabaseIO.SaveDatabase(_selectionWorkingPoints, _loadedMisses, _dbPath));

            _loadedPoints.Clear();
            _loadedPoints.AddRange(_selectionWorkingPoints);
            _selectionOriginalPoints.Clear();
            _selectionOriginalPoints.AddRange(_selectionWorkingPoints);
            _selectionHasPendingChanges = false;
            Log($"[SELECT] Saved {_selectionWorkingPoints.Count} points to {_dbPath}");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed saving selection edits: {ex.Message}");
        }
        finally
        {
            ApplyInteractionMode();
        }
    }

    private void BtnSelectionDiscard_Click(object? sender, RoutedEventArgs e)
    {
        if (!_selectionHasPendingChanges)
        {
            return;
        }

        _selectionWorkingPoints.Clear();
        _selectionWorkingPoints.AddRange(_selectionOriginalPoints);
        _selectionHasPendingChanges = false;
        Viewport.ClearPointSelection();
        _selectedPointCount = 0;
        TxtSelectedPoints.Text = "Selected: 0";
        Viewport.LoadData(_selectionWorkingPoints.ToArray(), _loadedMisses.ToArray(), _loadedAvgDistance, resetCamera: false);
        _isDbLoaded = true;
        Log("[SELECT] Discarded unsaved selection edits.");
        ApplyInteractionMode();
    }

    private void ApplyInteractionMode()
    {
        bool monitorActive = _monitorTask != null;
        bool selectionMode = !monitorActive && (ChkPointSelectMode.IsChecked ?? false);

        Viewport.PointSelectionModeEnabled = selectionMode;
        SetScanEditingEnabled(!monitorActive && !selectionMode);

        ChkPointSelectMode.IsEnabled = !monitorActive;
        BtnDeleteSelectedPoints.IsEnabled = selectionMode && _selectedPointCount > 0;
        BtnClearSelectedPoints.IsEnabled = selectionMode && _selectedPointCount > 0;
        BtnSelectionSave.IsEnabled = !monitorActive && _selectionHasPendingChanges;
        BtnSelectionDiscard.IsEnabled = !monitorActive && _selectionHasPendingChanges;
    }

    private async Task<bool> ConfirmClearDatabaseAsync()
    {
        var dialog = new Window
        {
            Width = 460,
            Height = 180,
            CanResize = false,
            Title = "Confirm Clear Database",
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var result = false;

        var yesButton = new Button
        {
            Content = "Yes, Clear Database",
            MinWidth = 150
        };
        yesButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 90
        };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "This will permanently remove all points and rays and overwrite the selected DB file.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = "Are you sure you want to continue?",
                    FontSize = 13
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children =
                    {
                        cancelButton,
                        yesButton
                    }
                }
            }
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private static string GetDbDisplayEntry(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string cwd = Path.GetFullPath(Environment.CurrentDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string dir = Path.GetDirectoryName(fullPath) ?? string.Empty;
        dir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(dir, cwd, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(fullPath);
        }

        return fullPath;
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

        await GenerateMeshFromDbAsync();
    }

    private async void BtnSaveMesh_Click(object sender, RoutedEventArgs e)
    {
        if (_cachedMesh == null)
        {
            Log("[MESH] Generate mesh first before saving.");
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
        BtnSaveMesh.IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                DatabaseIO.LoadDatabase(_dbPath, out var masterPoints, out _);
                if (outPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                {
                    GlbExporter.ExportGlb(_cachedMesh, outPath);
                }
                else if (outPath.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[EXPORT] Generating XYZ point cloud for {masterPoints.Count} points...");
                    XyzExporter.ExportXyz(masterPoints, outPath);
                }
                else
                {
                    ObjExporter.ExportObj(masterPoints, _cachedMesh, outPath);
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
            BtnSaveMesh.IsEnabled = _cachedMesh != null;
        }
    }

    private async Task GenerateMeshFromDbAsync()
    {
        _meshGenerationInProgress = true;
        // Always show mesh during generation and keep preview visible after completion.
        ChkShowMesh.IsChecked = true;
        Viewport.ShowMesh = true;

        BtnGenerateMesh.IsEnabled = false;
        ChkShowMesh.IsEnabled = false;
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

                var allTriangles = DelaunayMesher.GenerateMesh(masterPoints, (buffer, vertexCount) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        Viewport.LoadMeshRaw(buffer, vertexCount);
                        Viewport.ShowMesh = true;
                    });
                }, () => Viewport.IsMeshUpdatePending);

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
                    var meshBounds = new MeshTool.Core.Data.Bounds { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ, MinY = minY, MaxY = maxY };
                    var quadtree = MeshTool.Core.Data.TriangleQuadtree.Build(allTriangles, meshBounds);

                    Log($"[CARVE] Raycasting {masterMisses.Count} miss rays against the mesh...");
                    int removed = SpaceCarver.CarveMesh(quadtree, masterMisses);
                    Log($"[CARVE] Pruned {removed} triangles intersecting empty space.");

                    allTriangles = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(allTriangles, t => !t.IsDeleted));
                }

                int removedBoundary;
                allTriangles = DelaunayMesher.CullWeakBoundaryTriangles(
                    allTriangles,
                    masterPoints,
                    Settings.BOUNDARY_EDGE_SPACING_MULTIPLIER,
                    Settings.BOUNDARY_MIN_NORMALIZED_SUPPORT,
                    Settings.BOUNDARY_HEIGHT_TOL_MULTIPLIER,
                    Settings.BOUNDARY_MIN_HEIGHT_TOL,
                    out removedBoundary);
                if (removedBoundary > 0)
                {
                    Log($"[MESH] Removed {removedBoundary} weak boundary triangles.");
                }

                Log($"[MESH] Final Triangle Count: {allTriangles.Count}");
                sw.Stop();
                Log($"[DONE] Total Processing Time: {sw.Elapsed.TotalSeconds:F2}s");

                _cachedMesh = allTriangles;
            });

            if (_cachedMesh != null && ChkShowMesh.IsChecked == true)
            {
                Viewport.LoadMesh(_cachedMesh);
                Viewport.ShowMesh = true;
            }
        }
        catch (Exception ex)
        {
            Log($"[ERROR] {ex.Message}");
        }
        finally
        {
            _meshGenerationInProgress = false;
            BtnGenerateMesh.IsEnabled = true;
            PrgProcessing.IsVisible = false;
            UpdateMeshUiState();
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

            // Loaded DB content should render as baseline data; only monitor-session additions animate as "new".
            for (int i = 0; i < masterPoints.Count; i++)
            {
                masterPoints[i].SpawnTime = 0f;
            }
            for (int i = 0; i < masterMisses.Count; i++)
            {
                var ray = masterMisses[i];
                ray.SpawnTime = 0f;
                masterMisses[i] = ray;
            }

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
            var grid = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<MeshTool.Core.Data.Vertex>>();

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
                    list = new System.Collections.Generic.List<MeshTool.Core.Data.Vertex>();
                    grid[h] = list;
                }
                list.Add(p);
            }

            // Sample points and find their nearest neighbor
            var rand = new Random(42);
            var samplePoints = new MeshTool.Core.Data.Vertex[sampleCount];
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
                _loadedPoints.Clear();
                _loadedPoints.AddRange(masterPoints);
                _loadedMisses.Clear();
                _loadedMisses.AddRange(masterMisses);
                _loadedAvgDistance = avgDistance;
                BeginSelectionSessionFromLoadedData();

                Viewport.LoadData(masterPoints.ToArray(), masterMisses.ToArray(), avgDistance);
                _isDbLoaded = true;
                ApplyInteractionMode();
            });
        });
    }

    private void SetScanEditingEnabled(bool enabled)
    {
        _scanBoundsEditingEnabled = enabled;
        Viewport.ScanVolumeEditEnabled = enabled;
        Viewport.ShowScanHandles = enabled;

        TxtScanCenterX.IsEnabled = enabled;
        TxtScanCenterZ.IsEnabled = enabled;
        TxtScanSizeX.IsEnabled = enabled;
        TxtScanSizeZ.IsEnabled = enabled;
        TxtScanYTop.IsEnabled = enabled;
        TxtScanYBottom.IsEnabled = enabled;
        TxtScanYaw.IsEnabled = enabled;
        TxtScanRayTilt.IsEnabled = enabled;
        SldScanDensity.IsEnabled = enabled;
        SldFineDensity.IsEnabled = enabled;
        BtnResetScanVolume.IsEnabled = enabled;

        Viewport.Invalidate();
    }

    private void ApplyRenderSettingsToViewport()
    {
        Viewport.ShowPoints = ChkShowPoints.IsChecked ?? true;
        Viewport.ShowSurfels = ChkShowSurfels.IsChecked ?? true;
        Viewport.ShowMissRays = ChkShowMissRays.IsChecked ?? true;
        Viewport.ShowNormalRays = ChkShowNormals.IsChecked ?? true;
        Viewport.ShowGrid = ChkShowGrid.IsChecked ?? true;
        Viewport.ShowScanDensityPreview = ChkShowDensityPreview.IsChecked ?? true;
        Viewport.ShowScanVolume = ChkShowVolume.IsChecked ?? true;
    }

    private void ApplyMonitorRenderOverrides(bool enabled)
    {
        if (enabled)
        {
            if (!_monitorRenderOverridesActive)
            {
                _preMonitorShowPoints = ChkShowPoints.IsChecked ?? true;
                _preMonitorShowSurfels = ChkShowSurfels.IsChecked ?? true;
                _preMonitorShowMissRays = ChkShowMissRays.IsChecked ?? false;
                _preMonitorShowDensityPreview = ChkShowDensityPreview.IsChecked ?? true;
                _preMonitorShowVolume = ChkShowVolume.IsChecked ?? true;
            }

            _monitorRenderOverridesActive = true;

            ChkShowPoints.IsChecked = true;
            ChkShowSurfels.IsChecked = true;
            ChkShowMissRays.IsChecked = true;
            ChkShowDensityPreview.IsChecked = false;
            ChkShowVolume.IsChecked = true;

            ChkShowPoints.IsEnabled = false;
            ChkShowSurfels.IsEnabled = false;
            ChkShowMissRays.IsEnabled = false;
            ChkShowDensityPreview.IsEnabled = false;
            ChkShowVolume.IsEnabled = false;

            ApplyRenderSettingsToViewport();
            Viewport.ShowScanHandles = false;
            Viewport.Invalidate();
            return;
        }

        if (!_monitorRenderOverridesActive)
        {
            return;
        }

        _monitorRenderOverridesActive = false;

        ChkShowPoints.IsEnabled = true;
        ChkShowSurfels.IsEnabled = true;
        ChkShowMissRays.IsEnabled = true;
        ChkShowDensityPreview.IsEnabled = true;
        ChkShowVolume.IsEnabled = true;

        ChkShowPoints.IsChecked = _preMonitorShowPoints;
        ChkShowSurfels.IsChecked = _preMonitorShowSurfels;
        ChkShowMissRays.IsChecked = _preMonitorShowMissRays;
        ChkShowDensityPreview.IsChecked = _preMonitorShowDensityPreview;
        ChkShowVolume.IsChecked = _preMonitorShowVolume;

        ApplyRenderSettingsToViewport();
        Viewport.Invalidate();
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
            BtnNewDb.IsEnabled = true;
            BtnClearDb.IsEnabled = true;
            BtnGenerateMesh.IsEnabled = true;
            UpdateMeshUiState();
            ApplyInteractionMode();
            ApplyMonitorRenderOverrides(false);
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
        BtnNewDb.IsEnabled = false;
        BtnClearDb.IsEnabled = false;
        BtnGenerateMesh.IsEnabled = false;
        BtnSaveMesh.IsEnabled = false;
        ChkShowMesh.IsChecked = false;
        ChkShowMesh.IsEnabled = false;
        Viewport.PointSelectionModeEnabled = false;
        SetScanEditingEnabled(false);
        ChkPointSelectMode.IsEnabled = false;
        BtnDeleteSelectedPoints.IsEnabled = false;
        BtnClearSelectedPoints.IsEnabled = false;
        BtnSelectionSave.IsEnabled = false;
        BtnSelectionDiscard.IsEnabled = false;
        ApplyMonitorRenderOverrides(true);

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
                BtnNewDb.IsEnabled = true;
                BtnClearDb.IsEnabled = true;
                BtnGenerateMesh.IsEnabled = true;
                UpdateMeshUiState();
                ApplyInteractionMode();
                ApplyMonitorRenderOverrides(false);
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
                BtnNewDb.IsEnabled = true;
                BtnClearDb.IsEnabled = true;
                BtnGenerateMesh.IsEnabled = true;
                UpdateMeshUiState();
                ApplyInteractionMode();
                ApplyMonitorRenderOverrides(false);
            });

            _monitorTask = null;
            _monitorCts?.Dispose();
            _monitorCts = null;
        });
    }

    private void RenderSettings_Changed(object? sender, RoutedEventArgs e)
    {
        ApplyRenderSettingsToViewport();
        Viewport.Invalidate();
    }

    private void ChkShowMesh_Changed(object? sender, RoutedEventArgs e)
    {
        bool showMesh = ChkShowMesh.IsChecked ?? false;
        Viewport.ShowMesh = showMesh;

        if (showMesh && _cachedMesh == null && !_meshGenerationInProgress)
        {
            Log("[MESH] Generate mesh first using 'Generate Mesh'.");
            ChkShowMesh.IsChecked = false;
            Viewport.LoadMesh(null);
            Viewport.Invalidate();
            return;
        }

        if (showMesh && _cachedMesh != null)
        {
            Viewport.LoadMesh(_cachedMesh);
        }
        else if (!showMesh)
        {
            Viewport.LoadMesh(null);
        }

        Viewport.Invalidate();
    }

    private void ChkDynamicColor_Changed(object? sender, RoutedEventArgs e)
    {
        Viewport.UseDynamicColorMapping = ChkDynamicColor.IsChecked ?? false;
        Viewport.Invalidate();
    }

    private void SldSurfelSize_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        Viewport.SurfelScale = (float)SldSurfelSize.Value;
        Viewport.Invalidate();
    }

}
