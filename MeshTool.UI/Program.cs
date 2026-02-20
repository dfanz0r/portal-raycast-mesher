using Avalonia;
using System;

namespace MeshTool.UI;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var proj = Silk.NET.Maths.Matrix4X4.CreatePerspectiveFieldOfView(MathF.PI / 3.0f, 1.0f, 0.1f, 100.0f);
        System.IO.File.WriteAllText("proj_output.txt", $"M33: {proj.M33}, M43: {proj.M43}, M34: {proj.M34}, M44: {proj.M44}");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
