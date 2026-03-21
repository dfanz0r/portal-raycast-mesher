using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MeshTool.Core.Data;

namespace MeshTool.UI.Controllers;

public sealed class DatabaseWorkflowController
{
    private readonly DatabaseController _databaseController;
    private readonly ObservableCollection<string> _dbFiles;
    private readonly Action<string> _log;
    private readonly Action _clearMesh;
    private readonly Action _clearLoadedViewportState;
    private readonly Action _updateMeshUiState;
    private readonly Action<Vertex[], Ray[], float> _loadEmptyViewportData;

    public DatabaseWorkflowController(
        DatabaseController databaseController,
        ObservableCollection<string> dbFiles,
        Action<string> log,
        Action clearMesh,
        Action clearLoadedViewportState,
        Action updateMeshUiState,
        Action<Vertex[], Ray[], float> loadEmptyViewportData)
    {
        _databaseController = databaseController;
        _dbFiles = dbFiles;
        _log = log;
        _clearMesh = clearMesh;
        _clearLoadedViewportState = clearLoadedViewportState;
        _updateMeshUiState = updateMeshUiState;
        _loadEmptyViewportData = loadEmptyViewportData;
    }

    public void LoadLocalDbFiles(ComboBox comboBox, string currentDirectory)
    {
        _dbFiles.Clear();
        foreach (var entry in _databaseController.GetLocalDatabaseEntries(currentDirectory))
        {
            _dbFiles.Add(entry);
        }

        if (_dbFiles.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    public string ResolveSelectedPath(string selected, string currentDirectory)
    {
        _clearMesh();
        _updateMeshUiState();
        return _databaseController.ResolveSelectedPath(selected, currentDirectory);
    }

    public async Task<string?> BrowseAsync(IStorageProvider storageProvider, ComboBox comboBox, string currentDirectory)
    {
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Database",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Database Files") { Patterns = ["*.db"] }]
        });

        if (files.Count < 1)
        {
            return null;
        }

        string path = files[0].Path.LocalPath;
        SelectPath(comboBox, path, currentDirectory);
        return path;
    }

    public async Task<string?> CreateNewAsync(IStorageProvider storageProvider, ComboBox comboBox, string currentDirectory)
    {
        string suggested = $"map_{DateTime.Now:yyyyMMdd_HHmmss}.db";
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create New Database",
            DefaultExtension = ".db",
            SuggestedFileName = suggested,
            FileTypeChoices = [new FilePickerFileType("Database Files") { Patterns = ["*.db"] }]
        });

        if (file == null)
        {
            return null;
        }

        string path = file.Path.LocalPath;
        _databaseController.CreateEmptyDatabase(path);
        SelectPath(comboBox, path, currentDirectory);
        _clearLoadedViewportState();
        _loadEmptyViewportData(Array.Empty<Vertex>(), Array.Empty<Ray>(), 1.0f);
        _log($"[DB] Created new database: {path}");
        return path;
    }

    public void Clear(string path)
    {
        _databaseController.ClearDatabase(path);
        _clearMesh();
        _clearLoadedViewportState();
        _updateMeshUiState();
        _loadEmptyViewportData(Array.Empty<Vertex>(), Array.Empty<Ray>(), 1.0f);
        _log($"[DB] Cleared database: {path}");
    }

    private void SelectPath(ComboBox comboBox, string path, string currentDirectory)
    {
        string displayEntry = _databaseController.GetDisplayEntry(path, currentDirectory);
        if (!_dbFiles.Contains(displayEntry))
        {
            _dbFiles.Add(displayEntry);
        }

        comboBox.SelectedItem = displayEntry;
        _clearMesh();
        _updateMeshUiState();
    }
}
