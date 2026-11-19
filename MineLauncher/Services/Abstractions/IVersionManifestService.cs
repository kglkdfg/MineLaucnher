using MineLauncher.Models;
namespace MineLauncher.Services.Abstractions;
public interface IVersionManifestService
{
 Task<IReadOnlyList<string>> GetReleaseListAsync(CancellationToken ct = default);
 Task<VersionJson> GetVersionAsync(string versionId, CancellationToken ct = default);
 Task DownloadLibrariesAsync(VersionJson version, Action<double>? progress = null, CancellationToken ct = default);
 Task DownloadAssetsAsync(VersionJson version, Action<double>? progress = null, CancellationToken ct = default);
}