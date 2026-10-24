using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MouseFix.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace MouseFix.App;

public partial class App : Application
{
    private const string InstanceName = "MouseFix.SingleInstance";
    private const string ExitArgument = "--exit";
    private static readonly TimeSpan NotifyCooldown = TimeSpan.FromMinutes(5);

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _exitEvent;
    private MouseFixService _service = null!;
    private ProtectionMonitor _monitor = null!;
    private AppSettings _settings = null!;
    private MainWindow _window = null!;
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private TrayIcon? _tray;
    private DateTime _lastReapplyNotice = DateTime.MinValue;
    private bool _toldAboutTray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceName, out bool isFirstInstance);

        if (e.Args.Contains(ExitArgument))
        {
            // Used by the uninstaller: ask the running copy to close and wait for it, so its files can be removed.
            if (!isFirstInstance && EventWaitHandle.TryOpenExisting(InstanceName + ".Exit", out var exitEvent))
            {
                exitEvent.Set();
                try
                {
                    _instanceMutex.WaitOne(TimeSpan.FromSeconds(10));
                }
                catch (AbandonedMutexException)
                {
                    // The other copy ended without releasing the mutex; it is gone either way.
                }
            }
            Shutdown();
            return;
        }

        if (!isFirstInstance)
        {
            // Already running (e.g. started with Windows): ask that instance to show itself.
            if (EventWaitHandle.TryOpenExisting(InstanceName + ".Show", out var existing))
                existing.Set();
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
        ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(ShowMainWindow),
            null, Timeout.Infinite, executeOnlyOnce: false);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Exit");
        ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) => Dispatcher.BeginInvoke(ExitApp),
            null, Timeout.Infinite, executeOnlyOnce: true);

        // Always the navy dark look with the blue accent, whatever the Windows theme or accent colour is.
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.None, updateAccent: false);
        ApplicationAccentColorManager.Apply((Color)Resources["MfAccentColor"], ApplicationTheme.Dark);

        var settings = _settings = AppSettings.Load(AppSettings.DefaultPath);
        ProtectionMonitor? monitor = null;
        // The speed in effect: the running program's profile, otherwise the default chosen in the window.
        _service = new MouseFixService(new WindowsMouseSettingsStore(), new BackupStore(BackupStore.DefaultPath),
            () => new TargetOptions(monitor?.ActiveProfile?.Speed ?? settings.PointerSpeed, settings.AccelerationEnabled, settings.ScrollLines));
        var history = new ChangeHistory(ChangeHistory.DefaultPath);
        _monitor = monitor = new ProtectionMonitor(_service, () => settings.AutoReapply, () => settings.Profiles, history);
        monitor.Reapplied += OnReapplied;
        monitor.ProfileChanged += OnProfileChanged;
        monitor.FightDetected += (_, program) => _tray?.Notify(
            "Um programa está mudando o seu mouse",
            $"\"{program}\" mudou a aceleração ou a velocidade várias vezes no último minuto. O MouseFix desfez todas, " +
            "mas se a mira oscilar, procure a opção de mouse nesse programa.");

        _window = new MainWindow(_service, settings, monitor, history);
        _window.HiddenToTray += OnHiddenToTray;
        _window.CheckForUpdatesRequested += (_, _) => CheckForUpdates(force: true);

        _tray = new TrayIcon(open: ShowMainWindow, fixNow: FixNow, report: () => ProblemReporter.Open(_service, monitor), exit: ExitApp);

        monitor.Start(_window);

        // First check shortly after start (not during it), then see every hour whether a day has passed.
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(1);
            CheckForUpdates(force: false);
        };
        _updateTimer.Start();

        if (!e.Args.Contains(AutoStart.MinimizedArgument))
            ShowMainWindow();
    }

    private async void CheckForUpdates(bool force)
    {
        if (!_settings.CheckForUpdates)
            return;
        if (!force && _settings.LastUpdateCheck is DateTime last && DateTime.Now - last < UpdateChecker.Interval)
            return;

        try
        {
            var current = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
            var update = await UpdateChecker.CheckAsync(current);
            _settings.LastUpdateCheck = DateTime.Now;
            if (update is not null)
            {
                string version = update.Version.ToString(3);
                _settings.AvailableUpdateVersion = version;
                _settings.AvailableUpdateUrl = update.Url;
                Log.Info($"Nova versão disponível: {version}");
                if (_settings.NotifiedUpdateVersion != version)
                {
                    _settings.NotifiedUpdateVersion = version;
                    _tray?.Notify($"MouseFix {version} disponível", "Abra o MouseFix e clique em \"Baixar\" para atualizar.");
                }
            }
            _settings.Save(AppSettings.DefaultPath);
            _window.Refresh();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Offline or GitHub unreachable: try again on the next hourly tick.
            Log.Info($"Não foi possível verificar atualizações: {ex.Message}");
        }
    }

    private void OnProfileChanged(object? sender, EventArgs e)
    {
        _window.Refresh();
        _tray?.Notify("Velocidade do ponteiro", _monitor.ActiveProfile is { } p
            ? $"{p.DisplayName} aberto: velocidade {p.Speed}/20, sem aceleração."
            : $"Velocidade padrão: {_service.TargetSpeed}/20, sem aceleração.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned (an --exit run that timed out waiting); nothing to release.
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void ShowMainWindow()
    {
        _window.Refresh();
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void FixNow()
    {
        try
        {
            _tray?.Notify("Aceleração removida", _window.RemoveAccelerationNow());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "MouseFix", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        _window.Refresh();
    }

    private void ExitApp()
    {
        _window.AllowClose = true;
        Shutdown();
    }

    private void OnReapplied(object? sender, EventArgs e)
    {
        _window.Refresh();

        // Some games toggle the setting repeatedly; don't flood the user with notifications.
        if (DateTime.Now - _lastReapplyNotice < NotifyCooldown)
            return;
        _lastReapplyNotice = DateTime.Now;
        _tray?.Notify("Configuração do mouse restaurada",
            $"Outro programa mudou o mouse ({_monitor.LastExternalChange ?? "aceleração ou velocidade"}) e o MouseFix desfez.");
    }

    private void OnHiddenToTray(object? sender, EventArgs e)
    {
        if (_toldAboutTray)
            return;
        _toldAboutTray = true;
        _tray?.Notify("MouseFix continua ativo", "Ele fica na bandeja protegendo sua configuração. Use \"Sair\" no menu para fechar.");
    }

    // Raised on Windows shutdown/logoff and when an installer or uninstaller asks the app to close
    // (Restart Manager). The window normally hides to the tray on close; here it must really exit.
    private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        if (_window is not null)
            _window.AllowClose = true;
        Shutdown();
    }

    // Without this, an error while opening the window closes the app with no message.
    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Erro inesperado", e.Exception);
        MessageBox.Show(
            $"Ocorreu um erro inesperado e o MouseFix será fechado.\n\n{e.Exception.GetBaseException().Message}",
            "MouseFix", MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true;
        Shutdown(1);
    }
}
