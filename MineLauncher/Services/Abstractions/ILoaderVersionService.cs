using MineLauncher.Models;
namespace MineLauncher.Services.Abstractions;
public interface ILoaderVersionService
{
 Task<IReadOnlyList<string>> GetVersionsAsync(LoaderType loader, string minecraftVersion, CancellationToken ct = default);
}