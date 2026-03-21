using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MeshTool.UI.Controls;

public partial class ActionButtonsPanel : UserControl
{
    public ActionButtonsPanel()
    {
        InitializeComponent();
    }

    public Button MonitorButton => this.FindControl<Button>("BtnMonitor")!;
    public Button GenerateMeshButton => this.FindControl<Button>("BtnGenerateMesh")!;
    public Button SaveMeshButton => this.FindControl<Button>("BtnSaveMesh")!;
    public ProgressBar ProcessingProgressBar => this.FindControl<ProgressBar>("PrgProcessing")!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
