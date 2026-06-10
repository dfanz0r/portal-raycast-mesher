using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MeshTool.UI.Controls;

public partial class ScanVolumePanel : UserControl
{
    public event EventHandler<RoutedEventArgs>? ResetRequested;
    public event EventHandler<RoutedEventArgs>? ExportScriptRequested;
    public event Action<object?, PointerPressedEventArgs>? HandlePointerPressed;
    public event Action<object?, PointerEventArgs>? HandlePointerMoved;
    public event Action<object?, PointerReleasedEventArgs>? HandlePointerReleased;

    public ScanVolumePanel()
    {
        InitializeComponent();
        WireEvents();
    }

    public TextBox ScanCenterXTextBox => this.FindControl<TextBox>("TxtScanCenterX")!;
    public TextBox ScanCenterZTextBox => this.FindControl<TextBox>("TxtScanCenterZ")!;
    public TextBox ScanSizeXTextBox => this.FindControl<TextBox>("TxtScanSizeX")!;
    public TextBox ScanSizeZTextBox => this.FindControl<TextBox>("TxtScanSizeZ")!;
    public TextBox ScanYTopTextBox => this.FindControl<TextBox>("TxtScanYTop")!;
    public TextBox ScanYBottomTextBox => this.FindControl<TextBox>("TxtScanYBottom")!;
    public TextBox ScanYawTextBox => this.FindControl<TextBox>("TxtScanYaw")!;
    public TextBox ScanRayTiltTextBox => this.FindControl<TextBox>("TxtScanRayTilt")!;
    public int BotCount
    {
        get
        {
            if (int.TryParse(this.FindControl<TextBox>("TxtBotCount")?.Text, out var val))
                return Math.Clamp(val, 1, 70);
            return 5;
        }
    }
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

    private void WireEvents()
    {
        ResetScanVolumeButton.Click += (sender, e) => ResetRequested?.Invoke(sender, e);
        ExportScanScriptButton.Click += (sender, e) => ExportScriptRequested?.Invoke(sender, e);

        foreach (var handle in ScanHandleBorders)
        {
            handle.PointerPressed += (sender, e) => HandlePointerPressed?.Invoke(sender, e);
            handle.PointerMoved += (sender, e) => HandlePointerMoved?.Invoke(sender, e);
            handle.PointerReleased += (sender, e) => HandlePointerReleased?.Invoke(sender, e);
        }
    }

    public void SetEditingEnabled(bool enabled)
    {
        ScanCenterXTextBox.IsEnabled = enabled;
        ScanCenterZTextBox.IsEnabled = enabled;
        ScanSizeXTextBox.IsEnabled = enabled;
        ScanSizeZTextBox.IsEnabled = enabled;
        ScanYTopTextBox.IsEnabled = enabled;
        ScanYBottomTextBox.IsEnabled = enabled;
        ScanYawTextBox.IsEnabled = enabled;
        ScanRayTiltTextBox.IsEnabled = enabled;
        ScanDensitySlider.IsEnabled = enabled;
        FineDensitySlider.IsEnabled = enabled;
        ResetScanVolumeButton.IsEnabled = enabled;
        this.FindControl<TextBox>("TxtBotCount")!.IsEnabled = enabled;
    }
}
