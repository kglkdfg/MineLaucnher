using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MineLauncher.Models;
using MineLauncher.Services.Abstractions;

namespace MineLauncher.ViewModels;

/// <summary>
/// ViewModel главного окна. Связывает: список сборок -> версия MC -> загрузчик ->
/// автосмена требуемой Java -> аутентификация -> кнопка "Играть".
/// Все долгие операции — async commands с CancellationTokenSource.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly OfflineAuthService _offlineAuth;
    private readonly IJavaManager _java;
    private readonly ILauncherService _launcher;
    private readonly IVersionManifestService _manifest;
    private readonly ILoaderVersionService _loaderVersions; // запрос списка версий Forge/Fabric/...
    private readonly Dispatcher _dispatcher;

    private CancellationTokenSource? _launchCts;

    // ---------- Списки для ComboBox ----------
    public ObservableCollection<Instance> Instances { get; } = [];
    public ObservableCollection<string> MinecraftVersions { get; } = [];
    public ObservableCollection<string> LoaderVersions { get; } = [];

    public Array LoaderTypes => Enum.GetValues<LoaderType>();

    // ---------- Свойства, привязанные к UI ----------
    [ObservableProperty] private Instance? _selectedInstance;
    [ObservableProperty] private string? _selectedVersion;
    [ObservableProperty] private LoaderType _selectedLoader = LoaderType.Vanilla;
    [ObservableProperty] private string? _selectedLoaderVersion;

    [ObservableProperty] private string _offlineNickname = "Player";
    [ObservableProperty] private AuthMode _authMode = AuthMode.Offline;
    [ObservableProperty] private string _accountStatus = "Не авторизован";

    [ObservableProperty] private string _requiredJavaLabel = "";
    [ObservableProperty] private bool _isJavaMissing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchGameCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private bool _isBusy;

    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _gameLog = "";

    // ---------- Инициализация ----------
    public MainViewModel(
        IAuthService auth, OfflineAuthService offlineAuth, IJavaManager java, ILauncherService launcher,
        IVersionManifestService manifest, ILoaderVersionService loaderVersions,
        IInstanceRepository instances)
    {
        _auth = auth; _offlineAuth = offlineAuth; _java = java; _launcher = launcher;
        _manifest = manifest; _loaderVersions = loaderVersions;
        _dispatcher = Dispatcher.CurrentDispatcher;

        foreach (var i in instances.LoadAll()) Instances.Add(i);
        SelectedInstance = Instances.FirstOrDefault();
        _auth.AuthenticationChanged += (_, s) =>
            _dispatcher.Invoke(() => AccountStatus =
                s is null ? "Не авторизован" : $"{s.Nickname} ({s.Mode})");

        _ = SafeInitAsync();
        _ = StreamGameLogAsync();
    }

    private async Task SafeInitAsync()
    {
        try
        {
            var versions = await _manifest.GetReleaseListAsync();
            foreach (var v in versions) MinecraftVersions.Add(v);
        }
        catch (Exception ex) { ProgressText = $"Не удалось получить список версий: {ex.Message}"; }
    }

    // ---------- Каскад: сборка -> версия -> загрузчик -> Java ----------

    partial void OnSelectedInstanceChanged(Instance? value)
    {
        if (value is null) return;
        SelectedVersion = value.MinecraftVersion;          // триггерит OnSelectedVersionChanged
        SelectedLoader = value.Loader;
    }

    partial void OnSelectedVersionChanged(string? value)
        => _ = UpdateDependencyInfoAsync(value);

    partial void OnSelectedLoaderChanged(LoaderType value)
        => _ = UpdateLoaderVersionsAsync();

    private async Task UpdateDependencyInfoAsync(string? mcVersion)
    {
        if (mcVersion is null) return;

        int required = _java.GetRequiredJavaMajorVersion(mcVersion);
        RequiredJavaLabel = $"Требуется Java {required}";
        IsJavaMissing = await _java.FindCompatibleRuntimeAsync(required) is null;

        await UpdateLoaderVersionsAsync();
    }

    private async Task UpdateLoaderVersionsAsync()
    {
        LoaderVersions.Clear();
        SelectedLoaderVersion = null;

        if (SelectedLoader == LoaderType.Vanilla || SelectedVersion is null) return;

        try
        {
            var list = await _loaderVersions.GetVersionsAsync(SelectedLoader, SelectedVersion);
            foreach (var v in list) LoaderVersions.Add(v);   // стабильные версии первыми
            SelectedLoaderVersion = LoaderVersions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ProgressText = $"Сервер загрузчика недоступен: {ex.Message}";
        }
    }

    // ---------- Команды ----------

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            // Выбор реализации происходит в DI-корневом провайдере по AuthMode,
            // либо здесь — через фабрику IAuthServiceFactory.
            // Online Microsoft auth is not implemented yet; use the nickname entered in the UI.
            _offlineAuth.Nickname = OfflineNickname;
            var result = await _auth.LoginAsync(ct);
            AccountStatus = result.Success
                ? $"{result.Session!.Nickname} ({result.Session.Mode})"
                : result.Error ?? "Ошибка входа";
        }
        finally { IsBusy = false; }
    }

    private bool CanLogin() => !IsBusy && _auth.CurrentSession is null;

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchGameAsync()
    {
        if (SelectedInstance is null || SelectedVersion is null) return;

        IsBusy = true;
        ProgressValue = 0;
        _launchCts = new CancellationTokenSource();

        var progress = new Progress<LaunchPhase>(p =>
        {
            ProgressValue = p.Percent;
            ProgressText = p.Description;
        });

        try
        {
            if (!await _auth.EnsureValidSessionAsync(_launchCts.Token))
            {
                ProgressText = "Сначала выполните вход.";
                return;
            }

            // Применяем текущий выбор пользователя к копии сборки
            var instance = SelectedInstance;
            instance.MinecraftVersion = SelectedVersion;
            instance.Loader = SelectedLoader;
            instance.LoaderVersion = SelectedLoaderVersion;

            var options = new LaunchOptions(
                Instance: instance,
                Session: _auth.CurrentSession!,
                JavaPath: "",                                  // определит JavaManager внутри
                MemoryArgs: "-Xmx4G -Xms1G",
                ExtraGameArgs: "");

            var result = await _launcher.LaunchAsync(options, progress, _launchCts.Token);
            ProgressText = result.Success ? "Готово" : result.Error ?? "Ошибка запуска";
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Отменено.";
        }
        finally
        {
            IsBusy = false;
            _launchCts.Dispose();
            _launchCts = null;
        }
    }

    private bool CanLaunch() =>
        !IsBusy && _auth.CurrentSession is not null &&
        SelectedInstance is not null && SelectedVersion is not null;

    [RelayCommand]
    private async Task StopGameAsync() => await _launcher.KillGameAsync();

    // ---------- Стриминг лога игры в UI ----------
    private async Task StreamGameLogAsync()
    {
        await foreach (var line in _launcher.GameOutput)
        {
            // Обновление UI через Dispatcher; батчинг строк — задача оптимизации
            _dispatcher.BeginInvoke(() => GameLog += line + Environment.NewLine);
        }
    }
}
