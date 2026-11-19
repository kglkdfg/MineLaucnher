using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MineLauncher.Models;
using MineLauncher.Services.Abstractions;
namespace MineLauncher.Services.Implementations;
/// <summary>Онлайн-провайдер подключается на этапе 6; успешный вход не имитируется.</summary>
public sealed class MicrosoftAuthService : IAuthService
{
 public AuthSession? CurrentSession { get; private set; }
 public event EventHandler<AuthSession?>? AuthenticationChanged;
 public Task<AuthResult> LoginAsync(CancellationToken ct = default) => Task.FromResult(AuthResult.Fail("Онлайн-авторизация Microsoft ещё не реализована."));
 public Task LogoutAsync() { CurrentSession = null; AuthenticationChanged?.Invoke(this, null); return Task.CompletedTask; }
 public Task<bool> EnsureValidSessionAsync(CancellationToken ct = default) => Task.FromResult(CurrentSession is not null);
}
public sealed class OfflineAuthService : IAuthService
{
 private readonly Func<string> _nicknameProvider;
 public string Nickname { get; set; } = "Player";
 public AuthSession? CurrentSession { get; private set; }
 public event EventHandler<AuthSession?>? AuthenticationChanged;
 public OfflineAuthService(Func<string>? nicknameProvider = null) => _nicknameProvider = nicknameProvider ?? (() => Nickname);
 public Task<AuthResult> LoginAsync(CancellationToken ct = default)
 {
  ct.ThrowIfCancellationRequested();
  var nick = _nicknameProvider().Trim();
  if (nick.Length is < 3 or > 16 || !Regex.IsMatch(nick, "^[A-Za-z0-9_]+$"))
   return Task.FromResult(AuthResult.Fail("Ник должен содержать 3–16 латинских букв, цифр или '_'."));
  CurrentSession = new AuthSession(AuthMode.Offline, nick, OfflineUuid(nick), "0", null, null);
  AuthenticationChanged?.Invoke(this, CurrentSession);
  return Task.FromResult(AuthResult.Ok(CurrentSession));
 }
 public Task LogoutAsync() { CurrentSession = null; AuthenticationChanged?.Invoke(this, null); return Task.CompletedTask; }
 public Task<bool> EnsureValidSessionAsync(CancellationToken ct = default) => Task.FromResult(CurrentSession is not null);
 public static string OfflineUuid(string nickname)
 {
  var hash = MD5.HashData(System.Text.Encoding.UTF8.GetBytes("OfflinePlayer:" + nickname));
  hash[6] = (byte)((hash[6] & 0x0f) | 0x30); hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
  return new Guid(hash).ToString("N");
 }
}