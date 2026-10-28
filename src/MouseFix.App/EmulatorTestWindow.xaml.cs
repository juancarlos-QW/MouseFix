using System.Windows;
using System.Windows.Threading;
using MouseFix.Core;
using Wpf.Ui.Controls;

namespace MouseFix.App;

/// <summary>
/// Alternates the Windows pointer speed between 1:1 and 0.5x while the user aims in the game.
/// If the in-game aim changes with it, the emulator follows the Windows cursor.
/// </summary>
public partial class EmulatorTestWindow : FluentWindow
{
    private static readonly TimeSpan SwitchInterval = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(2);

    private readonly MouseFixService _service;
    private readonly ProtectionMonitor _monitor;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = SwitchInterval };

    private MouseSettings? _before;
    private DateTime _startedAt;
    private bool _half;

    public EmulatorTestWindow(MouseFixService service, ProtectionMonitor monitor, AppSettings settings)
    {
        _service = service;
        _monitor = monitor;
        _settings = settings;
        InitializeComponent();
        _timer.Tick += (_, _) => NextPhase();
        // Never leave the speed changed, whatever way the window is closed.
        Closed += (_, _) => StopTest();
    }

    private bool IsRunning => _before is not null;

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsRunning)
            StopTest();
        else
            StartTest();
    }

    private void StartTest()
    {
        _before = _service.Current;
        _monitor.Paused = true;
        _startedAt = DateTime.Now;
        _half = false;
        ApplyPhase();
        _timer.Start();

        StartStopButton.Content = L.T("Parar teste", "Stop test");
        StartStopButton.Icon = new SymbolIcon(SymbolRegular.Stop24);
    }

    private void StopTest()
    {
        if (_before is null)
            return;

        _timer.Stop();
        _service.Apply(_before);
        _before = null;
        _monitor.Paused = false;

        PhaseText.Text = L.T("Parado", "Stopped");
        PhaseSubtext.Text = L.T("Velocidade original restaurada.", "Original speed restored.");
        StartStopButton.Content = L.T("Iniciar teste", "Start test");
        StartStopButton.Icon = new SymbolIcon(SymbolRegular.Play24);
    }

    private void NextPhase()
    {
        if (DateTime.Now - _startedAt > MaxDuration)
        {
            StopTest();
            return;
        }
        _half = !_half;
        ApplyPhase();
    }

    private void ApplyPhase()
    {
        // Both phases are acceleration-free; only the speed multiplier differs (the user's speed vs. about half of it).
        var normal = _service.BuildTarget();
        int halfSpeed = MouseSettings.SpeedForMultiplier(MouseSettings.SpeedMultiplier(normal.Speed) / 2);
        _service.Apply(_half ? normal with { Speed = halfSpeed } : normal);

        PhaseText.Text = _half ? L.T("Agora: metade da velocidade", "Now: half speed") : L.T("Agora: velocidade normal", "Now: normal speed");
        PhaseSubtext.Text = _half ? L.T("Bipe grave", "Low beep") : L.T("Bipe agudo", "High beep");

        int frequency = _half ? 440 : 1320;
        // Console.Beep blocks while it plays, so keep it off the UI thread.
        Task.Run(() => Console.Beep(frequency, 150));
    }

    private void YesButton_Click(object sender, RoutedEventArgs e) => SaveAnswer(true);

    private void NoButton_Click(object sender, RoutedEventArgs e) => SaveAnswer(false);

    private void SaveAnswer(bool usesWindowsCursor)
    {
        StopTest();
        _settings.EmulatorUsesWindowsCursor = usesWindowsCursor;
        _settings.Save(AppSettings.DefaultPath);
        Close();
    }
}
