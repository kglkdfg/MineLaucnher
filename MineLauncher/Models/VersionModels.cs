using MineLauncher.Services.Implementations;
namespace MineLauncher.Models;
public sealed class VersionJson
{
 public string Id { get; init; } = "";
 public string Type { get; init; } = "release";
 public int MinimumVersion { get; init; }
 public int MinecraftVersionMinor { get; init; }
 public int ReleasePatch { get; init; }
 public string MainClass { get; init; } = "";
 public VersionArguments Arguments { get; init; } = new([], []);
 public IReadOnlyList<VersionLibrary> Libraries { get; init; } = [];
 public VersionAssetIndex AssetIndex { get; init; } = new("");
 public VersionJavaVersion JavaVersion { get; init; } = new("17");
}
public sealed record VersionArguments(IReadOnlyList<ArgumentRule> Jvm, IReadOnlyList<ArgumentRule> Game);
public sealed record VersionAssetIndex(string Id);
public sealed record VersionJavaVersion(string Component);
public sealed record VersionLibrary(string Name, string? OsName = null)
{
 public bool AppliesToCurrentOs()
 {
  if (OsName is null) return true;
  var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
  return string.Equals(OsName, os, StringComparison.OrdinalIgnoreCase);
 }
}