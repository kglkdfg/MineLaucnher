using MineLauncher.Models;
using MineLauncher.Services.Abstractions;

namespace MineLauncher.Services.Implementations;

/// <summary>
/// Абстрактный пример установки загрузчика. Конкретные источники:
///  - Fabric:   https://meta.fabricmc.net/v2/versions/installer + launcher.json
///  - Quilt:    https://meta.quiltmc.org/v3/... (структура идентична Fabric)
///  - Forge:    maven-metadata.xml с Maven Central + запуск installer.jar в headless-режиме
///  - NeoForge: как Forge, но другой groupId (net.neoforged)
/// Общий паттерн: получить "profile json" загрузчика -> смёржить libraries/mainClass
/// с vanilla version.json -> сохранить как versions/{mc}-{loader}/....json.
/// </summary>
public abstract class LoaderProviderBase : ILoaderProvider
{
    protected IDownloadService Download { get; }
    protected IVersionManifestService Manifest { get; }

    protected LoaderProviderBase(IDownloadService download, IVersionManifestService manifest)
    {
        Download = download;
        Manifest = manifest;
    }

    public abstract LoaderType LoaderType { get; }

    /// <summary>Канонический путь профиля: versions/{versionId}/{versionId}.json</summary>
    protected string ProfilePath(string versionId) => Path.Combine(
        LauncherPaths.VersionsDir, versionId, $"{versionId}.json");

    public Task<bool> IsInstalledAsync(string minecraftVersion, CancellationToken ct = default)
        => Task.Run(() => File.Exists(ProfilePath(BuildVersionId(minecraftVersion))), ct);

    public async Task<LoaderProfile> InstallAsync(
        string minecraftVersion,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        // 1. Запросить у конкретного загрузчика его "сырой" профиль (шаблонный метод)
        var loaderJson = await FetchLoaderProfileAsync(minecraftVersion, ct);

        // 2. Смёржить с vanilla: наследуем mainClass/libraries/assetIndex если их нет
        var vanilla = await Manifest.GetVersionAsync(minecraftVersion, ct);
        var merged = MergeProfiles(vanilla, loaderJson);

        // 3. Сохранить результат в виде стандартного version.json — дальше
        //    LaunchCommandBuilder работает с ним универсально для всех загрузчиков.
        string versionId = BuildVersionId(minecraftVersion);
        string path = ProfilePath(versionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, merged.ToString(), ct);

        // 4. Скачать библиотеки загрузчика (пропускаем уже существующие по SHA-1)
        var libs = ExtractLibraries(merged);
        for (int i = 0; i < libs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var lib = libs[i];
            string target = MavenPathResolver.Resolve(LauncherPaths.LibrariesDir, lib.Name);
            if (!NeedsDownload(target, lib.Sha1)) continue;

            await Download.DownloadFileAsync(lib.Url, target,
                expectedSha1: lib.Sha1, expectedSize: lib.Size, ct: ct);
            progress?.Report((i + 1) / (double)libs.Count * 100);
        }

        return new LoaderProfile(
            LoaderType, versionId, minecraftVersion,
            MainClass: GetString(merged, "mainClass")!,
            ExtraJvmArgs: GetStringArray(merged, "arguments", "jvm"),
            Libraries: libs);
    }

    // -------- шаблонные методы, реализуемые наследниками --------

    /// <summary>Например "1.20.1-forge-47.2.0".</summary>
    protected abstract string BuildVersionId(string minecraftVersion);

    /// <summary>Получить JSON профиля загрузчика из официального источника.</summary>
    protected abstract Task<System.Text.Json.Nodes.JsonObject> FetchLoaderProfileAsync(
        string minecraftVersion, CancellationToken ct);

    // -------- общий merge-код (упрощённо) --------

    private static System.Text.Json.Nodes.JsonObject MergeProfiles(
        VersionJson vanilla, System.Text.Json.Nodes.JsonObject loader)
    {
        // Профиль загрузчика может не указывать assetIndex/inheritsVanilla — дополняем.
        if (!loader.ContainsKey("assetIndex"))
            loader["assetIndex"] = vanilla.AssetIndex.Id;
        if (!loader.ContainsKey("javaVersion"))
            loader["javaVersion"] = vanilla.JavaVersion.Component;
        return loader;
    }

