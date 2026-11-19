using MineLauncher.Models;

namespace MineLauncher.Services.Abstractions;

/// <summary>
/// Абстракция аутентификации. Реализации: Microsoft OAuth (online) и локальная (offline).
/// Принцип LSP: вызывающий код не знает, какой режим используется.
/// </summary>
public interface IAuthService
{
    /// <summary>Текущее состояние сессии (null — не авторизован).</summary>
    AuthSession? CurrentSession { get; }

    /// <summary>Событие изменения состояния авторизации (для обновления UI).</summary>
    event EventHandler<AuthSession?>? AuthenticationChanged;

    /// <summary>
    /// Выполнить вход. Для online-режима запускает OAuth flow с системным браузером,
    /// для offline — валидирует никнейм локально.
    /// </summary>
    Task<AuthResult> LoginAsync(CancellationToken ct = default);

    /// <summary>Завершить сессию, удалить сохранённые токены из secure storage.</summary>
    Task LogoutAsync();

    /// <summary>
    /// Проверить/обновить access-токен Minecraft по refresh-токену.
    /// В offline-режиме всегда возвращает true (токен фиктивный).
    /// </summary>
    Task<bool> EnsureValidSessionAsync(CancellationToken ct = default);
}

public record AuthResult(bool Success, string? Error, AuthSession? Session)
{
    public static AuthResult Ok(AuthSession s) => new(true, null, s);
    public static AuthResult Fail(string error) => new(false, error, null);
}
