using MineLauncher.Models;

namespace MineLauncher.Services.Implementations;

/// <summary>
/// Генерация командной строки запуска по правилам Mojang (arguments из version.json).
/// Все пути подставляются как отдельные элементы ArgumentList — экранирование
/// (пробелы, кириллица) берёт на себя ProcessStartInfo, поэтому здесь НЕ нужно
/// вручную оборачивать значения в кавычки.
/// </summary>
public static class LaunchCommandBuilder
{
    public static IReadOnlyList<string> Build(
        LaunchOptions options,
        VersionJson version,
        string javaPath,
        string assetsRoot,
        string librariesRoot,
        string gameDir)
    {
        var session = options.Session;
        bool isLegacyAuth = version.MinimumVersion <= 0; // пре-1.7.2: аргумент --session вместо ${auth_*}

        var args = new List<string>();

        // ---- JVM-аргументы ----
        args.AddRange(options.MemoryArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        foreach (var rule in version.Arguments.Jvm)          // правила из version.json
            args.AddRange(rule.Resolve(BuildPlaceholders(version, session, assetsRoot,
                librariesRoot, gameDir, options)));

        if (!args.Contains("-Dlog4j2.formatMsgNoLookups=true") && NeedsLog4ShellFix(version))
            args.Add("-Dlog4j2.formatMsgNoLookups=true");    // защита для 1.7–1.17 без патча

        // ---- main class ----
        args.Add(version.MainClass);                          // net.minecraft.client.main.Main / cpw.mods...

        // ---- game-аргументы ----
        foreach (var rule in version.Arguments.Game)
            args.AddRange(rule.Resolve(BuildPlaceholders(version, session, assetsRoot,
                librariesRoot, gameDir, options)));

        if (isLegacyAuth)
        {
            args.Add("--session");
            args.Add(session.AccessToken);                    // legacy-формат сессии
        }

        // ---- пользовательские доп. аргументы и размер окна ----
        args.AddRange(options.ExtraGameArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        args.Add("--width");  args.Add(options.WindowWidth.ToString());
        args.Add("--height"); args.Add(options.WindowHeight.ToString());

        return args;
    }

    /// <summary>Словарь подстановок ${placeholder} → значения.</summary>
    private static Dictionary<string, string?> BuildPlaceholders(
        VersionJson v, AuthSession s, string assetsRoot, string libsRoot,
        string gameDir, LaunchOptions o)
    {
        string classpath = string.Join(IO_CLASSPATH_SEPARATOR,
            v.Libraries.Where(l => l.AppliesToCurrentOs() && l.Classifier is null).Select(l => Path.Combine(libsRoot,
                MavenPathResolver.ToRelativePath(l.Name)))
            .Append(Path.Combine(LauncherPaths.VersionsDir, v.Id, $"{v.Id}.jar")));

        return new(StringComparer.Ordinal)
        {
            ["auth_player_name"]  = s.Nickname,
            ["version_name"]      = v.Id,
            ["game_directory"]    = gameDir,
            ["assets_root"]       = assetsRoot,
            ["assets_index_name"] = v.AssetIndex.Id,
            ["auth_uuid"]         = s.Uuid,
            ["auth_access_token"] = s.AccessToken,            // "0" в offline — сервера online-mode это устроят? нет; но локальные — да
            ["user_type"]         = s.Mode == AuthMode.Online ? "msa" : "legacy",
            ["version_type"]      = v.Type,                   // "release" / "snapshot"
            ["natives_directory"] = Path.Combine(LauncherPaths.VersionsDir, v.Id, "natives"),
            ["launcher_name"]     = "MineLauncher",
            ["launcher_version"]  = "1.0.0",
            ["classpath"]         = classpath,
            ["user_properties"]   = "{}",
            ["game_assets"]       = assetsRoot,               // legacy-плейсхолдер
            ["resolution_width"]  = o.WindowWidth.ToString(),
            ["resolution_height"] = o.WindowHeight.ToString(),
        };
    }

    // Windows использует ';' в -cp, Unix — ':'
    private static readonly string IO_CLASSPATH_SEPARATOR =
        OperatingSystem.IsWindows() ? ";" : ":";

    private static bool NeedsLog4ShellFix(VersionJson v) =>
        v.MinecraftVersionMinor is >= 7 and <= 18 ||
        (v.MinecraftVersionMinor == 12 && v.ReleasePatch >= 9); // 1.12.9+ backport
}

/// <summary>Разрешение правил OS/features из modern version.json (arguments.jvm/game).</summary>
public record ArgumentRule(IReadOnlyList<string> Value, IReadOnlyList<OsRule> Rules)
{
    public IEnumerable<string> Resolve(Dictionary<string, string?> placeholders)
    {
        if (Rules.Count > 0)
        {
            var hasAllow = Rules.Any(r => r.Action == "allow");
            var allowed = !hasAllow;
            foreach (var rule in Rules)
                if (rule.MatchesCurrentEnvironment())
                    allowed = rule.Action == "allow";
            if (!allowed) return [];
        }

        return Value.Select(v => SubstitutionRegex.Replace(v, m =>
            placeholders.TryGetValue(m.Groups[1].Value, out var val) ? val ?? "" : m.Value));
    }

    private static readonly System.Text.RegularExpressions.Regex SubstitutionRegex =
        new(@"\$\{(\w+)\}", System.Text.RegularExpressions.RegexOptions.Compiled);
}

public record OsRule(string? Name, string? Version, string? Arch, string Action = "allow")
{
    public bool MatchesCurrentEnvironment()
    {
        string current = OperatingSystem.IsWindows() ? "windows"
                       : OperatingSystem.IsMacOS() ? "osx" : "linux";
        if (Name is not null && !string.Equals(Name, current, StringComparison.OrdinalIgnoreCase)) return false;
        if (Arch is not null && !System.Text.RegularExpressions.Regex.IsMatch(
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), Arch))
            return false;
        if (Version is not null && !System.Text.RegularExpressions.Regex.IsMatch(Environment.OSVersion.Version.ToString(), Version))
            return false;
        return true;
    }

    public bool AllowsCurrentOs() => Action == "allow" && MatchesCurrentEnvironment();
}
