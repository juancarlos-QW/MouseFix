using System.Windows;
using System.Windows.Threading;
using MouseFix.Core;

namespace MouseFix.App;

public partial class App : Application
{
    private const string InstanceName = "MouseFix.SingleInstance";
    private static readonly TimeSpan NotifyCooldown = TimeSpan.FromMinutes(5);

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private MouseFixService _service = null!;
    private MainWindow _window = null!;
    private TrayIcon? _tray;
    private DateTime _lastReapplyNotice = DateTime.MinValue;
    private bool _toldAboutTray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceName, out bool isFirstInstance);
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

        var settings = AppSettings.Load(AppSettings.DefaultPath);
        _service = new MouseFixService(new WindowsMouseSettingsStore(), new BackupStore(BackupStore.DefaultPath));
        var monitor = new ProtectionMonitor(_service, () => settings.AutoReapply);
        monitor.Reapplied += OnReapplied;

        _window = new MainWindow(_service, settings, monitor);
        _window.HiddenToTray += OnHiddenToTray;

        _tray = new TrayIcon(open: ShowMainWindow, fixNow: FixNow, exit: ExitApp);

        monitor.Start(_window);

        if (!e.Args.Contains(AutoStart.MinimizedArgument))
            ShowMainWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instanceMutex?.ReleaseMutex();
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
            _service.RemoveAcceleration();
            _tray?.Notify("Aceleração removida", "O mouse agora está em 1:1.");
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
        _tray?.Notify("Aceleração bloqueada", "Um programa religou a aceleração do mouse e o MouseFix desligou de novo.");
    }

    private void OnHiddenToTray(object? sender, EventArgs e)
    {
        if (_toldAboutTray)
            return;
        _toldAboutTray = true;
        _tray?.Notify("MouseFix continua ativo", "Ele fica na bandeja protegendo sua configuração. Use \"Sair\" no menu para fechar.");
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
