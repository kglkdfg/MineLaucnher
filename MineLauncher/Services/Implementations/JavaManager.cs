using System.Text.RegularExpressions;
using MineLauncher.Models;
using MineLauncher.Services.Abstractions;

namespace MineLauncher.Services.Implementations;

/// <summary>
/// Определение требуемой версии Java и управление изолированными рантаймами.
/// Логика опирается на данные Mojang (javaVersion.component в version.json),
/// но содержит fallback по диапазону версий — как это делает официальный лаунчер.
/// </summary>
public sealed partial class JavaManager : IJavaManager
{
    private readonly IDownloadService _download;
    private readonly HttpClient _http;

    public JavaManager(IDownloadService download, HttpClient http)
    {
        _download = download;
        _http = http;
    }

    // ===================== Правило выбора версии Java =====================

    public int GetRequiredJavaMajorVersion(string minecraftVersionId)
    {
        // Точная таблица соответствия (по official manifests Mojang):
        //   MC <= 1.16.5            -> Java 8
        //   1.17                     -> Java 16 (принудительно в 1.17.1+ -> 17)
        //   1.18 – 1.20.4            -> Java 17
        //   1.20.5+                  -> Java 21
        //   Snapshot/Pre-release     -> парсим номер minors из "23w31a"-подобных id
        if (TryParseRelease(minecraftVersionId, out var minor, out var patch))
            return MapRelease(minor, patch);

        if (TryParseSnapshot(minecraftVersionId, out var year, out var week))
            return MapSnapshot(year, week);

        // Консервативный default для неизвестных id — новая Java не сломает старые версии так,
        // как старая Java сломает новые.
        return 21;

        static int MapRelease(int minor, int patch) => minor switch
        {
            <= 16 => 8,
            17 or 18 or 19 => 17,
            20 => patch >= 5 ? 21 : 17,
            _ => 21
        };
    }

    private static int MapSnapshot(int year, int week)
    {
        // Снимки используют ту же Java, что и следующий релиз:
        // 21w* / 1.* снапшоты до 1.17 -> 8; 22w*/23w* -> 17/21 по году.
        if (year < 21 || (year == 21 && week < 11)) return 8;
        if (year == 21) return 16;
        if (year <= 23) return 17;
        return 21;
    }

    [GeneratedRegex(@"^1\.(\d{1,2})(?:\.(\d{1,2}))?")]
    private static partial Regex ReleasePattern();

    [GeneratedRegex(@"^(\d{2})w(\d{2})[a-z]$")]
    private static partial Regex SnapshotPattern();

    private static bool TryParseRelease(string id, out int minor, out int patch)
    {
        minor = patch = -1;
        var m = ReleasePattern().Match(id);
        if (!m.Success) return false;
        minor = int.Parse(m.Groups[1].Value);
        patch = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        return true;
    }

    private static bool TryParseSnapshot(string id, out int year, out int week)
    {
        year = week = -1;
        var m = SnapshotPattern().Match(id);
        if (!m.Success) return false;
        year = 2000 + int.Parse(m.Groups[1].Value);
        week = int.Parse(m.Groups[2].Value);
        return true;
    }

    // ===================== Поиск установленного JDK =====================

    public async Task<JavaRuntimeInfo?> FindCompatibleRuntimeAsync(
        int requiredMajor, CancellationToken ct = default)
    {
        // 1. Сначала смотрим в изолированных рантаймах лаунчера (.minecraft/runtime/jre-17)
        var managedDir = Path.Combine(LauncherPaths.RuntimeDir, $"jre-{requiredMajor}");
        var managedExe = FindJavaExecutable(managedDir);
        if (managedExe is not null && await VerifyMajorAsync(managedExe, requiredMajor, ct))
            return new JavaRuntimeInfo(managedExe, requiredMajor, IsManagedByLauncher: true);

        // 2. Системные установки: JAVA_HOME, PATH, типичные директории Adoptium/Oracle
        foreach (var candidate in EnumerateSystemCandidates())
        {
            if (await VerifyMajorAsync(candidate, requiredMajor, ct))
                return new JavaRuntimeInfo(candidate, requiredMajor, IsManagedByLauncher: false);
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSystemCandidates()
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var p = FindJavaExecutable(javaHome);
            if (p is not null) yield return p;
        }

        string[] roots = OperatingSystem.IsWindows()
            ? [
                @"C:\Program Files\Eclipse Adoptium",
                @"C:\Program Files\Java",
                @"C:\Program Files\Microsoft"
              ]
            : ["/usr/lib/jvm", "/Library/Java/JavaVirtualMachines"];

        foreach (var root in roots.Where(Directory.Exists))
        foreach (var sub in Directory.EnumerateDirectories(root))
        {
            var exe = FindJavaExecutable(sub);
            if (exe is not null) yield return exe;
        }
    }

