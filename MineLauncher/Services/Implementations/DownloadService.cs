using System.Net.Http.Json;
using System.Security.Cryptography;
using MineLauncher.Services.Abstractions;

namespace MineLauncher.Services.Implementations;

/// <summary>
/// Единый HTTP-загрузчик: retry с экспоненциальной задержкой, SHA-1 верификация,
/// атомарная запись (через .part-файл), корректная работа с кириллическими URL
/// (Uri автоматически percent-encodes не-ASCII символы).
/// </summary>
public sealed class DownloadService : IDownloadService
{
    private readonly HttpClient _http;
    private const int MaxRetries = 3;

    public DownloadService(HttpClient http) => _http = http;

    public async Task DownloadFileAsync(
        string url, string destinationPath, IProgress<double>? progress = null,
        string? expectedSha1 = null, long? expectedSize = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        string partPath = destinationPath + ".part";

        // Если файл уже на месте и валиден — не качаем повторно.
        if (File.Exists(destinationPath) && expectedSha1 is not null &&
            await Sha1MatchesAsync(destinationPath, expectedSha1, ct))
            return;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var resp = await _http.GetAsync(
                    new Uri(url), HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();

                long total = resp.Content.Headers.ContentLength ?? expectedSize ?? -1;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(partPath);

                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report(done * 100.0 / total);
                }
                dst.Flush();
                break; // успех — выходим из retry-цикла
            }
            catch (Exception ex) when (attempt < MaxRetries && ex is not OperationCanceledException)
            {
                // Retry: сетевые сбои/429/5xx. Пауза растёт: 1s, 2s, 4s...
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }

        if (expectedSha1 is not null && !await Sha1MatchesAsync(partPath, expectedSha1, ct))
        {
            File.Delete(partPath);
            throw new InvalidDataException($"SHA-1 не совпал для {url}. Файл повреждён — удалён.");
        }

        File.Move(partPath, destinationPath, overwrite: true); // атомарная публикация файла
    }

    public async Task<T> GetJsonAsync<T>(string url, CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<T>(url,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web), ct)
            ?? throw new InvalidOperationException($"Пустой ответ от {url}");
        return result;
    }

    private static async Task<bool> Sha1MatchesAsync(string path, string sha1, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant() == sha1.ToLowerInvariant();
    }
}
