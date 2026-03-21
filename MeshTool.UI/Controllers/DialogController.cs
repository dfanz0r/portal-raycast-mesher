using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MeshTool.UI.Controllers;

public sealed class DialogController
{
    public async Task<bool> ConfirmClearDatabaseAsync(Window owner)
    {
        var dialog = new Window
        {
            Width = 460,
            Height = 180,
            CanResize = false,
            Title = "Confirm Clear Database",
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        bool result = false;

        var yesButton = new Button
        {
            Content = "Yes, Clear Database",
            MinWidth = 150
        };
        yesButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 90
        };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "This will permanently remove all points and rays and overwrite the selected DB file.",
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = "Are you sure you want to continue?",
                    FontSize = 13
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children =
                    {
                        cancelButton,
                        yesButton
                    }
                }
            }
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
