using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MineLauncher.Models;
using MineLauncher.Services.Abstractions;

namespace MineLauncher.Services.Implementations;

/// <summary>
/// Оркестратор запуска. Сам формат командной строки собирает LaunchCommandBuilder,
/// здесь — только подготовка зависимостей и жизненный цикл процесса.
/// </summary>
public sealed class LauncherService : ILauncherService, IDisposable
{
    private readonly IJavaManager _java;
    private readonly ILoaderProviderFactory _loaders;
    private readonly IVersionManifestService _manifest;
    private readonly Channel<string> _logChannel =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false });

    private Process? _gameProcess;

    public LauncherService(IJavaManager java, ILoaderProviderFactory loaders,
                           IVersionManifestService manifest)
        => (_java, _loaders, _manifest) = (java, loaders, manifest);

    public bool IsGameRunning => _gameProcess is { HasExited: false };

    public IAsyncEnumerable<string> GameOutput => _logChannel.Reader.ReadAllAsync();

    public async Task<LaunchResult> LaunchAsync(
        LaunchOptions options, IProgress<LaunchPhase> progress, CancellationToken ct = default)
    {
        if (IsGameRunning)
            return LaunchResult.Fail("Игра уже запущена.");

        var instance = options.Instance;
        try
        {
            // 1. Профиль версии (vanilla или смёрженный с загрузчиком)
            progress.Report(new(LaunchPhaseKind.VersionManifest, "Проверка версии игры…", 5));
            string versionId = instance.Loader == LoaderType.Vanilla
                ? instance.MinecraftVersion
                : $"{instance.MinecraftVersion}-{instance.Loader.ToString().ToLowerInvariant()}-{instance.LoaderVersion}";

            var profilePath = Path.Combine(
                LauncherPaths.VersionsDir, versionId, $"{versionId}.json");

            // 2. При необходимости устанавливаем загрузчик
            if (!File.Exists(profilePath) && instance.Loader != LoaderType.Vanilla)
            {
                progress.Report(new(LaunchPhaseKind.Loader, "Установка загрузчика…", 15));
                var provider = _loaders.GetProvider(instance.Loader);
                await provider.InstallAsync(instance.MinecraftVersion, null, ct);
            }

            // 3. Java: required major -> поиск -> скачивание в runtime/
            progress.Report(new(LaunchPhaseKind.Java, "Проверка Java…", 30));
            var runtime = await _java.EnsureRuntimeAsync(instance.MinecraftVersion, null, ct);

            // 4. Библиотеки и ассеты (пропущенные по SHA-1 файлы докачиваются)
            progress.Report(new(LaunchPhaseKind.Libraries, "Проверка библиотек…", 45));
            var versionJson = await _manifest.GetVersionAsync(versionId, ct);
            await _manifest.DownloadLibrariesAsync(versionJson, p =>
                progress.Report(new(LaunchPhaseKind.Libraries, "Загрузка библиотек…", 45 + p * 0.3)), ct);

            progress.Report(new(LaunchPhaseKind.Assets, "Проверка ассетов…", 78));
            await _manifest.DownloadAssetsAsync(versionJson, p =>
                progress.Report(new(LaunchPhaseKind.Assets, "Загрузка ассетов…", 78 + p * 0.15)), ct);

            // 5. Генерация аргументов и запуск
            progress.Report(new(LaunchPhaseKind.Launch, "Запуск Minecraft…", 96));
            var args = LaunchCommandBuilder.Build(options, versionJson, runtime.ExecutablePath,
                assetsRoot: LauncherPaths.AssetsDir, librariesRoot: LauncherPaths.LibrariesDir,
                gameDir: instance.GameDirectory);

            var psi = new ProcessStartInfo
            {
                FileName = runtime.ExecutablePath,   // пробелы/кириллица в пути безопасны:
                WorkingDirectory = instance.GameDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8, // важно для кириллицы в логах
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            foreach (var a in args) psi.ArgumentList.Add(a); // ArgumentList экранирует сам

            _gameProcess = Process.Start(psi)
                ?? throw new InvalidOperationException("Не удалось запустить процесс Java.");

            PipeToChannel(_gameProcess.StandardOutput);
            PipeToChannel(_gameProcess.StandardError);

            await _gameProcess.WaitForExitAsync(ct);
            int exit = _gameProcess.ExitCode;
            progress.Report(new(LaunchPhaseKind.Launch,
                exit == 0 ? "Игра завершилась штатно." : $"Игра упала с кодом {exit}.", 100));
            return exit == 0 ? LaunchResult.Ok(exit)
                             : LaunchResult.Fail($"Minecraft завершился с кодом {exit}");
        }
        catch (OperationCanceledException)
        {
            await KillGameAsync();
            return LaunchResult.Fail("Запуск отменён.");
        }
        catch (HttpRequestException ex)
        {
            return LaunchResult.Fail($"Сетевая ошибка при подготовке файлов: {ex.Message}");
        }
        catch (Exception ex)
        {
            return LaunchResult.Fail(ex.Message);
        }
    }

    private void PipeToChannel(StreamReader reader)
    {
        _ = Task.Run(async () =>
        {
            while (await reader.ReadLineAsync() is { } line)
                _logChannel.Writer.TryWrite(line);      // UI читает через GameOutput
        });
    }

    public async Task KillGameAsync()
    {
        if (_gameProcess is { HasExited: false } p)
        {
            try { p.Kill(entireProcessTree: true); }
            catch { /* уже умер */ }
            await p.WaitForExitAsync();
        }
    }

    public void Dispose() => _gameProcess?.Dispose();
}
