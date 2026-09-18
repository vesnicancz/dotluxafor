using Avalonia;
using Avalonia.X11;

namespace DotLuxafor.ControlPanel;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Client-side decorations let the title bar follow the One Dark theme.
            // Windows and macOS honour ExtendClientAreaToDecorationsHint on their own;
            // on X11 the drawn decorations still sit behind an experimental opt-in.
#pragma warning disable AVALONIA_X11_CSD
            .With(new X11PlatformOptions { EnableDrawnDecorations = true })
#pragma warning restore AVALONIA_X11_CSD
            .WithInterFont()
            .LogToTrace();
}
