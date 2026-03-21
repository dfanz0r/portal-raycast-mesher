using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Threading.Tasks;
using MeshTool.Core.Data;
using MeshTool.Core.IO;
using MeshTool.UI.Controllers;
using MeshTool.UI.Controls;
using MeshTool.UI.Models;

namespace MeshTool.UI;

public partial class MainWindow : Window
{
    private DatabasePanel _databasePanel = null!;
    private RenderSettingsPanel _renderSettingsPanel = null!;
    private ConsoleOutputPanel _consoleOutputPanel = null!;
    private ScanVolumePanel _scanVolumePanel = null!;
    private ActionButtonsPanel _actionButtonsPanel = null!;
    private StatusPanel _statusPanel = null!;

    private string _dbPath = string.Empty;
    private readonly string _logPath;
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _dbFiles = new();

    private bool _isDbLoaded = false;
    private bool _scanBoundsEditingEnabled = true;
    private int _selectedPointCount = 0;
    private readonly System.Collections.Generic.List<Vertex> _loadedPoints = new();
    private readonly System.Collections.Generic.List<Ray> _loadedMisses = new();
    private float _loadedAvgDistance = 1.0f;
    // Controllers
    private readonly DatabaseController _databaseController = new();
    private readonly DatabaseWorkflowController _databaseWorkflowController;
    private readonly DialogController _dialogController;
    private readonly LogPanelController _logPanelController;
    private readonly MeshWorkflowController _meshWorkflowController;
    private readonly MonitorWorkflowController _monitorWorkflowController;
    private readonly ScanVolumeController _scanVolumeController;
    private readonly ScanVolumeInteractionController _scanVolumeInteractionController;
    private readonly MonitorController _monitorController = new();
    private readonly SelectionManager _selectionManager = new();
    private readonly SelectionWorkflowController _selectionWorkflowController;
    private readonly ScanScriptExportController _scanScriptExportController;
    private readonly ViewportLoadController _viewportLoadController;

    public MainWindow()
    {
        InitializeComponent();
        InitializePanels();
        _logPanelController = new LogPanelController(_consoleOutputPanel, _statusPanel.ToggleConsoleButton);
        _dialogController = new DialogController();
        _meshWorkflowController = new MeshWorkflowController(
            Viewport,
            _renderSettingsPanel,
            _actionButtonsPanel,
            () => _dbPath,
            () => _monitorController.IsRunning,
            () => TopLevel.GetTopLevel(this)?.StorageProvider,
            Log);
        _databaseWorkflowController = new DatabaseWorkflowController(
            _databaseController,
            _dbFiles,
            Log,
            () => _meshWorkflowController.ClearMesh(),
            ClearLoadedViewportState,
            UpdateMeshUiState,
            (points, rays, avgDistance) => Viewport.LoadData(points, rays, avgDistance, resetCamera: false));
        _monitorWorkflowController = new MonitorWorkflowController(
            _monitorController,
            Viewport,
            _databasePanel,
            _renderSettingsPanel,
            _actionButtonsPanel,
            SetScanEditingEnabled,
            ApplyInteractionMode,
            UpdateMeshUiState,
            ApplyRenderSettingsToViewport,
            Log);
        _selectionWorkflowController = new SelectionWorkflowController(
            _selectionManager,
            _databaseController,
            Viewport,
            Log,
            () => _meshWorkflowController.ClearMesh(),
            UpdateMeshUiState,
            ApplyInteractionMode,
            () => _isDbLoaded = true,
            () => _dbPath,
            () => _monitorController.IsRunning,
            () => _databasePanel.PointSelectionModeCheckBox.IsChecked ?? false,
            () => _loadedMisses,
            () => _loadedAvgDistance,
            count => _selectedPointCount = count,
            text => _databasePanel.SelectedPointsTextBlock.Text = text);
        _viewportLoadController = new ViewportLoadController(
            _databaseController,
            _selectionWorkflowController,
            Viewport,
            ApplyInteractionMode,
            () => _dbPath,
            () => _monitorController.IsRunning,
            _databasePanel.SetLoadButtonEnabled,
            Log,
            _loadedPoints,
            _loadedMisses,
            avgDistance => _loadedAvgDistance = avgDistance,
            () => _isDbLoaded = true);
        _scanScriptExportController = new ScanScriptExportController();

        WirePanelEvents();
        _scanVolumeController = new ScanVolumeController(_scanVolumePanel);
        _scanVolumeInteractionController = new ScanVolumeInteractionController(
            _scanVolumeController,
            () => Viewport.ScanVolume,
            settings =>
            {
                Viewport.SetScanVolume(settings);
                SyncScanControls(settings);
            });

        InitializeLogger();

        WireViewportCallbacks();
        _databasePanel.DatabasePathComboBox.ItemsSource = _dbFiles;

        WireControllerEvents();

        _logPath = ResolvePortalLogPath();

        LoadLocalDbFiles();
        SyncScanControls(Viewport.ScanVolume);
        UpdateMeshUiState();
        ApplyInteractionMode();
    }

