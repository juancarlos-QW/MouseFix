using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MouseFix.Core;

namespace MouseFix.App;

/// <summary>
/// Watches for other programs turning acceleration back on and re-applies 1:1.
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
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    // HwndSource only keeps a weak reference to hooks; hold the delegate so it isn't collected.
    private readonly HwndSourceHook _hook;
    private string? _lastError;

    public ProtectionMonitor(MouseFixService service, Func<bool> isEnabled)
    {
        _service = service;
        _isEnabled = isEnabled;
        _hook = WndProc;
        _timer.Tick += (_, _) => Check();
    }

    public event EventHandler? Reapplied;

    /// <summary>Temporarily stops re-applying, e.g. while the emulator test switches the pointer speed on purpose.</summary>
    public bool Paused { get; set; }

    /// <summary>Starts listening. The window only provides a message loop; it can stay hidden.</summary>
    public void Start(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        HwndSource.FromHwnd(hwnd).AddHook(_hook);
        _timer.Start();
        Check();
    }

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
        if (Paused || !_isEnabled())
            return;

        try
        {
            if (_service.EnsureFixed())
            {
                Log.Info("Algo mudou a configuração do mouse; 1:1 reaplicado.");
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
}
