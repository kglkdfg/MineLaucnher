using MineLauncher.Models;
using MineLauncher.Services.Abstractions;
namespace MineLauncher.Services.Implementations;
public sealed class LoaderProviderFactory : ILoaderProviderFactory
{
 private readonly IEnumerable<ILoaderProvider> _providers;
 public LoaderProviderFactory(IEnumerable<ILoaderProvider> providers) => _providers = providers;
 public ILoaderProvider GetProvider(LoaderType type) => _providers.FirstOrDefault(p => p.LoaderType == type)
  ?? throw new NotSupportedException($"Загрузчик {type} пока не реализован.");
}