    private bool NeedsDownload(string path, string? sha1)
    {
        if (!File.Exists(path)) return true;
        if (string.IsNullOrEmpty(sha1)) return false;
        using var fs = File.OpenRead(path);
        string actual = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(fs))
                                     .ToLowerInvariant();
        return actual != sha1; // файл повреждён — перекачиваем
    }

    // helpers для работы с JsonObject опущены ради краткости ...
    private static string? GetString(System.Text.Json.Nodes.JsonObject o, string key)
        => o[key]?.GetValue<string>();
    private static IReadOnlyList<string> GetStringArray(
        System.Text.Json.Nodes.JsonObject o, params string[] path) => [];
    private static IReadOnlyList<LibraryInfo> ExtractLibraries(
        System.Text.Json.Nodes.JsonObject o) => [];
}

/// <summary>Fabric — самый простой случай: meta API отдаёт готовый launcher profile.</summary>
public sealed class FabricLoaderProvider : LoaderProviderBase
{
    private readonly string _selectedLoaderVersion; // например "0.15.11"

    public FabricLoaderProvider(IDownloadService d, IVersionManifestService m, string loaderVersion)
        : base(d, m) => _selectedLoaderVersion = loaderVersion;

    public override LoaderType LoaderType => LoaderType.Fabric;

    protected override string BuildVersionId(string mc) => $"{mc}-fabric-{_selectedLoaderVersion}";

    protected override async Task<System.Text.Json.Nodes.JsonObject> FetchLoaderProfileAsync(
        string minecraftVersion, CancellationToken ct)
    {
        // Официальный meta-эндпоинт Fabric:
        string url = $"https://meta.fabricmc.net/v2/versions/loader/" +
                     $"{minecraftVersion}/{_selectedLoaderVersion}/profile/json";
        return System.Text.Json.Nodes.JsonNode.Parse(
            await Download.GetJsonAsync<string>(url, ct) is string s ? s
                 : await new HttpClient().GetStringAsync(url, ct))!.AsObject();
    }
}

/// <summary>
/// Forge: официальный путь — скачать installer.jar и выполнить
/// `java -jar forge-installer.jar --installClient <dir>`, который сам
/// кладёт libraries и пишет versions/1.20.1-forge-47.2.0.json.
/// </summary>
public sealed class ForgeLoaderProvider : LoaderProviderBase
{
    private const string ForgeMavenMeta =
        "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";

    public ForgeLoaderProvider(IDownloadService d, IVersionManifestService m) : base(d, m) { }

    public override LoaderType LoaderType => LoaderType.Forge;

    protected override string BuildVersionId(string mc) => mc + "-forge"; // уточняется на этапе Resolve

    protected override Task<System.Text.Json.Nodes.JsonObject> FetchLoaderProfileAsync(
        string minecraftVersion, CancellationToken ct)
    {
        // 1. Парсим maven-metadata.xml -> находим версию вида "1.20.1-47.2.0"
        // 2. Скачиваем installer:
        //    https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.0/
        //        forge-1.20.1-47.2.0-installer.jar
        // 3. Запускаем инсталлятор headless от имени JavaManager-рантайма:
        //    java -jar installer.jar --installClient "<LauncherPaths.Root>"
        // 4. После установки читаем созданный versions/{id}.json и возвращаем его.
        throw new NotImplementedException("См. описание шагов выше.");
    }
}

/// <summary>Небольшой helper: maven coordinates -> путь на диске.</summary>
public static class MavenPathResolver
{
    // "com.mojang:patchy:1.3.9" -> libraries/com/mojang/patchy/1.3.9/patchy-1.3.9.jar
    public static string Resolve(string librariesRoot, string coords)
    {
        var p = coords.Split(':');
        string group = p[0].Replace('.', '/');
        string artifact = p[1], version = p.Length > 3 && p[2] == "" ? p[3] : p[^1];
        if (p.Length == 4) artifact = p[1]; // classifier handled simply
        string file = $"{artifact}-{version}.jar";
        return Path.Combine(librariesRoot, group, artifact, version, file);
    }
}
