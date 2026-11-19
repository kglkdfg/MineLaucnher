using System.Text.Json;
using MineLauncher.Models;
using MineLauncher.Services.Abstractions;
namespace MineLauncher.Services.Implementations;
public sealed class InstanceRepository : IInstanceRepository
{
 private readonly string _path = Path.Combine(LauncherPaths.Root, "instances.json");
 private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
 public IReadOnlyList<Instance> LoadAll()
 {
  try {
   if (!File.Exists(_path)) return [DefaultInstance()];
   return JsonSerializer.Deserialize<List<Instance>>(File.ReadAllText(_path), Options) ?? [DefaultInstance()];
  }
  catch (JsonException) {
   try { File.Move(_path, _path + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds()); } catch (IOException) { }
   return [DefaultInstance()];
  }
  catch (IOException) { return [DefaultInstance()]; }
 }
 public async Task SaveAllAsync(IEnumerable<Instance> instances, CancellationToken ct = default)
 {
  Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
  var temp = _path + ".tmp";
  await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
   await JsonSerializer.SerializeAsync(stream, instances.ToList(), Options, ct);
  File.Move(temp, _path, true);
 }
 private static Instance DefaultInstance() => new() { Id = "default", DisplayName = "Моя сборка", MinecraftVersion = "1.20.4", Loader = LoaderType.Vanilla };
}