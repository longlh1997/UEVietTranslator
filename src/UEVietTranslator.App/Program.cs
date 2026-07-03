using Avalonia;

namespace UEVietTranslator.App;

internal static class Program
{
    // Điểm vào chuẩn của Avalonia. KHÔNG thêm logic nghiệp vụ ở đây —
    // mọi DI setup nằm trong App.axaml.cs để giữ file này ổn định.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
