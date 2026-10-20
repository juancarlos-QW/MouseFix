using System.ComponentModel;
using System.Windows;
using MouseFix.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace MouseFix.App;

public partial class MainWindow : FluentWindow
{
    private readonly MouseFixService _service;
    private readonly AppSettings _settings;
    private readonly ProtectionMonitor _monitor;
    private EmulatorGameProfile? _emulatorProfile;

    public MainWindow(MouseFixService service, AppSettings settings, ProtectionMonitor monitor)
    {
        _service = service;
        _settings = settings;
        _monitor = monitor;

        InitializeComponent();
        SystemThemeWatcher.Watch(this);

        AutoReapplyToggle.IsChecked = settings.AutoReapply;
        AutoStartToggle.IsChecked = AutoStart.IsEnabled;
        Refresh();
    }

    /// <summary>When false, closing the window only hides it to the tray.</summary>
    public bool AllowClose { get; set; }

    public event EventHandler? HiddenToTray;

    public void Refresh()
    {
        var current = _service.Current;
        var target = _service.BuildOneToOne();
        bool curvesLinear = current.SmoothMouseXCurve.AsSpan().SequenceEqual(target.SmoothMouseXCurve)
                         && current.SmoothMouseYCurve.AsSpan().SequenceEqual(target.SmoothMouseYCurve);

        (SymbolRegular icon, string brush, string title, string subtitle) = (current.IsOneToOne, curvesLinear) switch
        {
            (true, true) => (SymbolRegular.CheckmarkCircle24, "SystemFillColorSuccessBrush",
                "Aceleração removida", "Movimento 1:1: o cursor segue exatamente o sensor do mouse."),
            (true, false) => (SymbolRegular.Warning24, "SystemFillColorCautionBrush",
                "Sem aceleração, sem proteção", "Aplique para impedir que jogos antigos religuem a aceleração."),
            _ => (SymbolRegular.DismissCircle24, "SystemFillColorCriticalBrush",
                "Aceleração ativa", "O Windows está alterando o movimento conforme a velocidade da mão."),
        };

        StatusIcon.Symbol = icon;
        StatusIcon.SetResourceReference(ForegroundProperty, brush);
        StatusText.Text = title;
        StatusSubtext.Text = subtitle;

        AccelValue.Text = current.IsAccelerationEnabled ? "Ligada" : "Desligada";
        SpeedValue.Text = current.Speed == MouseSettings.OneToOneSpeed ? $"{current.Speed}/20 (1:1)" : $"{current.Speed}/20";
        CurvesValue.Text = curvesLinear ? "Lineares" : "Padrão do Windows";

        LoginInfo.IsOpen = _service.HasBackup;
        RestoreButton.IsEnabled = _service.HasBackup;
        FixButton.IsEnabled = !current.Equals(target);

        var mouseSoftware = MouseSoftware.FindRunning();
        MouseSoftwareInfo.IsOpen = mouseSoftware.Count > 0;
        MouseSoftwareInfo.Title = $"{string.Join(", ", mouseSoftware)} aberto";
        MouseSoftwareInfo.Message = "Programas de mouse podem aplicar aceleração no driver, antes do Windows, onde o MouseFix " +
                                    "não alcança. Use Testes › Distância para conferir.";

        RefreshEmulator();
    }

    private void RefreshEmulator()
    {
        // With several emulators/games, show the mapping the user touched most recently.
        var profile = BlueStacksProfiles.Find(BlueStacksProfiles.DefaultRoot)
            .OrderByDescending(p => System.IO.File.GetLastWriteTimeUtc(p.FilePath))
            .FirstOrDefault();
        _emulatorProfile = profile;

        EmulatorCard.Visibility = profile?.SelectedAim is null ? Visibility.Collapsed : Visibility.Visible;
        if (profile?.SelectedAim is not { } aim)
            return;

        EmulatorTitle.Text = $"{profile.EmulatorName} · {profile.GameName}";
        EmulatorAccelValue.Text = profile.AnyAcceleration ? "Ligada" : "Desligada";
        EmulatorSchemeValue.Text = aim.SchemeName;
        EmulatorSensValue.Text = $"{aim.Sensitivity:0.##} / {aim.SensitivityRatioY:P0}";
        EmulatorFixButton.Visibility = profile.AnyAcceleration ? Visibility.Visible : Visibility.Collapsed;

        EmulatorTestButton.Content = _settings.EmulatorUsesWindowsCursor is null ? "Testar emulador" : "Testar de novo";
        EmulatorAdvice.Text = profile.AnyAcceleration
            ? "O emulador está aplicando aceleração própria na mira. Feche o emulador e desligue abaixo."
            : EmulatorAdviceFor(profile.GameName, aim);

        var suggested = SuggestedSensitivity(aim);
        bool needsAdjust = suggested is double s && Math.Abs(s - aim.Sensitivity) >= 0.01;
        EmulatorSensButton.Visibility = needsAdjust ? Visibility.Visible : Visibility.Collapsed;
        EmulatorSensButton.Content = $"Aplicar sensibilidade {suggested:0.00}";
    }

