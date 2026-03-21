using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MeshTool.UI.Models;

namespace MeshTool.UI.Controllers;

/// <summary>
/// Handles drag interactions for scan volume handle controls.
/// </summary>
public sealed class ScanVolumeInteractionController
{
    private readonly ScanVolumeController _scanVolumeController;
    private readonly Func<ScanVolumeSettings> _getCurrentSettings;
    private readonly Action<ScanVolumeSettings> _applySettings;
    private bool _isDragging;
    private string _handleKey = string.Empty;
    private Point _lastPoint;

    public ScanVolumeInteractionController(
        ScanVolumeController scanVolumeController,
        Func<ScanVolumeSettings> getCurrentSettings,
        Action<ScanVolumeSettings> applySettings)
    {
        _scanVolumeController = scanVolumeController;
        _getCurrentSettings = getCurrentSettings;
        _applySettings = applySettings;
    }

    public bool EditingEnabled { get; set; } = true;

    public void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!EditingEnabled || sender is not Border border || border.Tag is not string key)
        {
            return;
        }

        _isDragging = true;
        _handleKey = key;
        _lastPoint = e.GetPosition(border);
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    public void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!EditingEnabled || !_isDragging || sender is not Border border)
        {
            return;
        }

        var current = e.GetPosition(border);
        double dx = current.X - _lastPoint.X;
        if (Math.Abs(dx) < 0.01)
        {
            return;
        }

        bool fine = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool coarse = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        float unit = ScanVolumeController.GetScanHandleStep(_handleKey, fine, coarse);
        float delta = (float)(dx * unit);

        var updated = _scanVolumeController.ApplyDelta(_getCurrentSettings(), _handleKey, delta).Sanitize();
        _applySettings(updated);
        _lastPoint = current;
        e.Handled = true;
    }

    public void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!EditingEnabled)
        {
            return;
        }

        _isDragging = false;
        _handleKey = string.Empty;
        if (sender is Border border && e.Pointer.Captured == border)
        {
            e.Pointer.Capture(null);
        }

        e.Handled = true;
    }
}
