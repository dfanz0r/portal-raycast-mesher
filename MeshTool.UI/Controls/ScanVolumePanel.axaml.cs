using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MeshTool.UI.Controls;

public partial class ScanVolumePanel : UserControl
{
    public ScanVolumePanel()
    {
        InitializeComponent();
    }

    public TextBox ScanCenterXTextBox => this.FindControl<TextBox>("TxtScanCenterX")!;
    public TextBox ScanCenterZTextBox => this.FindControl<TextBox>("TxtScanCenterZ")!;
    public TextBox ScanSizeXTextBox => this.FindControl<TextBox>("TxtScanSizeX")!;
    public TextBox ScanSizeZTextBox => this.FindControl<TextBox>("TxtScanSizeZ")!;
    public TextBox ScanYTopTextBox => this.FindControl<TextBox>("TxtScanYTop")!;
    public TextBox ScanYBottomTextBox => this.FindControl<TextBox>("TxtScanYBottom")!;
    public TextBox ScanYawTextBox => this.FindControl<TextBox>("TxtScanYaw")!;
    public TextBox ScanRayTiltTextBox => this.FindControl<TextBox>("TxtScanRayTilt")!;
    public Slider ScanDensitySlider => this.FindControl<Slider>("SldScanDensity")!;
    public Slider FineDensitySlider => this.FindControl<Slider>("SldFineDensity")!;
    public TextBlock BroadDensityTextBlock => this.FindControl<TextBlock>("TxtBroadDensityMeters")!;
    public TextBlock FineDensityTextBlock => this.FindControl<TextBlock>("TxtFineDensityMeters")!;
    public Button ResetScanVolumeButton => this.FindControl<Button>("BtnResetScanVolume")!;
    public Button ExportScanScriptButton => this.FindControl<Button>("BtnExportScanTs")!;
    public Border[] ScanHandleBorders =>
    [
        this.FindControl<Border>("HandleCenterX")!,
        this.FindControl<Border>("HandleCenterZ")!,
        this.FindControl<Border>("HandleSizeX")!,
        this.FindControl<Border>("HandleSizeZ")!,
        this.FindControl<Border>("HandleYTop")!,
        this.FindControl<Border>("HandleYBottom")!,
        this.FindControl<Border>("HandleYaw")!,
        this.FindControl<Border>("HandleRayTilt")!
    ];

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
