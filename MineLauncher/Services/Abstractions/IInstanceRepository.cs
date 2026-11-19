using MineLauncher.Models;
namespace MineLauncher.Services.Abstractions;
public interface IInstanceRepository
{
 IReadOnlyList<Instance> LoadAll();
 Task SaveAllAsync(IEnumerable<Instance> instances, CancellationToken ct = default);
}