    private string EmulatorAdviceFor(string game, EmulatorAim aim) => _settings.EmulatorUsesWindowsCursor switch
    {
        null => $"Faça o teste do emulador para saber se as configurações do Windows afetam a mira no {game}.",
        false => $"Teste feito: o emulador lê o sensor do mouse direto. A velocidade do Windows não muda a mira no {game}; " +
                 "ajuste pela sensibilidade do emulador ou pelo DPI do mouse.",
        true => SuggestedSensitivity(aim) is double s
            ? Math.Abs(s - aim.Sensitivity) < 0.01
                ? $"Teste feito: a mira do {game} segue o cursor do Windows. Sensibilidade já ajustada para o 1:1 " +
                  "(mesma sensação de antes, sem perder pontos do sensor)."
                : $"Teste feito: a mira do {game} segue o cursor do Windows. Antes do MouseFix a velocidade era " +
                  $"{_service.Original!.Speed}/20, por isso a mira ficou mais rápida. Com sensibilidade X {s:0.00} " +
                  "a sensação volta a ser a de antes, agora sem perder nenhum ponto do sensor."
            : $"Teste feito: a mira do {game} segue o cursor do Windows, então o 1:1 do MouseFix chega até o jogo.",
    };

    /// <summary>
    /// Emulator sensitivity that keeps the old in-game feel after the pointer speed changed to 1:1.
    /// Only meaningful when the emulator follows the Windows cursor. Based on the sensitivity from before
    /// the adjustment, so applying it once doesn't keep suggesting a smaller value.
    /// </summary>
    private double? SuggestedSensitivity(EmulatorAim aim)
    {
        var original = _service.Original;
        if (_settings.EmulatorUsesWindowsCursor != true || original is null || original.Speed == MouseSettings.OneToOneSpeed)
            return null;

        double baseSensitivity = _settings.EmulatorSensitivityBeforeAdjust ?? aim.Sensitivity;
        return Math.Round(baseSensitivity * MouseSettings.SpeedMultiplier(original.Speed), 2);
    }

    private void EmulatorSensButton_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (_emulatorProfile?.SelectedAim is not { } aim || SuggestedSensitivity(aim) is not double suggested)
            return;
        if (!EnsureEmulatorClosed())
            return;

        _settings.EmulatorSensitivityBeforeAdjust ??= aim.Sensitivity;
        _settings.Save(AppSettings.DefaultPath);
        BlueStacksProfiles.SetSelectedSensitivity(_emulatorProfile.FilePath, suggested);
        Log.Info($"Sensibilidade do emulador ajustada de {aim.Sensitivity} para {suggested} em {_emulatorProfile.FilePath}");
    });

    private bool EnsureEmulatorClosed()
    {
        if (!BlueStacksProfiles.IsEmulatorRunning)
            return true;
        System.Windows.MessageBox.Show(this,
            $"Feche o {_emulatorProfile?.EmulatorName ?? "emulador"} antes. Ele regrava esse arquivo e desfaria a mudança.",
            "MouseFix", System.Windows.MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void EmulatorTestButton_Click(object sender, RoutedEventArgs e)
    {
        new EmulatorTestWindow(_service, _monitor, _settings) { Owner = this }.ShowDialog();
        Refresh();
    }

    private void EmulatorFixButton_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (_emulatorProfile is null || !EnsureEmulatorClosed())
            return;
        int changed = BlueStacksProfiles.DisableMouseAcceleration(_emulatorProfile.FilePath);
        Log.Info($"Aceleração do emulador desligada em {changed} controle(s) de {_emulatorProfile.FilePath}");
    });

    private void TestButton_Click(object sender, RoutedEventArgs e) =>
        new PrecisionTestWindow { Owner = this }.ShowDialog();

    private void DistanceTestButton_Click(object sender, RoutedEventArgs e) =>
        new DistanceTestWindow { Owner = this }.ShowDialog();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
            return;

        e.Cancel = true;
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    private void FixButton_Click(object sender, RoutedEventArgs e) => Run(_service.RemoveAcceleration);

    private void RestoreButton_Click(object sender, RoutedEventArgs e) => Run(_service.Restore);

    private void AutoReapplyToggle_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        _settings.AutoReapply = AutoReapplyToggle.IsChecked == true;
        _settings.Save(AppSettings.DefaultPath);
    });

    private void AutoStartToggle_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (AutoStartToggle.IsChecked == true)
            AutoStart.Enable(Environment.ProcessPath!);
        else
            AutoStart.Disable();
    });

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Erro", System.Windows.MessageBoxButton.OK, MessageBoxImage.Error);
        }
        AutoStartToggle.IsChecked = AutoStart.IsEnabled;
        Refresh();
    }
}
