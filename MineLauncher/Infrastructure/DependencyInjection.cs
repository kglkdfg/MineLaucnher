using Microsoft.Extensions.DependencyInjection;
using MineLauncher.Services.Abstractions;
using MineLauncher.Services.Implementations;
using MineLauncher.ViewModels;
namespace MineLauncher.Infrastructure;
public static class DependencyInjection
{
 public static ServiceProvider Build()
 {
  var s = new ServiceCollection();
  s.AddLogging();
  s.AddSingleton<HttpClient>();
  s.AddSingleton<IDownloadService, DownloadService>();
  s.AddSingleton<IJavaManager, JavaManager>();
  s.AddSingleton<OfflineAuthService>();
  s.AddSingleton<IAuthService>(sp => sp.GetRequiredService<OfflineAuthService>());
  s.AddSingleton<IInstanceRepository, InstanceRepository>();
  s.AddSingleton<IVersionManifestService, VersionManifestService>();
  s.AddSingleton<ILoaderVersionService, LoaderVersionService>();
  s.AddSingleton<ILoaderProviderFactory, LoaderProviderFactory>();
  s.AddSingleton<ILauncherService, LauncherService>();
  s.AddSingleton<MainViewModel>();
  s.AddTransient<MainWindow>();
  return s.BuildServiceProvider();
 }
}