using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MeshTool.UI.Controls;

public partial class DatabasePanel : UserControl
{
    public event EventHandler<SelectionChangedEventArgs>? DatabasePathSelectionChanged;
    public event EventHandler<RoutedEventArgs>? BrowseRequested;
    public event EventHandler<RoutedEventArgs>? LoadRequested;
    public event EventHandler<RoutedEventArgs>? ClearRequested;
    public event EventHandler<RoutedEventArgs>? NewRequested;
    public event EventHandler<RoutedEventArgs>? PointSelectionModeChanged;
    public event EventHandler<RoutedEventArgs>? DeleteSelectionRequested;
    public event EventHandler<RoutedEventArgs>? ClearSelectionRequested;
    public event EventHandler<RoutedEventArgs>? SaveSelectionRequested;
    public event EventHandler<RoutedEventArgs>? DiscardSelectionRequested;

    public DatabasePanel()
    {
        InitializeComponent();
        WireEvents();
    }

    public ComboBox DatabasePathComboBox => this.FindControl<ComboBox>("CmbDbPath")!;
    public Button BrowseDatabaseButton => this.FindControl<Button>("BtnBrowseDb")!;
    public Button LoadDatabaseButton => this.FindControl<Button>("BtnMesh")!;
    public Button ClearDatabaseButton => this.FindControl<Button>("BtnClearDb")!;
    public Button NewDatabaseButton => this.FindControl<Button>("BtnNewDb")!;
    public CheckBox PointSelectionModeCheckBox => this.FindControl<CheckBox>("ChkPointSelectMode")!;
    public TextBlock SelectedPointsTextBlock => this.FindControl<TextBlock>("TxtSelectedPoints")!;
    public Button DeleteSelectedPointsButton => this.FindControl<Button>("BtnDeleteSelectedPoints")!;
    public Button ClearSelectedPointsButton => this.FindControl<Button>("BtnClearSelectedPoints")!;
    public Button SaveSelectionButton => this.FindControl<Button>("BtnSelectionSave")!;
    public Button DiscardSelectionButton => this.FindControl<Button>("BtnSelectionDiscard")!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireEvents()
    {
        DatabasePathComboBox.SelectionChanged += (sender, e) => DatabasePathSelectionChanged?.Invoke(sender, e);
        BrowseDatabaseButton.Click += (sender, e) => BrowseRequested?.Invoke(sender, e);
        LoadDatabaseButton.Click += (sender, e) => LoadRequested?.Invoke(sender, e);
        ClearDatabaseButton.Click += (sender, e) => ClearRequested?.Invoke(sender, e);
        NewDatabaseButton.Click += (sender, e) => NewRequested?.Invoke(sender, e);
        PointSelectionModeCheckBox.IsCheckedChanged += (sender, e) => PointSelectionModeChanged?.Invoke(sender, e);
        DeleteSelectedPointsButton.Click += (sender, e) => DeleteSelectionRequested?.Invoke(sender, e);
        ClearSelectedPointsButton.Click += (sender, e) => ClearSelectionRequested?.Invoke(sender, e);
        SaveSelectionButton.Click += (sender, e) => SaveSelectionRequested?.Invoke(sender, e);
        DiscardSelectionButton.Click += (sender, e) => DiscardSelectionRequested?.Invoke(sender, e);
    }

    public string? SelectedDatabaseEntry => DatabasePathComboBox.SelectedItem as string;

    public void SetSelectedPointsCount(int count)
    {
        SelectedPointsTextBlock.Text = $"Selected: {count}";
    }

    public void ClearSelectedPointsCount()
    {
        SelectedPointsTextBlock.Text = "Selected: 0";
    }

    public bool IsSelectionModeEnabled
    {
        get => PointSelectionModeCheckBox.IsChecked ?? false;
        set => PointSelectionModeCheckBox.IsChecked = value;
    }

    public void SetSelectionControlsEnabled(bool selectionMode, int selectedPointCount, bool hasPendingChanges, bool monitorActive)
    {
        PointSelectionModeCheckBox.IsEnabled = !monitorActive;
        DeleteSelectedPointsButton.IsEnabled = selectionMode && selectedPointCount > 0;
        ClearSelectedPointsButton.IsEnabled = selectionMode && selectedPointCount > 0;
        SaveSelectionButton.IsEnabled = !monitorActive && hasPendingChanges;
        DiscardSelectionButton.IsEnabled = !monitorActive && hasPendingChanges;
    }

    public void SetLoadButtonEnabled(bool enabled)
    {
        LoadDatabaseButton.IsEnabled = enabled;
    }
}
