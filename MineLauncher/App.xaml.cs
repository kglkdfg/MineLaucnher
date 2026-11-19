using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MineLauncher.Infrastructure;

namespace MineLauncher;
public partial class App : Application
{
    private ServiceProvider? _services;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services = DependencyInjection.Build();
        _services.GetRequiredService<MainWindow>().Show();
    }
    protected override void OnExit(ExitEventArgs e) { _services?.Dispose(); base.OnExit(e); }
}