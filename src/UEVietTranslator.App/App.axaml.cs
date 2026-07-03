using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using UEVietTranslator.App.ViewModels;
using UEVietTranslator.App.Views;
using UEVietTranslator.Core;

namespace UEVietTranslator.App;

public class App : Application
{
    // Service provider cho toàn app. Core được đăng ký qua
    // AddUeVietTranslatorCore() để App và Cli dùng chung 1 cách đăng ký
    // (xem CLAUDE.md §5.6 / docs/CODING_STANDARDS.md).
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddUeVietTranslatorCore();
        services.AddSingleton<MainWindowViewModel>();
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
