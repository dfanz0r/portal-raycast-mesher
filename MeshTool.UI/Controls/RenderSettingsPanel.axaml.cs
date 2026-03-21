using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MeshTool.UI.Controls;

public partial class RenderSettingsPanel : UserControl
{
    private CheckBox? _showPointsCheckBox;
    private CheckBox? _showSurfelsCheckBox;
    private CheckBox? _showMissRaysCheckBox;
    private CheckBox? _showNormalsCheckBox;
    private CheckBox? _showMeshCheckBox;
    private CheckBox? _showGridCheckBox;
    private CheckBox? _showDensityPreviewCheckBox;
    private CheckBox? _showVolumeCheckBox;
    private CheckBox? _dynamicColorCheckBox;
    private Slider? _surfelSizeSlider;

    public RenderSettingsPanel()
    {
        InitializeComponent();
    }

    public CheckBox ShowPointsCheckBox => _showPointsCheckBox ??= this.FindControl<CheckBox>("ChkShowPoints")!;
    public CheckBox ShowSurfelsCheckBox => _showSurfelsCheckBox ??= this.FindControl<CheckBox>("ChkShowSurfels")!;
    public CheckBox ShowMissRaysCheckBox => _showMissRaysCheckBox ??= this.FindControl<CheckBox>("ChkShowMissRays")!;
    public CheckBox ShowNormalsCheckBox => _showNormalsCheckBox ??= this.FindControl<CheckBox>("ChkShowNormals")!;
    public CheckBox ShowMeshCheckBox => _showMeshCheckBox ??= this.FindControl<CheckBox>("ChkShowMesh")!;
    public CheckBox ShowGridCheckBox => _showGridCheckBox ??= this.FindControl<CheckBox>("ChkShowGrid")!;
    public CheckBox ShowDensityPreviewCheckBox => _showDensityPreviewCheckBox ??= this.FindControl<CheckBox>("ChkShowDensityPreview")!;
    public CheckBox ShowVolumeCheckBox => _showVolumeCheckBox ??= this.FindControl<CheckBox>("ChkShowVolume")!;
    public CheckBox DynamicColorCheckBox => _dynamicColorCheckBox ??= this.FindControl<CheckBox>("ChkDynamicColor")!;
    public Slider SurfelSizeSlider => _surfelSizeSlider ??= this.FindControl<Slider>("SldSurfelSize")!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
