using MineLauncher.Models;
using MineLauncher.Services.Abstractions;
namespace MineLauncher.Services.Implementations;
/// <summary>Каталог версий загрузчиков будет реализован на этапе 5.</summary>
public sealed class LoaderVersionService : ILoaderVersionService
{
 public Task<IReadOnlyList<string>> GetVersionsAsync(LoaderType loader, string minecraftVersion, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
}