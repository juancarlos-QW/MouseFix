using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MouseFix.Core;

namespace MouseFix.App;

/// <summary>
/// Watches for other programs turning acceleration back on and re-applies the target settings, and switches
/// the pointer speed when a program with a speed profile starts or stops.
/// The timer is the main mechanism: on Windows 11, a program calling SystemParametersInfo with
/// SPIF_SENDCHANGE does not reach other apps. WM_SETTINGCHANGE still catches programs that
/// broadcast the change themselves (e.g. the Settings app), instantly.
/// </summary>
public sealed class ProtectionMonitor
{
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int SPI_SETMOUSE = 0x0004;
    private const int SPI_SETMOUSESPEED = 0x0071;

    private readonly MouseFixService _service;
    private readonly Func<bool> _isEnabled;
    private readonly Func<IReadOnlyList<SpeedProfile>> _profiles;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    // HwndSource only keeps a weak reference to hooks; hold the delegate so it isn't collected.
    private readonly HwndSourceHook _hook;
    private string? _lastError;

    public ProtectionMonitor(MouseFixService service, Func<bool> isEnabled, Func<IReadOnlyList<SpeedProfile>> profiles)
    {
        _service = service;
        _isEnabled = isEnabled;
        _profiles = profiles;
        _hook = WndProc;
        _timer.Tick += (_, _) => Check();
    }

    public event EventHandler? Reapplied;

    /// <summary>Raised when a profiled program starts or stops and the speed was switched.</summary>
    public event EventHandler? ProfileChanged;

    /// <summary>Temporarily stops re-applying, e.g. while the emulator test switches the pointer speed on purpose.</summary>
    public bool Paused { get; set; }

    /// <summary>Profile of the program that is running now, or null when the default speed applies.</summary>
    public SpeedProfile? ActiveProfile { get; private set; }

    /// <summary>Starts listening. The window only provides a message loop; it can stay hidden.</summary>
    public void Start(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        HwndSource.FromHwnd(hwnd).AddHook(_hook);
        _timer.Start();
        Check();
    }

    /// <summary>Runs a check now, e.g. right after the profile list changed.</summary>
    public void CheckNow() => Check();

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE && wParam is SPI_SETMOUSE or SPI_SETMOUSESPEED)
        {
            // Defer so we don't write settings while Windows is still broadcasting the change.
            Dispatcher.CurrentDispatcher.BeginInvoke(Check);
        }
        return 0;
    }

    private void Check()
    {
        if (Paused)
            return;

        try
        {
            var active = SpeedProfiles.FindActive(_profiles(), IsRunning);
            if (active != ActiveProfile)
            {
                ActiveProfile = active;
                // A profile switch is something the user asked for, so apply it even with automatic protection off.
                _service.EnsureFixed();
                Log.Info(active is null
                    ? $"Nenhum perfil ativo; velocidade padrão {_service.TargetSpeed}/20."
                    : $"Perfil \"{active.DisplayName}\" ativo; velocidade {active.Speed}/20.");
                ProfileChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (_isEnabled() && _service.EnsureFixed())
            {
                Log.Info("Algo mudou a configuração do mouse; configuração do MouseFix reaplicada.");
                Reapplied?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            // A transient failure (e.g. registry briefly locked) must not crash the background app;
            // the next tick retries. Log once per distinct error so a persistent one doesn't flood the file.
            if (ex.Message != _lastError)
                Log.Error("Falha ao verificar/reaplicar a configuração", ex);
            _lastError = ex.Message;
            return;
        }
        _lastError = null;
    }

    private static bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var p in processes)
            p.Dispose();
        return processes.Length > 0;
    }
}
