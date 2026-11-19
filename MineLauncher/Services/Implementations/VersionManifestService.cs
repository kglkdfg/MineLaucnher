using System.Text.Json;
using System.Text.Json.Nodes;
using MineLauncher.Models;
using MineLauncher.Services.Abstractions;

namespace MineLauncher.Services.Implementations;

/// <summary>Официальная реализация Vanilla pipeline на базе Mojang version_manifest_v2.</summary>
public sealed class VersionManifestService : IVersionManifestService
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDownloadService _download;
    private readonly HttpClient _http;
    private readonly string _root;

    public VersionManifestService(IDownloadService download, HttpClient http)
        : this(download, http, LauncherPaths.Root) { }

    public VersionManifestService(IDownloadService download, HttpClient http, string root)
        => (_download, _http, _root) = (download, http, Path.GetFullPath(root));

    public async Task<IReadOnlyList<string>> GetReleaseListAsync(CancellationToken ct = default)
    {
        var manifest = await _download.GetJsonAsync<MojangManifest>(ManifestUrl, ct);
        return manifest.Versions.Where(v => v.Type == "release").Select(v => v.Id).ToArray();
    }

    public async Task<VersionJson> GetVersionAsync(string versionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        if (versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || versionId.Contains("..") || versionId.Contains('/') || versionId.Contains('\\'))
            throw new ArgumentException("Invalid Minecraft version id.", nameof(versionId));

        var manifest = await _download.GetJsonAsync<MojangManifest>(ManifestUrl, ct);
        var entry = manifest.Versions.FirstOrDefault(v => v.Id == versionId)
            ?? throw new KeyNotFoundException($"Minecraft version '{versionId}' is not in Mojang's manifest.");
        var metadata = await _download.GetJsonAsync<JsonObject>(entry.Url, ct);
        var version = ParseVersion(metadata);
        if (!string.Equals(version.Id, versionId, StringComparison.Ordinal))
            throw new InvalidDataException("Version metadata id does not match the manifest entry.");

        var versionDir = Path.Combine(_root, "versions", versionId);
        Directory.CreateDirectory(versionDir);
        var jsonPath = Path.Combine(versionDir, versionId + ".json");
        await File.WriteAllTextAsync(jsonPath, metadata.ToJsonString(JsonOptions), ct);
        if (version.ClientJarUrl is not null)
            await _download.DownloadFileAsync(version.ClientJarUrl,
                Path.Combine(versionDir, versionId + ".jar"), expectedSha1: version.ClientJarSha1,
                expectedSize: version.ClientJarSize, ct: ct);
        return version;
    }

    public async Task DownloadLibrariesAsync(VersionJson version, Action<double>? progress = null, CancellationToken ct = default)
    {
        var selected = version.Libraries.Where(l => l.AppliesToCurrentOs() && !string.IsNullOrWhiteSpace(l.Url)).ToArray();
        for (var i = 0; i < selected.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var lib = selected[i];
            var relative = lib.Classifier is null
                ? MavenPathResolver.ToRelativePath(lib.Name)
                : ClassifierPath(lib.Name, lib.Classifier);
            var target = SafeCombine(Path.Combine(_root, "libraries"), relative);
            await _download.DownloadFileAsync(lib.Url!, target, expectedSha1: lib.Sha1, expectedSize: lib.Size, ct: ct);
            progress?.Invoke((i + 1d) / Math.Max(1, selected.Length) * 100);
        }
    }

    public async Task DownloadAssetsAsync(VersionJson version, Action<double>? progress = null, CancellationToken ct = default)
    {
        if (version.AssetIndex.Url is null) return;
        var indexPath = Path.Combine(_root, "assets", "indexes", version.AssetIndex.Id + ".json");
        await _download.DownloadFileAsync(version.AssetIndex.Url, indexPath,
            expectedSha1: version.AssetIndex.Sha1, expectedSize: version.AssetIndex.Size, ct: ct);
        var index = await File.ReadAllTextAsync(indexPath, ct);
        var parsed = JsonNode.Parse(index) ?? throw new InvalidDataException("Invalid asset index.");
        var objects = parsed["objects"]?.AsObject() ?? throw new InvalidDataException("Asset index has no objects.");
        var entries = objects.Select(x => (Name: x.Key, Hash: (string?)x.Value?["hash"], Size: (long?)x.Value?["size"]))
            .Where(x => x.Hash is { Length: 40 }).ToArray();
        for (var i = 0; i < entries.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = entries[i];
            var hash = item.Hash!;
            var relative = Path.Combine(hash[..2], hash);
            await _download.DownloadFileAsync("https://resources.download.minecraft.net/" + hash[..2] + "/" + hash,
                SafeCombine(Path.Combine(_root, "assets", "objects"), relative), expectedSha1: hash,
                expectedSize: item.Size, ct: ct);
            progress?.Invoke((i + 1d) / Math.Max(1, entries.Length) * 100);
        }
    }

    public static VersionJson ParseVersion(JsonObject json)
    {
        var id = (string?)json["id"] ?? throw new InvalidDataException("Version metadata has no id.");
        var libs = new List<VersionLibrary>();
        foreach (var item in json["libraries"]?.AsArray() ?? [])
        {
            if (item is not JsonObject lib) continue;
            var name = (string?)lib["name"];
            if (string.IsNullOrWhiteSpace(name)) continue;
            var os = (string?)lib["rules"]?.AsArray().LastOrDefault(x => (string?)x?["action"] == "allow")?["os"]?["name"];
            var artifact = lib["downloads"]?["artifact"];
            if (artifact is not null)
                libs.Add(new(name, os, (string?)artifact["url"], (string?)artifact["sha1"], (long?)artifact["size"]));
            var natives = lib["natives"]?[CurrentOsKey()];
            if (natives is not null)
            {
                var classifier = ((string?)natives)?.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                var nativeArtifact = lib["downloads"]?["classifiers"]?[classifier!];
                if (classifier is not null && nativeArtifact is not null)
                    libs.Add(new(name, os, (string?)nativeArtifact["url"], (string?)nativeArtifact["sha1"],
                        (long?)nativeArtifact["size"], classifier));
            }
        }
        var args = json["arguments"];
        var jvm = ParseArguments(args?["jvm"]);
        var game = ParseArguments(args?["game"]);
        if (args is null && (string?)json["minecraftArguments"] is string legacy)
            game = [new ArgumentRule(legacy.Split(' ', StringSplitOptions.RemoveEmptyEntries), [])];
        var asset = json["assetIndex"];
        var client = json["downloads"]?["client"];
        return new VersionJson
        {
            Id = id, Type = (string?)json["type"] ?? "release",
            MainClass = (string?)json["mainClass"] ?? throw new InvalidDataException("Version metadata has no mainClass."),
            Arguments = new(jvm, game), Libraries = libs,
            AssetIndex = new((string?)asset?["id"] ?? (string?)json["assets"] ?? "legacy",
                (string?)asset?["url"], (string?)asset?["sha1"], (long?)asset?["size"]),
            JavaVersion = new((string?)json["javaVersion"]?["component"] ?? "17"),
            ClientJarUrl = (string?)client?["url"], ClientJarSha1 = (string?)client?["sha1"],
            ClientJarSize = (long?)client?["size"],
            MinimumVersion = (int?)json["minimumLauncherVersion"] ?? 0
        };
    }

    private static IReadOnlyList<ArgumentRule> ParseArguments(JsonNode? node)
    {
        var result = new List<ArgumentRule>();
        if (node is not JsonArray arr) return result;
        foreach (var entry in arr)
        {
            if (entry is JsonValue scalar && scalar.TryGetValue<string>(out var text))
                result.Add(new([text], []));
            else if (entry is JsonObject obj)
            {
                var values = obj["value"] is JsonArray va
                    ? va.Select(x => (string?)x).Where(x => x is not null).Cast<string>().ToArray()
                    : new[] { (string?)obj["value"] }.Where(x => x is not null).Cast<string>().ToArray();
                var rules = new List<OsRule>();
                foreach (var rule in obj["rules"]?.AsArray() ?? [])
                {
                    var os = rule?["os"];
                    rules.Add(new((string?)os?["name"], (string?)os?["version"], (string?)os?["arch"]));
                }
                result.Add(new(values, rules));
            }
        }
        return result;
    }

    private static string CurrentOsKey() => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
    private static string ClassifierPath(string coordinates, string classifier)
    {
        var parts = coordinates.Split(':');
        if (parts.Length < 3) throw new InvalidDataException("Invalid Maven coordinates: " + coordinates);
        var version = parts[^1];
        var artifact = parts[1];
        return Path.Combine(parts[0].Replace('.', Path.DirectorySeparatorChar), artifact, version,
            artifact + "-" + version + "-" + classifier + ".jar");
    }
    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Resolved download path escapes its root.");
        return full;
    }

    private sealed class MojangManifest { public List<MojangVersion> Versions { get; set; } = []; }
    private sealed class MojangVersion { public string Id { get; set; } = ""; public string Type { get; set; } = ""; public string Url { get; set; } = ""; }
}
