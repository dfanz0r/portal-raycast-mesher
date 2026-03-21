using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MeshTool.UI.Controls;

public partial class ConsoleOutputPanel : UserControl
{
    private ListBox? _consoleList;
    private TextBlock? _consoleCountTextBlock;
    private Button? _clearConsoleButton;

    public ConsoleOutputPanel()
    {
        InitializeComponent();
    }

    public ListBox ConsoleList => _consoleList ??= this.FindControl<ListBox>("LstConsole")!;
    public TextBlock ConsoleCountTextBlock => _consoleCountTextBlock ??= this.FindControl<TextBlock>("TxtConsoleCount")!;
    public Button ClearConsoleButton => _clearConsoleButton ??= this.FindControl<Button>("BtnClearConsole")!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