    /// <summary>Корректно обрабатывает пути с пробелами и кириллицей — только Path.Combine.</summary>
    private static string? FindJavaExecutable(string home)
    {
        var exe = OperatingSystem.IsWindows()
            ? Path.Combine(home, "bin", "java.exe")
            : Path.Combine(home, "bin", "java");
        return File.Exists(exe) ? exe : null;
    }

    private static async Task<bool> VerifyMajorAsync(string javaExe, int expected, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = javaExe,
                RedirectStandardError = true,           // `java -version` пишет в stderr!
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-version");           // ArgumentList сам экранирует пробелы в пути

            using var proc = Process.Start(psi)!;
            string output = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            // "openjdk version \"17.0.9\" ..." или "\"1.8.0_392\""
            var m = Regex.Match(output, @"version ""(?:1\.)?(\d+)");
            return m.Success && int.Parse(m.Groups[1].Value) == expected;
        }
        catch
        {
            return false; // повреждённый рантайм — просто пропускаем
        }
    }

    // ===================== Скачивание JRE (Adoptium Temurin) =====================

    public async Task<JavaRuntimeInfo> EnsureRuntimeAsync(
        string minecraftVersionId,
        IProgress<double>? downloadProgress = null,
        CancellationToken ct = default)
    {
        int required = GetRequiredJavaMajorVersion(minecraftVersionId);

        var existing = await FindCompatibleRuntimeAsync(required, ct);
        if (existing is not null) return existing;

        // API Adoptium отдаёт последний GA release нужной мажорной версии для текущей ОС/архитектуры.
        string os = OperatingSystem.IsWindows() ? "windows"
                  : OperatingSystem.IsMacOS() ? "mac" : "linux";
        string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => throw new PlatformNotSupportedException("Unsupported CPU architecture.")
        };
        string url =
            $"https://api.adoptium.net/v3/binary/latest/{required}/ga/{os}/{arch}/jre/hotspot/normal/eclipse";

        string targetDir = Path.Combine(LauncherPaths.RuntimeDir, $"jre-{required}");
        string archivePath = Path.Combine(LauncherPaths.RuntimeDir, OperatingSystem.IsWindows() ? $"jre-{required}.zip" : $"jre-{required}.tar.gz");
        Directory.CreateDirectory(LauncherPaths.RuntimeDir);

        await _download.DownloadFileAsync(url, archivePath, downloadProgress, ct: ct);

        try
        {
            // SharpZipLib умеет и tar.gz, и zip; внутри архива — корневая папка jdk-17.x+y-jre
            await ExtractArchiveAsync(archivePath, targetDir, ct);
        }
        catch (Exception ex)
        {
            // Повреждённый архив: удаляем и пробujemy ещё раз при следующем запуске
            TryDelete(archivePath);
            throw new InvalidOperationException($"Не удалось распаковать JRE: {ex.Message}", ex);
        }
        finally
        {
            TryDelete(archivePath);
        }

        var exe = FindJavaExecutable(targetDir)
                  ?? FindJavaExecutable(Path.Combine(targetDir,
                        // у Adoptium содержимое часто вложенное — ищем на уровень глубже
                        Directory.EnumerateDirectories(targetDir).FirstOrDefault() ?? ""))
                  ?? throw new InvalidOperationException("java executable не найден после распаковки.");

        return new JavaRuntimeInfo(exe, required, IsManagedByLauncher: true);
    }

    private static Task ExtractArchiveAsync(string archive, string target, CancellationToken ct)
        => Task.Run(() =>
        {
            Directory.CreateDirectory(target);
            var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
            void ExtractTar(Stream input)
            {
                using var reader = new System.Formats.Tar.TarReader(input);
                System.Formats.Tar.TarEntry? entry;
                while ((entry = reader.GetNextEntry()) is not null)
                {
                    ct.ThrowIfCancellationRequested();
                    var destination = Path.GetFullPath(Path.Combine(target, entry.Name.Replace('/', Path.DirectorySeparatorChar)));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Archive path escapes the Java runtime directory.");
                    if (entry.EntryType == System.Formats.Tar.TarEntryType.Directory) { Directory.CreateDirectory(destination); continue; }
                    if (entry.EntryType is not (System.Formats.Tar.TarEntryType.RegularFile or System.Formats.Tar.TarEntryType.V7RegularFile)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using var output = File.Create(destination);
                    entry.DataStream?.CopyTo(output);
                }
            }
            if (archive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                using var file = File.OpenRead(archive);
                using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
                ExtractTar(gzip);
            }
            else
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var destination = Path.GetFullPath(Path.Combine(target, entry.FullName));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("ZIP entry escapes the Java runtime directory.");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    System.IO.Compression.ZipFileExtensions.ExtractToFile(entry, destination, overwrite: true);
                }
            }
        }, ct);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
