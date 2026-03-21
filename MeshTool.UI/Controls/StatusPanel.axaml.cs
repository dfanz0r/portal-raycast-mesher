using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MeshTool.UI.Controls;

public partial class StatusPanel : UserControl
{
    public event EventHandler<RoutedEventArgs>? ToggleConsoleRequested;

    public StatusPanel()
    {
        InitializeComponent();
        ToggleConsoleButton.Click += (sender, e) => ToggleConsoleRequested?.Invoke(sender, e);
    }

    public TextBlock HoveredCoordinateTextBlock => this.FindControl<TextBlock>("TxtHoveredCoord")!;
    public TextBlock CameraSpeedTextBlock => this.FindControl<TextBlock>("TxtCameraSpeed")!;
    public ToggleButton ToggleConsoleButton => this.FindControl<ToggleButton>("BtnToggleConsole")!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public void SetCameraSpeed(float speed)
    {
        CameraSpeedTextBlock.Text = $"{speed:F1} m/s";
    }

    public void SetHoveredCoordinate(string text)
    {
        HoveredCoordinateTextBlock.Text = text;
    }
}
