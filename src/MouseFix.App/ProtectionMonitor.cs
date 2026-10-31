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
    private readonly GameModeService _gameMode;
    private readonly Func<bool> _gameModeWithEmulator;
    // Null until the first process check, so an emulator already open when MouseFix starts counts as "just opened".
    private bool? _emulatorWasRunning;
    // Emulator processes whose priority MouseFix raised, so only those go back to normal (not ones the user set).
    private readonly HashSet<int> _raisedPriority = [];

    private readonly FocusModeController _focus;
    private readonly Func<bool> _focusWithEmulator;
    // Programs opened during the game are caught every few process checks (seconds).
    private const int FocusCatchEveryChecks = 5;
    private int _focusChecks;

    public ProtectionMonitor(MouseFixService service, Func<bool> isEnabled, Func<IReadOnlyList<SpeedProfile>> profiles,
        ChangeHistory history, GameModeService gameMode, Func<bool> gameModeWithEmulator,
        FocusModeController focus, Func<bool> focusWithEmulator)
    {
        _service = service;
        _isEnabled = isEnabled;
        _profiles = profiles;
        _history = history;
        _gameMode = gameMode;
        _gameModeWithEmulator = gameModeWithEmulator;
        _focus = focus;
        _focusWithEmulator = focusWithEmulator;
        _hook = WndProc;
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>Raised when game mode turned on or off (by an emulator or by the user).</summary>
    public event EventHandler? GameModeChanged;

    /// <summary>True while a supported emulator is running (as of the last process check).</summary>
    public bool EmulatorRunning => _emulatorWasRunning == true;

    /// <summary>
    /// The button: turns game mode on (it then stays until the user turns it off) or off. Records it in the history.
    /// Returns null when verified, otherwise what failed.
    /// </summary>
    public string? ToggleGameMode()
    {
        bool turningOn = !_gameMode.IsActive;
        string? problem = turningOn ? _gameMode.Activate(automatic: false) : _gameMode.Deactivate();
        RecordGameMode(ChangeSource.User, turningOn
            ? L.T($"Modo jogo ligado (plano {GameModeService.PlanName})", $"Game mode on (plan {GameModeService.PlanName})")
            : L.T($"Modo jogo desligado (plano {_gameMode.ActivePlanName})", $"Game mode off (plan {_gameMode.ActivePlanName})"), problem);
        UpdateEmulatorPriority();
        GameModeChanged?.Invoke(this, EventArgs.Empty);
        return problem;
    }

    /// <summary>
    /// High priority for the emulator while game mode is on, so Windows gives it the CPU first; back to normal
    /// when game mode turns off. Only touches processes still at normal priority, or ones MouseFix raised.
    /// </summary>
    public void UpdateEmulatorPriority()
    {
        bool raise = _gameMode.IsActive;
        foreach (var (processName, displayName) in SpeedProfiles.KnownPrograms)
        {
            foreach (var p in Process.GetProcessesByName(processName))
            {
                using (p)
                {
                    try
                    {
                        if (raise && !_raisedPriority.Contains(p.Id) && p.PriorityClass == ProcessPriorityClass.Normal)
                        {
                            p.PriorityClass = ProcessPriorityClass.High;
                            _raisedPriority.Add(p.Id);
                            RecordGameMode(ChangeSource.GameMode, L.T($"Prioridade do {displayName}: alta", $"{displayName} priority: high"), null);
                        }
                        else if (!raise && _raisedPriority.Remove(p.Id) && p.PriorityClass == ProcessPriorityClass.High)
                        {
                            p.PriorityClass = ProcessPriorityClass.Normal;
                            RecordGameMode(ChangeSource.GameMode, L.T($"Prioridade do {displayName}: normal", $"{displayName} priority: normal"), null);
                        }
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                        // The emulator runs as administrator, or just closed: leave its priority alone.
                    }
                }
            }
        }
        if (!raise)
            _raisedPriority.Clear(); // closed processes; their ids may be reused
    }

    private void RecordGameMode(ChangeSource source, string description, string? problem)
    {
        if (problem is null && _gameMode.Unsupported.Count > 0 && _gameMode.IsActive)
            description += L.T($". Este PC não tem: {string.Join(", ", _gameMode.Unsupported).ToLowerInvariant()}", $". This PC doesn't have: {string.Join(", ", _gameMode.Unsupported).ToLowerInvariant()}");
        _history.Add(new ChangeEntry(DateTime.Now, source, description, problem is null, problem));
        Log.Info(problem is null ? description : $"{description} — falhou: {problem}");
    }

    /// <summary>Raised when focus mode turned on or off; the argument is what was done.</summary>
    public event EventHandler<string>? FocusModeChanged;

    /// <summary>
    /// Brings focus mode in line with the setting and the emulator: on while an emulator runs and the option is on,
    /// otherwise off (everything put back).
    /// </summary>
    public void UpdateFocusMode()
    {
        bool want = EmulatorRunning && _focusWithEmulator();
        if (want && !_focus.IsActive)
            RecordFocus(_focus.Start());
        else if (!want && (_focus.IsActive || _focus.HasLeftovers))
            RecordFocus(_focus.Stop());
        else if (want && ++_focusChecks % FocusCatchEveryChecks == 0 && _focus.CatchNew() is int caught and > 0)
            Log.Info($"Modo foco: {FocusMode.Programs(caught)} aberto(s) durante o jogo com menos prioridade");
    }

    private void RecordFocus(string description)
    {
        _history.Add(new ChangeEntry(DateTime.Now, ChangeSource.GameMode, description, true));
        Log.Info(description);
        FocusModeChanged?.Invoke(this, description);
    }

    /// <summary>Emulator opened → game mode on (if enabled and not already on); emulator closed → back to the previous plan.</summary>
    private void HandleEmulator(HashSet<string> running)
    {
        var emulator = SpeedProfiles.KnownPrograms.Keys.FirstOrDefault(running.Contains);
        bool isRunning = emulator is not null;
        bool wasRunning = _emulatorWasRunning ?? false;
        bool firstCheck = _emulatorWasRunning is null;
        _emulatorWasRunning = isRunning;
        UpdateFocusMode();

        // MouseFix restarted (or crashed) after turning game mode on for an emulator that is now closed.
        if (firstCheck && !isRunning && _gameMode.ActivatedAutomatically)
        {
            RecordGameMode(ChangeSource.GameMode, L.T("Modo jogo desligado (o emulador já tinha fechado)", "Game mode off (the emulator had already closed)"), _gameMode.Deactivate());
            GameModeChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (isRunning == wasRunning && !firstCheck)
            return;

        if (isRunning)
        {
            string name = SpeedProfiles.KnownPrograms[emulator!];
            EnsureDedicatedGpu(emulator!, name);
            if (_gameModeWithEmulator() && !_gameMode.IsActive)
            {
                RecordGameMode(ChangeSource.GameMode, L.T($"Modo jogo ligado ({name} aberto)", $"Game mode on ({name} open)"), _gameMode.Activate(automatic: true));
                GameModeChanged?.Invoke(this, EventArgs.Empty);
            }
            UpdateEmulatorPriority();
        }
        else
        {
            _raisedPriority.Clear();
            if (_gameMode.ActivatedAutomatically)
            {
                RecordGameMode(ChangeSource.GameMode, L.T("Modo jogo desligado (emulador fechado)", "Game mode off (emulator closed)"), _gameMode.Deactivate());
                GameModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // On laptops with two GPUs, makes the emulator use the dedicated one. Skipped if the user already chose.
    private void EnsureDedicatedGpu(string processName, string displayName)
    {
        if (!_gameModeWithEmulator())
            return;
        try
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                var path = processes.FirstOrDefault()?.MainModule?.FileName;
                if (path is not null && GpuPreference.EnsureHighPerformance(path))
                    RecordGameMode(ChangeSource.GameMode, L.T($"{displayName} marcado para usar a placa de vídeo de alto desempenho", $"{displayName} set to use the high-performance graphics card"), null);
            }
            finally
            {
                foreach (var p in processes)
                    p.Dispose();
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            // Can't read the emulator's path (e.g. it runs as administrator): leave the graphics preference alone.
        }
    }

    private static HashSet<string> RunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
                names.Add(p.ProcessName);
        }
        return names;
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
        Check(checkProfiles: true);
    }

    /// <summary>Runs a full check now, profiles included, e.g. right after the profile list changed.</summary>
    public void CheckNow() => Check(checkProfiles: true);

    /// <summary>Treats an emulator that is already open as just opened, e.g. right after automatic game mode was turned on.</summary>
    public void RecheckEmulator()
    {
        _emulatorWasRunning = null;
        CheckNow();
    }

    private void OnTick() => Check(checkProfiles: _tick++ % ProfileCheckEveryTicks == 0);

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE && wParam is SPI_SETMOUSE or SPI_SETMOUSESPEED)
        {
            // Defer so we don't write settings while Windows is still broadcasting the change.
            Dispatcher.CurrentDispatcher.BeginInvoke(() => Check(checkProfiles: false));
        }
        return 0;
    }

    private void Check(bool checkProfiles)
    {
        if (Paused)
            return;

        try
        {
            CheckCore(checkProfiles);
            _lastError = null;
        }
        catch (Exception ex)
        {
            // A transient failure (e.g. registry briefly locked) must not crash the background app;
            // the next tick retries. Log once per distinct error so a persistent one doesn't flood the file.
            if (ex.Message != _lastError)
                Log.Error("Falha ao verificar/reaplicar a configuração", ex);
            _lastError = ex.Message;
        }
    }

    private void CheckCore(bool checkProfiles)
    {
        if (checkProfiles)
        {
            // One process list per check, shared by game mode and profiles.
            var running = RunningProcessNames();
            HandleEmulator(running);

            var active = SpeedProfiles.FindActive(_profiles(), running.Contains);
            if (active != ActiveProfile)
            {
                ActiveProfile = active;
                // A profile switch is something the user asked for, so apply it even with automatic protection off.
                bool applied = _service.EnsureFixed();
                string description = active is null
                    ? L.T($"Voltou à velocidade padrão {_service.TargetSpeed}/20", $"Back to the default speed {_service.TargetSpeed}/20")
                    : L.T($"Perfil \"{active.DisplayName}\": velocidade {active.Speed}/20", $"Profile \"{active.DisplayName}\": speed {active.Speed}/20");
                Log.Info(description);
                if (applied)
                {
                    string? profileProblem = _service.VerifyTarget();
                    _history.Add(new ChangeEntry(DateTime.Now, ChangeSource.Profile, description, profileProblem is null,
                        profileProblem, active?.DisplayName));
                }
                ProfileChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        if (!_isEnabled() || !_service.HasBackup)
            return;

        // One read of Windows per tick; the target is built from it.
        var current = _service.Current;
        var expected = _service.BuildTarget(current);
        if (current.Equals(expected))
            return;

        if (SettingsChange.OnlyCurvesDiffer(current, expected))
        {
            // Programs change acceleration or speed, not the protection curves: this is MouseFix's own update
            // (e.g. replacing curves written by an older version), so don't blame anyone or notify.
            _service.Apply(expected);
            string? curvesProblem = _service.VerifyTarget();
            Log.Info("Curvas de proteção atualizadas pelo MouseFix.");
            _history.Add(new ChangeEntry(DateTime.Now, ChangeSource.MouseFix,
                L.T("Curvas de proteção atualizadas (valem depois do próximo login)", "Protection curves updated (they take effect after the next sign-in)"), curvesProblem is null, curvesProblem));
            return;
        }

        // Something outside MouseFix changed the mouse: note what and who, then put the user's settings back.
        string what = SettingsChange.Describe(current, expected);
        string who = ForegroundProcessName();
        _service.Apply(expected);
        LastExternalChange = L.T($"{what} (em primeiro plano: {who})", $"{what} (in the foreground: {who})");
        Log.Info($"Mudança externa no mouse: {LastExternalChange}. Configuração do MouseFix reaplicada.");
        string? problem = _service.VerifyTarget();
        _history.Add(new ChangeEntry(DateTime.Now, ChangeSource.OtherProgram, L.T($"Outro programa mudou: {what}. Desfeito.", $"Another program changed: {what}. Undone."),
            problem is null, problem, who));
        Reapplied?.Invoke(this, EventArgs.Empty);
        TrackFight(who);
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
                return L.T("não identificado (em segundo plano)", "unidentified (in the background)");
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return L.T("desconhecido", "unknown");
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
}
