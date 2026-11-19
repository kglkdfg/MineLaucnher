using MineLauncher.Models;

namespace MineLauncher.Services.Abstractions;

/// <summary>
/// Оркестратор запуска: подготовка файлов игры → генерация командной строки →
/// запуск процесса → стриминг лога.
/// </summary>
public interface ILauncherService
{
    /// <summary>Поток строк консоли игры в реальном времени.</summary>
    IAsyncEnumerable<string> GameOutput { get; }

    /// <summary>Игра запущена прямо сейчас.</summary>
    bool IsGameRunning { get; }

    /// <summary>Полный цикл: проверить зависимости, скачать недостающее, запустить процесс.</summary>
    Task<LaunchResult> LaunchAsync(
        LaunchOptions options,
        IProgress<LaunchPhase> progress,
        CancellationToken ct = default);

    /// <summary>Аварийно завершить процесс игры.</summary>
    Task KillGameAsync();
}

public record LaunchResult(bool Success, int ExitCode, string? Error)
{
    public static LaunchResult Ok(int exitCode) => new(true, exitCode, null);
    public static LaunchResult Fail(string err) => new(false, -1, err);
}

/// <summary>Фаза подготовки — для прогресс-бара в UI.</summary>
public record LaunchPhase(LaunchPhaseKind Kind, string Description, double Percent);

public enum LaunchPhaseKind { VersionManifest, Assets, Libraries, Loader, Java, Launch }
