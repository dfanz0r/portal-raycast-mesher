using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using MeshTool.Core.IO;
using MeshTool.UI.Controls;

namespace MeshTool.UI.Controllers;

public sealed class MonitorWorkflowController
{
    private readonly MonitorController _monitorController;
    private readonly OpenGlViewport _viewport;
    private readonly DatabasePanel _databasePanel;
    private readonly RenderSettingsPanel _renderSettingsPanel;
    private readonly ActionButtonsPanel _actionButtonsPanel;
    private readonly Action<bool> _setScanEditingEnabled;
    private readonly Action _applyInteractionMode;
    private readonly Action _updateMeshUiState;
    private readonly Action _applyRenderSettingsToViewport;
    private readonly Action<string> _log;
    private bool _renderOverridesActive;
    private bool _preMonitorShowPoints;
    private bool _preMonitorShowSurfels;
    private bool _preMonitorShowMissRays;
    private bool _preMonitorShowDensityPreview;
    private bool _preMonitorShowVolume;

    public MonitorWorkflowController(
        MonitorController monitorController,
        OpenGlViewport viewport,
        DatabasePanel databasePanel,
        RenderSettingsPanel renderSettingsPanel,
        ActionButtonsPanel actionButtonsPanel,
        Action<bool> setScanEditingEnabled,
        Action applyInteractionMode,
        Action updateMeshUiState,
        Action applyRenderSettingsToViewport,
        Action<string> log)
    {
        _monitorController = monitorController;
        _viewport = viewport;
        _databasePanel = databasePanel;
        _renderSettingsPanel = renderSettingsPanel;
        _actionButtonsPanel = actionButtonsPanel;
        _setScanEditingEnabled = setScanEditingEnabled;
        _applyInteractionMode = applyInteractionMode;
        _updateMeshUiState = updateMeshUiState;
        _applyRenderSettingsToViewport = applyRenderSettingsToViewport;
        _log = log;

        _monitorController.Started += OnStarted;
        _monitorController.Stopped += OnStopped;
    }

    public async Task HandleMonitorButtonAsync(string logPath, string dbPath, bool isDbLoaded, Func<Task<bool>> loadDbAsync)
    {
        if (_monitorController.IsRunning)
        {
            _log("[MONITOR] Stopping...");
            await _monitorController.StopAsync();
            return;
        }

        if (string.IsNullOrEmpty(dbPath))
        {
            _log("[ERROR] Select DB file first.");
            return;
        }

        if (!isDbLoaded)
        {
            bool loaded = await loadDbAsync();
            if (!loaded)
            {
                return;
            }
        }

        var options = new MonitorRunOptions
        {
            StartAtEnd = true,
            IncludeSnapshots = false,
            Log = _log
        };

        await _monitorController.StartAsync(logPath, dbPath, options);
    }

    private void OnStarted()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _actionButtonsPanel.MonitorButton.Content = "Stop Monitor";
            _databasePanel.LoadDatabaseButton.IsEnabled = false;
            _databasePanel.BrowseDatabaseButton.IsEnabled = false;
            _databasePanel.NewDatabaseButton.IsEnabled = false;
            _databasePanel.ClearDatabaseButton.IsEnabled = false;
            _actionButtonsPanel.GenerateMeshButton.IsEnabled = false;
            _actionButtonsPanel.SaveMeshButton.IsEnabled = false;
            _renderSettingsPanel.ShowMeshCheckBox.IsChecked = false;
            _renderSettingsPanel.ShowMeshCheckBox.IsEnabled = false;
            _viewport.PointSelectionModeEnabled = false;
            _setScanEditingEnabled(false);
            _databasePanel.PointSelectionModeCheckBox.IsEnabled = false;
            _databasePanel.DeleteSelectedPointsButton.IsEnabled = false;
            _databasePanel.ClearSelectedPointsButton.IsEnabled = false;
            _databasePanel.SaveSelectionButton.IsEnabled = false;
            _databasePanel.DiscardSelectionButton.IsEnabled = false;
            ApplyRenderOverrides(true);
        });
    }

    private void OnStopped()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _actionButtonsPanel.MonitorButton.Content = "Start Monitor";
            _databasePanel.LoadDatabaseButton.IsEnabled = true;
            _databasePanel.BrowseDatabaseButton.IsEnabled = true;
            _databasePanel.NewDatabaseButton.IsEnabled = true;
            _databasePanel.ClearDatabaseButton.IsEnabled = true;
            _actionButtonsPanel.GenerateMeshButton.IsEnabled = true;
            _updateMeshUiState();
            _applyInteractionMode();
            ApplyRenderOverrides(false);
        });
    }

    private void ApplyRenderOverrides(bool enabled)
    {
        if (enabled)
        {
            if (!_renderOverridesActive)
            {
                _preMonitorShowPoints = _renderSettingsPanel.ShowPointsCheckBox.IsChecked ?? true;
                _preMonitorShowSurfels = _renderSettingsPanel.ShowSurfelsCheckBox.IsChecked ?? true;
                _preMonitorShowMissRays = _renderSettingsPanel.ShowMissRaysCheckBox.IsChecked ?? false;
                _preMonitorShowDensityPreview = _renderSettingsPanel.ShowDensityPreviewCheckBox.IsChecked ?? true;
                _preMonitorShowVolume = _renderSettingsPanel.ShowVolumeCheckBox.IsChecked ?? true;
            }

            _renderOverridesActive = true;
            _renderSettingsPanel.ShowPointsCheckBox.IsChecked = true;
            _renderSettingsPanel.ShowSurfelsCheckBox.IsChecked = true;
            _renderSettingsPanel.ShowMissRaysCheckBox.IsChecked = true;
            _renderSettingsPanel.ShowDensityPreviewCheckBox.IsChecked = false;
            _renderSettingsPanel.ShowVolumeCheckBox.IsChecked = true;
            _renderSettingsPanel.ShowPointsCheckBox.IsEnabled = false;
            _renderSettingsPanel.ShowSurfelsCheckBox.IsEnabled = false;
            _renderSettingsPanel.ShowMissRaysCheckBox.IsEnabled = false;
            _renderSettingsPanel.ShowDensityPreviewCheckBox.IsEnabled = false;
            _renderSettingsPanel.ShowVolumeCheckBox.IsEnabled = false;
            _applyRenderSettingsToViewport();
            _viewport.ShowScanHandles = false;
            _viewport.Invalidate();
            return;
        }

        if (!_renderOverridesActive)
        {
            return;
        }

        _renderOverridesActive = false;
        _renderSettingsPanel.ShowPointsCheckBox.IsEnabled = true;
        _renderSettingsPanel.ShowSurfelsCheckBox.IsEnabled = true;
        _renderSettingsPanel.ShowMissRaysCheckBox.IsEnabled = true;
        _renderSettingsPanel.ShowDensityPreviewCheckBox.IsEnabled = true;
        _renderSettingsPanel.ShowVolumeCheckBox.IsEnabled = true;
        _renderSettingsPanel.ShowPointsCheckBox.IsChecked = _preMonitorShowPoints;
        _renderSettingsPanel.ShowSurfelsCheckBox.IsChecked = _preMonitorShowSurfels;
        _renderSettingsPanel.ShowMissRaysCheckBox.IsChecked = _preMonitorShowMissRays;
        _renderSettingsPanel.ShowDensityPreviewCheckBox.IsChecked = _preMonitorShowDensityPreview;
        _renderSettingsPanel.ShowVolumeCheckBox.IsChecked = _preMonitorShowVolume;
        _applyRenderSettingsToViewport();
        _viewport.Invalidate();
    }
}
