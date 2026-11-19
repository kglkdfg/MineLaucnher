using MineLauncher.Models;

namespace MineLauncher.Services.Abstractions;

/// <summary>
/// Менеджер Java-рантаймов: определяет требуемую мажорную версию JDK
/// по версии Minecraft, сканирует систему, скачивает JRE при необходимости.
/// </summary>
public interface IJavaManager
{
    /// <summary>Требуемая мажорная версия Java (8/17/21) для указанной версии Minecraft.</summary>
    int GetRequiredJavaMajorVersion(string minecraftVersionId);

    /// <summary>Найти подходящий java.exe среди установленных и скачанных рантаймов.</summary>
    Task<JavaRuntimeInfo?> FindCompatibleRuntimeAsync(int requiredMajor, CancellationToken ct = default);

    /// <summary>
    /// Убедиться, что подходящий рантайм доступен. Если нет — скачать
    /// (например, Temurin JRE с API Adoptium) в изолированную папку
    /// <c>.minecraft/runtime/jre-{major}</c> и вернуть путь к исполняемому файлу.
    /// </summary>
    Task<JavaRuntimeInfo> EnsureRuntimeAsync(
        string minecraftVersionId,
        IProgress<double>? downloadProgress = null,
        CancellationToken ct = default);
}
