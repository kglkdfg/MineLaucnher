using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using MineLauncher.Models;
using MinecraftAuth;                    // https://github.com/terrakok/MinecraftAuth
using MinecraftAuth.Exceptions;
using MinecraftAuth.Store;

namespace MineLauncher.Services.Implementations;

/// <summary>
/// Online-авторизация через Microsoft (OAuth 2.0 device flow) с помощью библиотеки
/// MinecraftAuth, которая инкапсулирует всю цепочку:
///   MS Device Code -> Xbox Live -> XSTS -> Minecraft Services -> Minecraft Token.
/// Токены сохраняются в encrypted store и переиспользуются между запусками.
/// </summary>
public sealed class MicrosoftAuthService : IAuthService, IDisposable
{
    private readonly MinecraftAuthenticator _auth = new();
    private readonly ITokenStore _store;
    private AuthSession? _session;

    public AuthSession? CurrentSession => _session;
    public event EventHandler<AuthSession?>? AuthenticationChanged;

    public MicrosoftAuthService()
    {
        // DPAPI-backed хранилище refresh-токенов (Windows). Для кроссплатформенности — AES + keyring.
        _store = new DefaultTokenStore(
            Path.Combine(LauncherPaths.Root, "accounts.json"),
            new AesEncryptionProvider());
        RestoreFromStore();
    }

    public async Task<AuthResult> LoginAsync(CancellationToken ct = default)
    {
        try
        {
            // Device flow: пользователь получает код и вводит его в браузере —
            // не нужен локальный HTTP-сервер и редирект-URL (важно для ЧСВ и файрволов).
            var authBegin = _auth.BeginDeviceFlowAuthentication(
                "MineLauncher",                          // имя клиента
                "https://login.microsoftonline.com",     // authority
                ct);

            // Показываем код в UI и открываем системный браузер.
            OpenBrowser(authBegin.VerificationUri);
            ReportCodeToUi(authBegin.UserCode);           // например через MessageBox/Event

            MinecraftAccount account = await authBegin.ContinueTask;

            _session = new AuthSession(
                Mode: AuthMode.Online,
                Nickname: account.GameProfile.Name,
                Uuid: account.GameProfile.Id.ToString("N"),
                AccessToken: account.AccessToken,
                RefreshToken: account.RefreshToken,
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(24));

            await _store.SaveAsync(account, ct);
            AuthenticationChanged?.Invoke(this, _session);
            return AuthResult.Ok(_session);
        }
        catch (NoMinecraftJavaTokenLoadException)
        {
            return AuthResult.Fail("Аккаунт Microsoft не приобрёл Minecraft: Java Edition.");
        }
        catch (RateLimitedException)
        {
            return AuthResult.Fail("Слишком много попыток входа. Подождите минуту.");
        }
        catch (OperationCanceledException)
        {
            return AuthResult.Fail("Вход отменён пользователем.");
        }
        catch (Exception ex)
        {
            return AuthResult.Fail($"Ошибка авторизации Microsoft: {ex.Message}");
        }
    }

    public async Task LogoutAsync()
    {
        if (_session?.RefreshToken is { } rt)
            await _auth.Logout(rt, invalidate: true);   // отзыв refresh-токена на стороне MS
        await _store.ClearAsync();
        _session = null;
        AuthenticationChanged?.Invoke(this, null);
    }

    public async Task<bool> EnsureValidSessionAsync(CancellationToken ct = default)
    {
        if (_session is null) return false;
        if (_session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5)) return true;

        try
        {
            var refreshed = await _auth.Refresh(_session.RefreshToken!, ct);
            _session = _session with
            {
                AccessToken = refreshed.AccessToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
            };
            return true;
        }
        catch (MinecraftAuthenticationException)
        {
            _session = null;                            // refresh-токен отозван — нужен повторный вход
            AuthenticationChanged?.Invoke(this, null);
            return false;
        }
    }

    /// <summary>
    /// Скины/плащи доступны через тот же MinecraftAccount: библиотека отдаёт
    /// ProfileInformation с URL skin/cape из Mojang profile API.
    /// Для превью в UI скачиваем PNG по account.ProfileSkin.Url.
    /// </summary>

    private void RestoreFromStore()
    {
        var saved = _store.Load().GetAwaiter().GetResult();
        if (saved?.TryGetValues(out var values, out _) == true && values.Count > 0)
        {
            var acc = values[0];
            _session = new AuthSession(AuthMode.Online, acc.GameProfile.Name,
                acc.GameProfile.Id.ToString("N"), acc.AccessToken, acc.RefreshToken, null);
        }
    }

    private static void OpenBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url, UseShellExecute = true)!);

    private static void ReportCodeToUi(string userCode) { /* событие в ViewModel */ }

    public void Dispose() => _auth.Dispose();
}

/// <summary>
/// Offline-режим: никакой сетевой проверки. UUID генерируется детерминированно
/// из ника (md5-based, как делали старые лаунчеры), чтобы скин/статы игрока
/// были стабильными на серверах с online-mode=false.
/// </summary>
public sealed class OfflineAuthService : IAuthService
{
    private readonly Func<string> _nicknameProvider;   // инжектим getter из ViewModel
    public AuthSession? CurrentSession { get; private set; }
    public event EventHandler<AuthSession?>? AuthenticationChanged;

    public OfflineAuthService(Func<string> nicknameProvider)
        => _nicknameProvider = nicknameProvider;

    public Task<AuthResult> LoginAsync(CancellationToken ct = default)
    {
        string nick = _nicknameProvider().Trim();

        if (nick.Length is < 3 or > 16)
            return Task.FromResult(AuthResult.Fail("Ник должен быть от 3 до 16 символов."));
        if (!System.Text.RegularExpressions.Regex.IsMatch(nick, @"^[A-Za-z0-9_]+$"))
            return Task.FromResult(AuthResult.Fail("Допустимы латиница, цифры и '_'."));

        string uuid = OfflineUuid(nick);
        CurrentSession = new AuthSession(AuthMode.Offline, nick, uuid,
            AccessToken: "0", RefreshToken: null, ExpiresAt: null);

        AuthenticationChanged?.Invoke(this, CurrentSession);
        return Task.FromResult(AuthResult.Ok(CurrentSession));
    }

    public Task LogoutAsync()
    {
        CurrentSession = null;
        AuthenticationChanged?.Invoke(this, null);
        return Task.CompletedTask;
    }

    public Task<bool> EnsureValidSessionAsync(CancellationToken ct = default)
        => Task.FromResult(CurrentSession is not null);

    public static string OfflineUuid(string nickname)
    {
        byte[] hash = MD5.HashData(System.Text.Encoding.UTF8.GetBytes("OfflinePlayer:" + nickname));
        hash[6] = (byte)(hash[6] & 0x0f | 0x30);   // version bits v3
        hash[8] = (byte)(hash[8] & 0x3f | 0x80);   // variant bits
        return new Guid(hash).ToString("N");
    }
}
