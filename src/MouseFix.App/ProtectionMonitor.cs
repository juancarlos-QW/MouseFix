using System.Diagnostics;
using System.Runtime.InteropServices;
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
    // Before the next sign-in the protection curves aren't active yet, so while acceleration is on (turned on by
    // another program) movement really is accelerated: react fast. Reading the settings is cheap.
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    // Listing processes for profiles is heavier, so that runs every 10th tick (once a second).
    private const int ProfileCheckEveryTicks = 10;
    // This many outside changes within the window means some program keeps fighting MouseFix.
    private const int FightCount = 5;
    private static readonly TimeSpan FightWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FightNoticeCooldown = TimeSpan.FromMinutes(10);

    // HwndSource only keeps a weak reference to hooks; hold the delegate so it isn't collected.
    private readonly HwndSourceHook _hook;
    private readonly Queue<DateTime> _recentFixes = new();
    private DateTime _lastFightNotice = DateTime.MinValue;
    private int _tick;
    private string? _lastError;

    private readonly ChangeHistory _history;

    public ProtectionMonitor(MouseFixService service, Func<bool> isEnabled, Func<IReadOnlyList<SpeedProfile>> profiles,
        ChangeHistory history)
    {
        _service = service;
        _isEnabled = isEnabled;
        _profiles = profiles;
        _history = history;
        _hook = WndProc;
        _timer.Tick += (_, _) => Check();
    }

    public event EventHandler? Reapplied;

    /// <summary>Raised (at most every 10 minutes) when a program keeps changing the mouse; the argument is its name.</summary>
    public event EventHandler<string>? FightDetected;

    /// <summary>What the last outside change was and which program was in the foreground, for notifications.</summary>
    public string? LastExternalChange { get; private set; }

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
            if (_tick++ % ProfileCheckEveryTicks == 0)
            {
                var active = SpeedProfiles.FindActive(_profiles(), IsRunning);
                if (active != ActiveProfile)
                {
                    ActiveProfile = active;
                    // A profile switch is something the user asked for, so apply it even with automatic protection off.
                    bool applied = _service.EnsureFixed();
                    string description = active is null
                        ? $"Voltou à velocidade padrão {_service.TargetSpeed}/20"
                        : $"Perfil \"{active.DisplayName}\": velocidade {active.Speed}/20";
                    Log.Info(description);
                    if (applied)
                    {
                        string? problem = _service.VerifyTarget();
                        _history.Add(new ChangeEntry(DateTime.Now, ChangeSource.Profile, description, problem is null, problem,
                            active?.DisplayName));
                    }
                    ProfileChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }

            if (!_isEnabled() || !_service.HasBackup)
                return;

            var current = _service.Current;
            var expected = _service.BuildTarget();
            if (current.Equals(expected))
                return;

            // Something outside MouseFix changed the mouse: note what and who, then put the user's settings back.
            string what = SettingsChange.Describe(current, expected);
            string who = ForegroundProcessName();
            if (_service.EnsureFixed())
            {
                LastExternalChange = $"{what} (em primeiro plano: {who})";
                Log.Info($"Mudança externa no mouse: {LastExternalChange}. Configuração do MouseFix reaplicada.");
                string? problem = _service.VerifyTarget();
                _history.Add(new ChangeEntry(DateTime.Now, ChangeSource.OtherProgram, $"Outro programa mudou: {what}. Desfeito.",
                    problem is null, problem, who));
                Reapplied?.Invoke(this, EventArgs.Empty);
                TrackFight(who);
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

    private void TrackFight(string who)
    {
        var now = DateTime.Now;
        _recentFixes.Enqueue(now);
        while (_recentFixes.Count > 0 && now - _recentFixes.Peek() > FightWindow)
            _recentFixes.Dequeue();

        if (_recentFixes.Count >= FightCount && now - _lastFightNotice > FightNoticeCooldown)
        {
            _lastFightNotice = now;
            Log.Info($"{_recentFixes.Count} mudanças externas no último minuto; provável responsável: {who}.");
            FightDetected?.Invoke(this, who);
        }
    }

    private static string ForegroundProcessName()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            // With MouseFix itself in front, the program that made the change ran in the background: don't blame ourselves.
            if (pid == 0 || pid == Environment.ProcessId)
                return "não identificado (em segundo plano)";
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "desconhecido";
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    private static bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var p in processes)
            p.Dispose();
        return processes.Length > 0;
    }
}
