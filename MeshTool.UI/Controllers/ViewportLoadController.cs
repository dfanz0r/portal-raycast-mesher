using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using MeshTool.Core.Data;
using MeshTool.UI.Controls;

namespace MeshTool.UI.Controllers;

public sealed class ViewportLoadController
{
    private readonly DatabaseController _databaseController;
    private readonly SelectionWorkflowController _selectionWorkflowController;
    private readonly OpenGlViewport _viewport;
    private readonly Action _applyInteractionMode;
    private readonly Func<string> _getDbPath;
    private readonly Func<bool> _isMonitorRunning;
    private readonly Action<bool> _setLoadButtonEnabled;
    private readonly Action<string> _log;
    private readonly List<Vertex> _loadedPoints;
    private readonly List<Ray> _loadedMisses;
    private readonly Action<float> _setLoadedAvgDistance;
    private readonly Action _markDbLoaded;

    public ViewportLoadController(
        DatabaseController databaseController,
        SelectionWorkflowController selectionWorkflowController,
        OpenGlViewport viewport,
        Action applyInteractionMode,
        Func<string> getDbPath,
        Func<bool> isMonitorRunning,
        Action<bool> setLoadButtonEnabled,
        Action<string> log,
        List<Vertex> loadedPoints,
        List<Ray> loadedMisses,
        Action<float> setLoadedAvgDistance,
        Action markDbLoaded)
    {
        _databaseController = databaseController;
        _selectionWorkflowController = selectionWorkflowController;
        _viewport = viewport;
        _applyInteractionMode = applyInteractionMode;
        _getDbPath = getDbPath;
        _isMonitorRunning = isMonitorRunning;
        _setLoadButtonEnabled = setLoadButtonEnabled;
        _log = log;
        _loadedPoints = loadedPoints;
        _loadedMisses = loadedMisses;
        _setLoadedAvgDistance = setLoadedAvgDistance;
        _markDbLoaded = markDbLoaded;
    }

    public async Task<DatabaseLoadResult?> LoadCurrentAsync()
    {
        string dbPath = _getDbPath();
        var result = await _databaseController.LoadForViewportAsync(dbPath, _log);
        if (result == null)
        {
            return null;
        }

        _loadedPoints.Clear();
        _loadedPoints.AddRange(result.Points);
        _loadedMisses.Clear();
        _loadedMisses.AddRange(result.Misses);
        _setLoadedAvgDistance(result.AverageDistance);
        _selectionWorkflowController.BeginSession(_loadedPoints);

        _viewport.LoadData(result.Points.ToArray(), result.Misses.ToArray(), result.AverageDistance);
        _markDbLoaded();
        _applyInteractionMode();
        return result;
    }

    public async Task<bool> TryLoadCurrentAsync()
    {
        return await LoadCurrentAsync() != null;
    }

    public async void OnLoadRequested(object? sender, RoutedEventArgs e)
    {
        if (_isMonitorRunning())
        {
            _log("[ERROR] Stop monitor before Load Points & View.");
            return;
        }

        if (string.IsNullOrEmpty(_getDbPath()))
        {
            _log("[ERROR] Select DB file first.");
            return;
        }

        _setLoadButtonEnabled(false);
        try
        {
            await LoadCurrentAsync();
        }
        catch (Exception ex)
        {
            _log($"[ERROR] {ex.Message}");
        }
        finally
        {
            _setLoadButtonEnabled(true);
        }
    }
}
