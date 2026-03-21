using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MeshTool.Core.IO;
using MeshTool.UI.Controls;

namespace MeshTool.UI.Controllers;

/// <summary>
/// Owns console/log panel state and UI synchronization.
/// </summary>
public sealed class LogPanelController
{
    private readonly ConsoleOutputPanel _consolePanel;
    private readonly Avalonia.Controls.Primitives.ToggleButton _toggleButton;
    private readonly ObservableCollection<string> _logLines = new();

    public LogPanelController(ConsoleOutputPanel consolePanel, Avalonia.Controls.Primitives.ToggleButton toggleButton)
    {
        _consolePanel = consolePanel;
        _toggleButton = toggleButton;
        _consolePanel.ConsoleList.ItemsSource = _logLines;
        _consolePanel.ClearConsoleButton.Click += OnClearConsoleClicked;
        _toggleButton.Click += OnToggleConsoleClicked;
    }

    public void AttachLogger()
    {
        Logger.LogAdded += OnLoggerMessage;
    }

    public void DetachLogger()
    {
        Logger.LogAdded -= OnLoggerMessage;
    }

    private void OnLoggerMessage(object? sender, LogEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _logLines.Add(e.Message);
            while (_logLines.Count > 200)
            {
                _logLines.RemoveAt(0);
            }

            _consolePanel.ConsoleCountTextBlock.Text = _logLines.Count > 0 ? $"{_logLines.Count} lines" : string.Empty;

            if (_logLines.Count == 0 || !_consolePanel.ConsoleList.IsVisible)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_logLines.Count > 0)
                    {
                        _consolePanel.ConsoleList.ScrollIntoView(_logLines[^1]);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }, DispatcherPriority.Background);
        });
    }

    private void OnToggleConsoleClicked(object? sender, RoutedEventArgs e)
    {
        _consolePanel.IsVisible = _toggleButton.IsChecked == true;
    }

    private void OnClearConsoleClicked(object? sender, RoutedEventArgs e)
    {
        _logLines.Clear();
        Logger.ClearRecentLogs();
        _consolePanel.ConsoleCountTextBlock.Text = string.Empty;
    }
}
