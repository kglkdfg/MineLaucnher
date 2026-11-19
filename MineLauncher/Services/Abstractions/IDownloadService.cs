namespace MineLauncher.Services.Abstractions;

/// <summary>Универсальный загрузчик файлов с retry, SHA-1 проверкой и прогрессом.</summary>
public interface IDownloadService
{
    /// <summary>Скачать файл. Ожидает целевой размер (для прогресса) и опционально SHA-1.</summary>
    Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<double>? progress = null,
        string? expectedSha1 = null,
        long? expectedSize = null,
        CancellationToken ct = default);

    /// <summary>Скачать JSON и десериализовать его.</summary>
    Task<T> GetJsonAsync<T>(string url, CancellationToken ct = default);
}