    private void InitializePanels()
    {
        _databasePanel = RequireControl<DatabasePanel>("DatabasePanel");
        _renderSettingsPanel = RequireControl<RenderSettingsPanel>("RenderPanel");
        _consoleOutputPanel = RequireControl<ConsoleOutputPanel>("ConsolePanel");
        _scanVolumePanel = RequireControl<ScanVolumePanel>("ScanPanel");
        _actionButtonsPanel = RequireControl<ActionButtonsPanel>("ActionPanel");
        _statusPanel = RequireControl<StatusPanel>("StatusPanel");
    }

    private T RequireControl<T>(string name) where T : Control
    {
        return this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} not found.");
    }

    private void WireViewportCallbacks()
    {
        Viewport.OnLog = Log;
        Viewport.OnHoveredCoordinateChanged = OnHoveredCoordinateChanged;
        Viewport.OnMoveSpeedChanged = OnMoveSpeedChanged;
        Viewport.OnScanVolumeChanged = OnScanVolumeChanged;
        Viewport.OnSelectionCountChanged = OnSelectionCountChanged;
        Viewport.OnDeleteSelectionRequested = OnDeleteSelectionRequested;
        Viewport.OnToggleSelectionModeRequested = OnToggleSelectionModeRequested;
    }

    private void WireControllerEvents()
    {
        _scanVolumeController.ScanVolumeChanged += settings => Viewport.SetScanVolume(settings);
        _scanVolumeController.FineDensityChanged += fineStep =>
        {
            Viewport.ScanFineTargetStep = fineStep;
            Viewport.Invalidate();
        };

        _monitorController.Error += message => Log($"[ERROR] {message}");
        _monitorController.Updated += update =>
        {
            if (update.NewPoints == null && update.NewMisses == null)
            {
                return;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Viewport.AppendData(update.NewPoints, update.NewMisses, update.AvgDistance);
            });
        };
    }

    private void WirePanelEvents()
    {
        _databasePanel.DatabasePathSelectionChanged += CmbDbPath_SelectionChanged;
        _databasePanel.BrowseRequested += BtnBrowseDb_Click;
        _databasePanel.LoadRequested += _viewportLoadController.OnLoadRequested;
        _databasePanel.ClearRequested += BtnClearDb_Click;
        _databasePanel.NewRequested += BtnNewDb_Click;
        _databasePanel.PointSelectionModeChanged += ChkPointSelectMode_Changed;
        _databasePanel.DeleteSelectionRequested += BtnDeleteSelectedPoints_Click;
        _databasePanel.ClearSelectionRequested += BtnClearSelectedPoints_Click;
        _databasePanel.SaveSelectionRequested += BtnSelectionSave_Click;
        _databasePanel.DiscardSelectionRequested += BtnSelectionDiscard_Click;

        _scanVolumePanel.ResetRequested += BtnResetScanVolume_Click;
        _scanVolumePanel.ExportScriptRequested += BtnExportScanTs_Click;
        _scanVolumePanel.HandlePointerPressed += ScanHandle_PointerPressed;
        _scanVolumePanel.HandlePointerMoved += ScanHandle_PointerMoved;
        _scanVolumePanel.HandlePointerReleased += ScanHandle_PointerReleased;

        _actionButtonsPanel.MonitorRequested += BtnMonitor_Click;
        _actionButtonsPanel.GenerateMeshRequested += _meshWorkflowController.OnGenerateRequested;
        _actionButtonsPanel.SaveMeshRequested += _meshWorkflowController.OnSaveRequested;

        _renderSettingsPanel.RenderSettingsChanged += RenderSettings_Changed;
        _renderSettingsPanel.MeshVisibilityChanged += _meshWorkflowController.OnMeshVisibilityChanged;
        _renderSettingsPanel.DynamicColorChanged += ChkDynamicColor_Changed;
        _renderSettingsPanel.SurfelSizeChanged += SldSurfelSize_ValueChanged;
    }

    private void InitializeLogger()
    {
        _logPanelController.AttachLogger();

        if (Logger.EnableFileLogging())
        {
            Logger.Info("MeshTool UI started");
            Logger.Info($"Log file: {Logger.LogFilePath}");
        }
    }

    private void UpdateMeshUiState()
    {
        _meshWorkflowController.UpdateMeshUiState();
    }

    private void OnMoveSpeedChanged(float speed)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _statusPanel.SetCameraSpeed(speed);
        });
    }

    private void OnHoveredCoordinateChanged(Silk.NET.Maths.Vector3D<float>? coord)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (coord.HasValue)
            {
                _statusPanel.SetHoveredCoordinate($"X: {coord.Value.X:F2}, Y: {coord.Value.Y:F2}, Z: {coord.Value.Z:F2}");
            }
            else
            {
                _statusPanel.SetHoveredCoordinate("X: ---, Y: ---, Z: ---");
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
            _databasePanel.SetSelectedPointsCount(count);
            ApplyInteractionMode();
        });
    }

    private void SyncScanControls(ScanVolumeSettings s)
    {
        _scanVolumeController.SyncToUi(s);
        Viewport.ScanFineTargetStep = _scanVolumeController.FinePhaseTargetStep;
    }

    private void BtnResetScanVolume_Click(object? sender, RoutedEventArgs e)
    {
        if (!_scanBoundsEditingEnabled) return;
        var defaults = ScanVolumeSettings.Default;
        Viewport.SetScanVolume(defaults);
        Viewport.ReframeToScanVolume();
        _scanVolumeController.SyncToUi(defaults);
        Viewport.ScanFineTargetStep = _scanVolumeController.FinePhaseTargetStep;
        Log("[SCAN] Volume reset to defaults (12k x 12k).");
    }

    private void ScanHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _scanVolumeInteractionController.OnPointerPressed(sender, e);
    }

    private void ScanHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        _scanVolumeInteractionController.OnPointerMoved(sender, e);
    }

    private void ScanHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _scanVolumeInteractionController.OnPointerReleased(sender, e);
    }

    private async void BtnExportScanTs_Click(object? sender, RoutedEventArgs e)
    {
        if (!_scanVolumeController.TryReadSettingsFromUi(out var settings))
        {
            Log("[ERROR] Invalid scan volume values. Use numeric values only.");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        try
        {
            await _scanScriptExportController.ExportToClipboardAsync(topLevel, settings, _scanVolumeController.FinePhaseTargetStep, Log);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to copy raycast script: {ex.Message}");
        }
    }

    private void LoadLocalDbFiles()
    {
        try
        {
            _databaseWorkflowController.LoadLocalDbFiles(_databasePanel.DatabasePathComboBox, Environment.CurrentDirectory);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to load local DB files: {ex.Message}");
        }
    }

    private void ClearLoadedViewportState()
    {
        _loadedPoints.Clear();
        _loadedMisses.Clear();
        _loadedAvgDistance = 1.0f;
        _selectedPointCount = 0;
        _databasePanel.ClearSelectedPointsCount();
    }

    private void CmbDbPath_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_databasePanel.SelectedDatabaseEntry is string selected)
        {
            _dbPath = _databaseWorkflowController.ResolveSelectedPath(selected, Environment.CurrentDirectory);
            _isDbLoaded = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_monitorController.IsRunning)
        {
            _ = _monitorController.StopAsync();
        }

        _logPanelController.DetachLogger();
        Logger.Info("MeshTool UI closed");
        Logger.Shutdown();
        base.OnClosed(e);
    }

    private void Log(string message)
    {
        // Use the Logger service for all logging
        Logger.Info(message);
    }

    private static string ResolvePortalLogPath()
    {
        return Path.Combine(Path.GetTempPath(), "Battlefieldâ„¢ 6", "PortalLog.txt");
    }

    private async void BtnBrowseDb_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var path = await _databaseWorkflowController.BrowseAsync(topLevel.StorageProvider, _databasePanel.DatabasePathComboBox, Environment.CurrentDirectory);
        if (path != null)
        {
            _dbPath = path;
            _isDbLoaded = false;
        }
    }

    private async void BtnNewDb_Click(object? sender, RoutedEventArgs e)
    {
        if (_monitorController.IsRunning)
        {
            Log("[ERROR] Stop monitor before creating a new DB.");
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        try
        {
            var path = await _databaseWorkflowController.CreateNewAsync(topLevel.StorageProvider, _databasePanel.DatabasePathComboBox, Environment.CurrentDirectory);
            if (path == null)
            {
                return;
            }

            _dbPath = path;
            _isDbLoaded = true;
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to create DB: {ex.Message}");
        }
    }

    private async void BtnClearDb_Click(object? sender, RoutedEventArgs e)
    {
        if (_monitorController.IsRunning)
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

        bool confirmed = await _dialogController.ConfirmClearDatabaseAsync(this);
        if (!confirmed)
        {
            Log("[DB] Clear cancelled.");
            return;
        }

        try
        {
            _databaseWorkflowController.Clear(_dbPath);
            _isDbLoaded = true;
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to clear DB: {ex.Message}");
        }
    }

    private void ChkPointSelectMode_Changed(object? sender, RoutedEventArgs e)
    {
        if (_databasePanel.IsSelectionModeEnabled && !_isDbLoaded)
        {
            Log("[SELECT] Load a DB before entering selection mode.");
            _databasePanel.IsSelectionModeEnabled = false;
        }

        if (_databasePanel.IsSelectionModeEnabled)
        {
            BeginSelectionSessionFromLoadedData();
        }

        ApplyInteractionMode();
    }

    private void BtnClearSelectedPoints_Click(object? sender, RoutedEventArgs e)
    {
        _selectionWorkflowController.ClearSelectionDisplay();
    }

    private void OnDeleteSelectionRequested()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => BtnDeleteSelectedPoints_Click(this, new RoutedEventArgs()));
    }

    private void OnToggleSelectionModeRequested()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_monitorController.IsRunning)
            {
                return;
            }

            bool next = !_databasePanel.IsSelectionModeEnabled;
            _databasePanel.IsSelectionModeEnabled = next;
        });
    }

    private void BeginSelectionSessionFromLoadedData()
    {
        _selectionWorkflowController.BeginSession(_loadedPoints);
    }

    private void BtnDeleteSelectedPoints_Click(object? sender, RoutedEventArgs e)
    {
        _selectionWorkflowController.DeleteSelectedPoints();
    }

    private async void BtnSelectionSave_Click(object? sender, RoutedEventArgs e)
    {
        _databasePanel.SaveSelectionButton.IsEnabled = false;
        await _selectionWorkflowController.SaveSelectionAsync(_loadedPoints);
    }

    private void BtnSelectionDiscard_Click(object? sender, RoutedEventArgs e)
    {
        _selectionWorkflowController.DiscardSelectionChanges();
    }

    private void ApplyInteractionMode()
    {
        bool monitorActive = _monitorController.IsRunning;
        bool selectionMode = !monitorActive && _databasePanel.IsSelectionModeEnabled;

        Viewport.PointSelectionModeEnabled = selectionMode;
        SetScanEditingEnabled(!monitorActive && !selectionMode);

        _databasePanel.SetSelectionControlsEnabled(selectionMode, _selectedPointCount, _selectionWorkflowController.HasPendingChanges, monitorActive);
    }

    private void SetScanEditingEnabled(bool enabled)
    {
        _scanBoundsEditingEnabled = enabled;
        _scanVolumeInteractionController.EditingEnabled = enabled;
        Viewport.ScanVolumeEditEnabled = enabled;
        Viewport.ShowScanHandles = enabled;
        _scanVolumePanel.SetEditingEnabled(enabled);

        Viewport.Invalidate();
    }

    private void ApplyRenderSettingsToViewport()
    {
        Viewport.ShowPoints = _renderSettingsPanel.ShowPointsCheckBox.IsChecked ?? true;
        Viewport.ShowSurfels = _renderSettingsPanel.ShowSurfelsCheckBox.IsChecked ?? true;
        Viewport.ShowMissRays = _renderSettingsPanel.ShowMissRaysCheckBox.IsChecked ?? true;
        Viewport.ShowNormalRays = _renderSettingsPanel.ShowNormalsCheckBox.IsChecked ?? true;
        Viewport.ShowGrid = _renderSettingsPanel.ShowGridCheckBox.IsChecked ?? true;
        Viewport.ShowScanDensityPreview = _renderSettingsPanel.ShowDensityPreviewCheckBox.IsChecked ?? true;
        Viewport.ShowScanVolume = _renderSettingsPanel.ShowVolumeCheckBox.IsChecked ?? true;
    }

    private async void BtnMonitor_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _monitorWorkflowController.HandleMonitorButtonAsync(_logPath, _dbPath, _isDbLoaded, _viewportLoadController.TryLoadCurrentAsync);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] {ex.Message}");
        }
    }

    private void RenderSettings_Changed(object? sender, RoutedEventArgs e)
    {
        ApplyRenderSettingsToViewport();
        Viewport.Invalidate();
    }

    private void ChkDynamicColor_Changed(object? sender, RoutedEventArgs e)
    {
        Viewport.UseDynamicColorMapping = _renderSettingsPanel.DynamicColorCheckBox.IsChecked ?? false;
        Viewport.Invalidate();
    }

    private void SldSurfelSize_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        Viewport.SurfelScale = (float)_renderSettingsPanel.SurfelSizeSlider.Value;
        Viewport.Invalidate();
    }

}
