using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MeshTool.Core.Data;
using MeshTool.UI.Controls;

namespace MeshTool.UI.Controllers;

public sealed class SelectionWorkflowController
{
    private readonly SelectionManager _selectionManager;
    private readonly DatabaseController _databaseController;
    private readonly OpenGlViewport _viewport;
    private readonly Action<string> _log;
    private readonly Action _clearMesh;
    private readonly Action _updateMeshUiState;
    private readonly Action _applyInteractionMode;
    private readonly Action _markDbLoaded;
    private readonly Func<string> _getDbPath;
    private readonly Func<bool> _isMonitorRunning;
    private readonly Func<bool> _isSelectionModeEnabled;
    private readonly Func<IReadOnlyList<Ray>> _getLoadedMisses;
    private readonly Func<float> _getLoadedAvgDistance;
    private readonly Action<int> _setSelectedPointCount;
    private readonly Action<string> _setSelectedPointsText;

    public SelectionWorkflowController(
        SelectionManager selectionManager,
        DatabaseController databaseController,
        OpenGlViewport viewport,
        Action<string> log,
        Action clearMesh,
        Action updateMeshUiState,
        Action applyInteractionMode,
        Action markDbLoaded,
        Func<string> getDbPath,
        Func<bool> isMonitorRunning,
        Func<bool> isSelectionModeEnabled,
        Func<IReadOnlyList<Ray>> getLoadedMisses,
        Func<float> getLoadedAvgDistance,
        Action<int> setSelectedPointCount,
        Action<string> setSelectedPointsText)
    {
        _selectionManager = selectionManager;
        _databaseController = databaseController;
        _viewport = viewport;
        _log = log;
        _clearMesh = clearMesh;
        _updateMeshUiState = updateMeshUiState;
        _applyInteractionMode = applyInteractionMode;
        _markDbLoaded = markDbLoaded;
        _getDbPath = getDbPath;
        _isMonitorRunning = isMonitorRunning;
        _isSelectionModeEnabled = isSelectionModeEnabled;
        _getLoadedMisses = getLoadedMisses;
        _getLoadedAvgDistance = getLoadedAvgDistance;
        _setSelectedPointCount = setSelectedPointCount;
        _setSelectedPointsText = setSelectedPointsText;
    }

    public void BeginSession(IReadOnlyList<Vertex> loadedPoints)
    {
        _selectionManager.BeginSession(loadedPoints);
    }

    public void ClearSelectionDisplay()
    {
        _viewport.ClearPointSelection();
        _setSelectedPointCount(0);
        _setSelectedPointsText("Selected: 0");
        _applyInteractionMode();
    }

    public void DeleteSelectedPoints()
    {
        if (_isMonitorRunning())
        {
            _log("[ERROR] Stop monitor before deleting selected points.");
            return;
        }

        string dbPath = _getDbPath();
        if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
        {
            _log("[ERROR] Select a valid DB file first.");
            return;
        }

        if (!_isSelectionModeEnabled())
        {
            _log("[SELECT] Enable Point Selection Mode first.");
            return;
        }

        var selectedIndices = _viewport.GetSelectedPointIndices();
        if (selectedIndices.Length == 0)
        {
            _log("[SELECT] No points selected.");
            return;
        }

        try
        {
            int removed = _selectionManager.RemovePoints(selectedIndices);
            _viewport.ClearPointSelection();
            _setSelectedPointCount(0);
            _setSelectedPointsText("Selected: 0");
            _clearMesh();
            _updateMeshUiState();
            _viewport.LoadData(_selectionManager.GetWorkingPointsArray(), _getLoadedMisses().ToArray(), _getLoadedAvgDistance(), resetCamera: false);
            _viewport.ClearPointSelection();
            _markDbLoaded();
            _log($"[SELECT] Removed {removed} points from working set (not saved yet).");
        }
        catch (Exception ex)
        {
            _log($"[ERROR] Failed deleting selected points: {ex.Message}");
        }
        finally
        {
            _applyInteractionMode();
        }
    }

    public async Task SaveSelectionAsync(List<Vertex> loadedPoints)
    {
        if (!_selectionManager.HasPendingChanges)
        {
            return;
        }

        try
        {
            await _databaseController.SaveSelectionAsync(_getDbPath(), _selectionManager.WorkingPoints, _getLoadedMisses());
            _selectionManager.SyncToLoadedPoints(loadedPoints);
            _selectionManager.CommitChanges();
            _log($"[SELECT] Saved {_selectionManager.PointCount} points to {_getDbPath()}");
        }
        catch (Exception ex)
        {
            _log($"[ERROR] Failed saving selection edits: {ex.Message}");
        }
        finally
        {
            _applyInteractionMode();
        }
    }

    public void DiscardSelectionChanges()
    {
        if (!_selectionManager.HasPendingChanges)
        {
            return;
        }

        _selectionManager.DiscardChanges();
        _viewport.ClearPointSelection();
        _setSelectedPointCount(0);
        _setSelectedPointsText("Selected: 0");
        _viewport.LoadData(_selectionManager.GetWorkingPointsArray(), _getLoadedMisses().ToArray(), _getLoadedAvgDistance(), resetCamera: false);
        _markDbLoaded();
        _log("[SELECT] Discarded unsaved selection edits.");
        _applyInteractionMode();
    }

    public bool HasPendingChanges => _selectionManager.HasPendingChanges;
}
