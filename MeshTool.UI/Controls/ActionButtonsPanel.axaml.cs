using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MeshTool.UI.Controls;

public partial class ActionButtonsPanel : UserControl
{
    public event EventHandler<RoutedEventArgs>? MonitorRequested;
    public event EventHandler<RoutedEventArgs>? GenerateMeshRequested;
    public event EventHandler<RoutedEventArgs>? SaveMeshRequested;

    public ActionButtonsPanel()
    {
        InitializeComponent();
        WireEvents();
    }

    public Button MonitorButton => this.FindControl<Button>("BtnMonitor")!;
    public Button GenerateMeshButton => this.FindControl<Button>("BtnGenerateMesh")!;
    public Button SaveMeshButton => this.FindControl<Button>("BtnSaveMesh")!;
    public ProgressBar ProcessingProgressBar => this.FindControl<ProgressBar>("PrgProcessing")!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireEvents()
    {
        MonitorButton.Click += (sender, e) => MonitorRequested?.Invoke(sender, e);
        GenerateMeshButton.Click += (sender, e) => GenerateMeshRequested?.Invoke(sender, e);
        SaveMeshButton.Click += (sender, e) => SaveMeshRequested?.Invoke(sender, e);
    }
}
