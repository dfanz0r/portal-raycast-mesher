using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;

namespace MeshTool.UI.Controls;

public partial class StatusPanel : UserControl
{
    public StatusPanel()
    {
        InitializeComponent();
    }

    public TextBlock HoveredCoordinateTextBlock => this.FindControl<TextBlock>("TxtHoveredCoord")!;
    public TextBlock CameraSpeedTextBlock => this.FindControl<TextBlock>("TxtCameraSpeed")!;
    public ToggleButton ToggleConsoleButton => this.FindControl<ToggleButton>("BtnToggleConsole")!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
