using MineLauncher.Models;
using MineLauncher.Services.Abstractions;
namespace MineLauncher.Services.Implementations;
/// <summary>Честный адаптер-каркас; реальная реализация Vanilla pipeline относится к этапу 4.</summary>
public sealed class VersionManifestService : IVersionManifestService
{
 public Task<IReadOnlyList<string>> GetReleaseListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
 public Task<VersionJson> GetVersionAsync(string versionId, CancellationToken ct = default) => throw new NotSupportedException("Vanilla manifest pipeline будет реализован на этапе 4.");
 public Task DownloadLibrariesAsync(VersionJson version, Action<double>? progress = null, CancellationToken ct = default) => throw new NotSupportedException("Загрузка библиотек будет реализована на этапе 4.");
 public Task DownloadAssetsAsync(VersionJson version, Action<double>? progress = null, CancellationToken ct = default) => throw new NotSupportedException("Загрузка assets будет реализована на этапе 4.");
}