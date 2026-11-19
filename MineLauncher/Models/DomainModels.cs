namespace MineLauncher.Models;

/// <summary>Типы поддерживаемых загрузчиков.</summary>
public enum LoaderType { Vanilla, Forge, Fabric, NeoForge, Quilt }

/// <summary>Режим аутентификации.</summary>
public enum AuthMode { Online, Offline }

/// <summary>Итог установки загрузчика: путь к main-классу и «профиль» запуска.</summary>
public record LoaderProfile(
    LoaderType Type,
    string VersionId,              // например "1.20.1-forge-47.2.0"
    string MinecraftVersion,
    string MainClass,
    IReadOnlyList<string> ExtraJvmArgs,
    IReadOnlyList<LibraryInfo> Libraries);

public record LibraryInfo(
    string Name,                   // maven coordinates: "com.mojang:patchy:1.3.9"
    string Url,                    // полный URL jar-файла
    string? Sha1,
    long Size,
    bool IsNative);

public record JavaRuntimeInfo(string ExecutablePath, int MajorVersion, bool IsManagedByLauncher);

public record AuthSession(
    AuthMode Mode,
    string Nickname,
    string Uuid,
    string AccessToken,          // в offline — фиктивный "0"
    string? RefreshToken,        // только online
    DateTimeOffset? ExpiresAt);

/// <summary>Модpack/сборка (instance) — изолированный набор версий и модов.</summary>
public class Instance
{
    public required string Id { get; init; }               // папка внутри instances/
    public required string DisplayName { get; set; }
    public string MinecraftVersion { get; set; } = "";
    public LoaderType Loader { get; set; } = LoaderType.Vanilla;
    public string? LoaderVersion { get; set; }             // конкретная версия Forge/Fabric
    public string GameDirectory => Path.Combine(LauncherPaths.Root, "instances", Id);
}

/// <summary>Всё, что нужно для одного запуска игры.</summary>
public record LaunchOptions(
    Instance Instance,
    AuthSession Session,
    string JavaPath,
    string MemoryArgs,           // "-Xmx4G -Xms1G"
    string ExtraGameArgs,        // "--fullscreen --width 1280"
    int WindowWidth = 854,
    int WindowHeight = 480);

public static class LauncherPaths
{
    /// <summary>Корень лаунчера. ВАЖНО: может содержать пробелы и кириллицу!</summary>
    public static string Root { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/MineLauncher";

    public static string VersionsDir   => Path.Combine(Root, "versions");
    public static string LibrariesDir  => Path.Combine(Root, "libraries");
    public static string AssetsDir     => Path.Combine(Root, "assets");
    public static string RuntimeDir    => Path.Combine(Root, "runtime");   // изолированные JRE
    public static string InstancesDir  => Path.Combine(Root, "instances");
}
