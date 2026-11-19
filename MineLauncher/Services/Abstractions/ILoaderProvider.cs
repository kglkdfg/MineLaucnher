using MineLauncher.Models;

namespace MineLauncher.Services.Abstractions;

/// <summary>
/// Провайдер загрузчика модов (SRP): каждый загрузчик — своя реализация.
/// Регистрация в DI: Keyed services или фабрика по LoaderType.
/// </summary>
public interface ILoaderProvider
{
    LoaderType LoaderType { get; }

    /// <summary>Доступны ли файлы загрузчика для версии (уже установлены).</summary>
    Task<bool> IsInstalledAsync(string minecraftVersion, CancellationToken ct = default);

    /// <summary>
    /// Установить загрузчик: получить profile-json из официального источника
    /// (или надежного зеркала), смёржить с vanilla version.json, скачать libraries.
    /// </summary>
    Task<LoaderProfile> InstallAsync(
        string minecraftVersion,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}

/// <summary>Фабрика провайдеров по типу загрузчика (OCP: новый загрузчик = новый класс).</summary>
public interface ILoaderProviderFactory
{
    ILoaderProvider GetProvider(LoaderType type);
}
