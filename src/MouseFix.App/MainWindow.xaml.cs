using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MouseFix.Core;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using TextBlock = System.Windows.Controls.TextBlock;

namespace MouseFix.App;

public partial class MainWindow : FluentWindow
{
    private readonly MouseFixService _service;
    private readonly AppSettings _settings;
    private readonly ProtectionMonitor _monitor;
    private EmulatorGameProfile? _emulatorProfile;
    // The slider raises ValueChanged while XAML loads and when its value is set from code; ignore those.
    private bool _initializing = true;

    // Choices on "Visão geral" take effect only when the user clicks "Aplicar configurações".
    private int _pendingSpeed;
    private bool _pendingAcceleration;
    private int _pendingScroll;
    // Result of the last one-click action, shown in the footer until the user changes something.
    private string? _feedback;

    public MainWindow(MouseFixService service, AppSettings settings, ProtectionMonitor monitor)
    {
        _service = service;
        _settings = settings;
        _monitor = monitor;

        InitializeComponent();
        FitToWorkArea();

        AutoReapplyToggle.IsChecked = settings.AutoReapply;
        AutoStartToggle.IsChecked = AutoStart.IsEnabled;
        UpdateCheckToggle.IsChecked = settings.CheckForUpdates;
        VersionText.Text = $"Versão {CurrentVersion.ToString(3)}";
        AboutText.Text = $"MouseFix {CurrentVersion.ToString(3)}";
        LoadPendingFromSettings();
        _initializing = false;
        NavHome.IsChecked = true;
        Refresh();
    }

    private static Version CurrentVersion => typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>When false, closing the window only hides it to the tray.</summary>
    public bool AllowClose { get; set; }

    public event EventHandler? HiddenToTray;

    /// <summary>Raised when the user turns update checks on, so the app checks right away.</summary>
    public event EventHandler? CheckForUpdatesRequested;

    // Never bigger than the screen minus the taskbar, and centred in that area.
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Height = Math.Min(Height, area.Height - 16);
        Width = Math.Min(Width, area.Width - 16);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    // ---------- Navigation ----------

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageHome is null)
            return; // still loading
        var pages = new (RadioButton Nav, UIElement Page)[]
        {
            (NavHome, PageHome), (NavProfiles, PageProfiles), (NavEmulator, PageEmulator),
            (NavTests, PageTests), (NavSettings, PageSettings),
        };
        foreach (var (nav, page) in pages)
            page.Visibility = nav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        // Apply / restore belong to the pointer settings on the overview.
        Footer.Visibility = NavHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GoToEmulator_Click(object sender, RoutedEventArgs e) => NavEmulator.IsChecked = true;

    private void CurrentProfile_Click(object sender, RoutedEventArgs e) => NavProfiles.IsChecked = true;

    private void GitHubButton_Click(object sender, RoutedEventArgs e) =>
        Run(() => ProblemReporter.OpenInBrowser("https://github.com/juancarlos-QW/MouseFix"));

    // ---------- Pending pointer settings ----------

    private int CurrentScrollLines => _service.Current.ScrollLines ?? 3;

    private void LoadPendingFromSettings()
    {
        _pendingSpeed = _settings.PointerSpeed;
        _pendingAcceleration = _settings.AccelerationEnabled;
        _pendingScroll = _settings.ScrollLines ?? CurrentScrollLines;

        bool wasInitializing = _initializing;
        _initializing = true;
        SpeedSlider.Value = _pendingSpeed;
        AccelToggle.IsChecked = _pendingAcceleration;
        _initializing = wasInitializing;
    }

    private bool HasPendingChanges =>
        _pendingSpeed != _settings.PointerSpeed ||
        _pendingAcceleration != _settings.AccelerationEnabled ||
        _pendingScroll != (_settings.ScrollLines ?? CurrentScrollLines);

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing)
            return;
        _pendingSpeed = (int)Math.Round(e.NewValue);
        RefreshPointerPanel();
    }

    private void SpeedResetButton_Click(object sender, RoutedEventArgs e) => SpeedSlider.Value = MouseSettings.OneToOneSpeed;

    private void AccelToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        _pendingAcceleration = AccelToggle.IsChecked == true;
        RefreshPointerPanel();
    }

    private void ScrollDown_Click(object sender, RoutedEventArgs e) => ChangeScroll(-1);

    private void ScrollUp_Click(object sender, RoutedEventArgs e) => ChangeScroll(+1);

    private void ChangeScroll(int delta)
    {
        _pendingScroll = Math.Clamp(_pendingScroll + delta, TargetOptions.MinScrollLines, TargetOptions.MaxScrollLines);
        RefreshPointerPanel();
    }

    private void FixButton_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        _settings.PointerSpeed = _pendingSpeed;
        _settings.AccelerationEnabled = _pendingAcceleration;
        // Start managing scrolling only once the user actually picks a value here.
        if (_settings.ScrollLines is not null || _pendingScroll != CurrentScrollLines)
            _settings.ScrollLines = _pendingScroll;
        _settings.Save(AppSettings.DefaultPath);
        _service.ApplyTarget();
        Log.Info($"Configurações aplicadas: velocidade {_pendingSpeed}/20, aceleração " +
                 $"{(_pendingAcceleration ? "ligada" : "desligada")}, rolagem {_pendingScroll} linha(s)");
    });

    private void RestoreButton_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        _service.Restore();
        LoadPendingFromSettings();
    });

    private void RemoveAccelButton_Click(object sender, RoutedEventArgs e) => Run(() => RemoveAccelerationNow());

    /// <summary>
    /// One click, no "Aplicar" needed: turns acceleration off in Windows (keeping the chosen speed and scrolling)
    /// and in the emulator's aim controls when possible. Returns a short summary of what was done.
    /// </summary>
    public string RemoveAccelerationNow()
    {
        _settings.AccelerationEnabled = false;
        _settings.Save(AppSettings.DefaultPath);
        _pendingAcceleration = false;
        _initializing = true;
        AccelToggle.IsChecked = false;
        _initializing = false;
        _service.ApplyTarget();

        string summary = "Aceleração do Windows removida.";
        var emulator = BlueStacksProfiles.Find(BlueStacksProfiles.DefaultRoot).FirstOrDefault(p => p.AnyAcceleration);
        if (emulator is not null)
        {
            if (BlueStacksProfiles.IsEmulatorRunning)
            {
                summary += $" Feche o {emulator.EmulatorName} e clique de novo para desligar a aceleração dele também.";
            }
            else
            {
                BlueStacksProfiles.DisableMouseAcceleration(emulator.FilePath);
                summary += $" A aceleração do {emulator.EmulatorName} também foi desligada.";
            }
        }
        Log.Info($"Remover aceleração: {summary}");
        _feedback = summary;
        return summary;
    }

    // ---------- Refresh ----------

    public void Refresh()
    {
        var current = _service.Current;
        var target = _service.BuildTarget();

        // Status pill: what Windows is doing right now.
        (SymbolRegular icon, string brush, string text) = current.IsAccelerationEnabled
            ? (SymbolRegular.Warning24, "MfWarning", "Aceleração ligada")
            : current.Speed == MouseSettings.OneToOneSpeed
                ? (SymbolRegular.CheckmarkCircle24, "MfSuccess", "Sem aceleração · 1:1")
                : (SymbolRegular.CheckmarkCircle24, "MfSuccess", $"Sem aceleração · {current.Speed}/20");
        StatusIcon.Symbol = icon;
        StatusIcon.SetResourceReference(ForegroundProperty, brush);
        StatusText.Text = text;

        AccelValue.Text = current.IsAccelerationEnabled ? "Ligada" : "Desligada";
        SpeedValue.Text = SpeedLabel(current.Speed);
        ScrollCurrentValue.Text = current.ScrollLines is int lines ? LinesLabel(lines) : "—";
        CurvesValue.Text = current.SmoothMouseXCurve.AsSpan().SequenceEqual(SmoothMouseCurve.WindowsDefaultX) ? "Padrão do Windows" : "Lineares";
        LoginInfo.IsOpen = _service.HasBackup;

        var active = _monitor.ActiveProfile;
        CurrentProfileText.Text = active is null
            ? $"Padrão · {SpeedLabel(_settings.PointerSpeed)}"
            : $"{active.DisplayName} · {SpeedLabel(active.Speed)}";

        var mouseSoftware = MouseSoftware.FindRunning();
        MouseSoftwareInfo.IsOpen = mouseSoftware.Count > 0;
        MouseSoftwareInfo.Title = $"{string.Join(", ", mouseSoftware)} aberto";
        MouseSoftwareInfo.Message = "Programas de mouse podem aplicar aceleração no driver, antes do Windows, onde o MouseFix " +
                                    "não alcança. O teste de distância mostra se isso acontece.";

        RefreshPointerPanel(current, target);
        RefreshProfiles();
        RefreshUpdate();
        RefreshEmulator();
    }

    private void RefreshPointerPanel() => RefreshPointerPanel(_service.Current, _service.BuildTarget());

    private void RefreshPointerPanel(MouseSettings current, MouseSettings target)
    {
        SpeedChoiceValue.Text = $"{_pendingSpeed} / 20";
        SpeedChoiceHint.Text = (_pendingSpeed switch
        {
            MouseSettings.OneToOneSpeed => "1:1 exato: cada ponto do sensor move 1 pixel.",
            < MouseSettings.OneToOneSpeed => $"{MouseSettings.SpeedMultiplier(_pendingSpeed):0.###}x, mais lento. Só 10 é 1:1 exato.",
            _ => $"{MouseSettings.SpeedMultiplier(_pendingSpeed):0.###}x, mais rápido; acima de 10 o cursor pula pixels.",
        }) + (_settings.Profiles.Count > 0 ? " Vale quando nenhum perfil está ativo." : "");
        SpeedResetButton.Visibility = _pendingSpeed == MouseSettings.OneToOneSpeed ? Visibility.Collapsed : Visibility.Visible;

        AccelHint.Text = _pendingAcceleration
            ? "Ligada: o cursor anda mais quando você move rápido. Não recomendado para jogos."
            : "Movimento consistente e previsível.";

        ScrollValue.Text = LinesLabel(_pendingScroll);
        ScrollNumber.Text = _pendingScroll.ToString();
        ScrollDown.IsEnabled = _pendingScroll > TargetOptions.MinScrollLines;
        ScrollUp.IsEnabled = _pendingScroll < TargetOptions.MaxScrollLines;

        RestoreButton.IsEnabled = _service.HasBackup;
        bool pending = HasPendingChanges;
        bool inSync = current.Equals(target) && _service.HasBackup;
        FixButton.IsEnabled = pending || !inSync;

        if (pending)
            _feedback = null;
        (SymbolRegular icon, string brush, string text) = (pending, inSync) switch
        {
            (true, _) => (SymbolRegular.Info24, "MfTextMuted", "Alterações ainda não aplicadas."),
            (false, true) when _feedback is not null => (SymbolRegular.CheckmarkCircle24, "MfSuccess", _feedback),
            (false, true) when _monitor.ActiveProfile is { } p =>
                (SymbolRegular.CheckmarkCircle24, "MfSuccess", $"Aplicado. Perfil \"{p.DisplayName}\" ativo agora ({p.Speed}/20)."),
            (false, true) => (SymbolRegular.CheckmarkCircle24, "MfSuccess", "Tudo aplicado e protegido."),
            (false, false) when !_service.HasBackup =>
                (SymbolRegular.Info24, "MfTextMuted", "O MouseFix ainda não está aplicando nada. Clique em \"Aplicar configurações\"."),
            _ => (SymbolRegular.Warning24, "MfWarning", "O Windows está diferente do que você escolheu. Clique em \"Aplicar configurações\"."),
        };
        FooterIcon.Symbol = icon;
        FooterIcon.SetResourceReference(ForegroundProperty, brush);
        FooterText.Text = text;
    }

    private static string SpeedLabel(int speed) => speed == MouseSettings.OneToOneSpeed
        ? $"{speed}/20 (1:1)"
        : $"{speed}/20 ({MouseSettings.SpeedMultiplier(speed):0.###}x)";

    private static string LinesLabel(int lines) => lines == 1 ? "1 linha" : $"{lines} linhas";

    // ---------- Profiles ----------

    // Speed in effect while the emulator runs: its profile if there is one (BlueStacks-family emulators run as HD-Player).
    private int EmulatorSpeed =>
        _settings.Profiles.FirstOrDefault(p => p.ProcessName.Equals("HD-Player", StringComparison.OrdinalIgnoreCase))?.Speed
        ?? _settings.PointerSpeed;

    private void RefreshProfiles()
    {
        ProfilesList.Children.Clear();
        ProfilesEmpty.Visibility = _settings.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProfilesHint.Text = "Quando um programa da lista abrir (ex.: o emulador), o MouseFix troca a velocidade sozinho e volta " +
                            $"à velocidade padrão, {SpeedLabel(_settings.PointerSpeed)}, quando ele fechar. Se vários estiverem abertos, " +
                            "vale o primeiro da lista.";

        for (int i = 0; i < _settings.Profiles.Count; i++)
        {
            var profile = _settings.Profiles[i];
            bool isActive = _monitor.ActiveProfile == profile;
            var row = new Grid { Margin = new Thickness(0, i == 0 ? 0 : 14, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new SymbolIcon(SymbolRegular.Apps24) { FontSize = 22, VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(ForegroundProperty, isActive ? "MfAccent" : "MfTextMuted");
            var name = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock
            {
                Text = profile.DisplayName,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var sub = new TextBlock { Text = isActive ? $"{profile.ProcessName}.exe · ativo agora" : $"{profile.ProcessName}.exe" };
            sub.SetResourceReference(ForegroundProperty, isActive ? "MfAccent" : "MfTextMuted");
            name.Children.Add(sub);
            var speed = new TextBlock
            {
                Text = SpeedLabel(profile.Speed),
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 8, 0),
            };
            var remove = new Button
            {
                Icon = new SymbolIcon(SymbolRegular.Delete24),
                Appearance = ControlAppearance.Transparent,
                ToolTip = "Remover perfil",
            };
            remove.Click += (_, _) => Run(() =>
            {
                _settings.Profiles.Remove(profile);
                _settings.Save(AppSettings.DefaultPath);
                Log.Info($"Perfil removido: {profile.DisplayName}");
                _monitor.CheckNow();
            });

            Grid.SetColumn(name, 1);
            Grid.SetColumn(speed, 2);
            Grid.SetColumn(remove, 3);
            row.Children.Add(icon);
            row.Children.Add(name);
            row.Children.Add(speed);
            row.Children.Add(remove);
            ProfilesList.Children.Add(row);
        }
    }

    private void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileWindow(_settings.PointerSpeed) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } profile)
            return;

        Run(() =>
        {
            // One profile per program: adding it again replaces the old speed.
            _settings.Profiles.RemoveAll(p => p.ProcessName.Equals(profile.ProcessName, StringComparison.OrdinalIgnoreCase));
            _settings.Profiles.Add(profile);
            _settings.Save(AppSettings.DefaultPath);
            Log.Info($"Perfil adicionado: {profile.DisplayName} ({profile.ProcessName}) → {profile.Speed}/20");
            // Profiles act on the MouseFix settings, so make sure they are applied (and backed up) once.
            if (!_service.HasBackup)
                _service.ApplyTarget();
            _monitor.CheckNow();
        });
    }

    // ---------- Updates and help ----------

    private void RefreshUpdate()
    {
        var available = UpdateChecker.ParseVersion(_settings.AvailableUpdateVersion);
        bool show = _settings.CheckForUpdates && available is not null && UpdateChecker.IsNewer(available, CurrentVersion);
        UpdateCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
            UpdateTitle.Text = $"MouseFix {available!.ToString(3)} disponível (você tem a {CurrentVersion.ToString(3)})";
    }

    private void UpdateDownloadButton_Click(object sender, RoutedEventArgs e) => Run(() =>
        ProblemReporter.OpenInBrowser(_settings.AvailableUpdateUrl ?? "https://github.com/juancarlos-QW/MouseFix/releases/latest"));

    private void UpdateCheckToggle_Click(object sender, RoutedEventArgs e)
    {
        Run(() =>
        {
            _settings.CheckForUpdates = UpdateCheckToggle.IsChecked == true;
            _settings.Save(AppSettings.DefaultPath);
        });
        if (_settings.CheckForUpdates)
            CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ReportButton_Click(object sender, RoutedEventArgs e) => Run(() => ProblemReporter.Open(_service, _monitor));

    private void LogFolderButton_Click(object sender, RoutedEventArgs e) => Run(ProblemReporter.OpenLogFolder);

    // ---------- Emulator ----------

    private void RefreshEmulator()
    {
        // With several emulators/games, show the mapping the user touched most recently.
        var profile = BlueStacksProfiles.Find(BlueStacksProfiles.DefaultRoot)
            .OrderByDescending(p => System.IO.File.GetLastWriteTimeUtc(p.FilePath))
            .FirstOrDefault();
        _emulatorProfile = profile;

        EmulatorCard.Visibility = profile?.SelectedAim is null ? Visibility.Collapsed : Visibility.Visible;
        EmulatorEmpty.Visibility = profile?.SelectedAim is null ? Visibility.Visible : Visibility.Collapsed;
        if (profile?.SelectedAim is not { } aim)
            return;

        EmulatorTitle.Text = $"{profile.EmulatorName} · {profile.GameName}";
        EmulatorAccelValue.Text = profile.AnyAcceleration ? "Ligada" : "Desligada";
        EmulatorSchemeValue.Text = aim.SchemeName;
        EmulatorSensValue.Text = $"{aim.Sensitivity:0.##} / {aim.SensitivityRatioY:P0}";
        EmulatorFixButton.Visibility = profile.AnyAcceleration ? Visibility.Visible : Visibility.Collapsed;

        EmulatorTestButton.Content = _settings.EmulatorUsesWindowsCursor is null ? "Testar emulador" : "Testar de novo";
        EmulatorAdvice.Text = profile.AnyAcceleration
            ? "O emulador está aplicando aceleração própria na mira. Feche o emulador e desligue acima."
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
                ? $"Teste feito: a mira do {game} segue o cursor do Windows. Sensibilidade já ajustada para a " +
                  "velocidade atual (mesma sensação de antes do MouseFix)."
                : $"Teste feito: a mira do {game} segue o cursor do Windows. Antes do MouseFix a velocidade era " +
                  $"{_service.Original!.Speed}/20 e agora é {EmulatorSpeed}/20, por isso a mira mudou. " +
                  $"Com sensibilidade X {s:0.00} a sensação volta a ser a de antes."
            : $"Teste feito: a mira do {game} segue o cursor do Windows, então as configurações do MouseFix chegam até o jogo.",
    };

    /// <summary>
    /// Emulator sensitivity that keeps the in-game feel from before MouseFix, given the pointer speed now in use.
    /// Only meaningful when the emulator follows the Windows cursor. Based on the sensitivity from before
    /// any adjustment, so applying it doesn't keep compounding, and choosing the old speed again suggests the old value.
    /// </summary>
    private double? SuggestedSensitivity(EmulatorAim aim)
    {
        var original = _service.Original;
        if (_settings.EmulatorUsesWindowsCursor != true || original is null)
            return null;

        double baseSensitivity = _settings.EmulatorSensitivityBeforeAdjust ?? aim.Sensitivity;
        double ratio = MouseSettings.SpeedMultiplier(original.Speed) / MouseSettings.SpeedMultiplier(EmulatorSpeed);
        return Math.Round(baseSensitivity * ratio, 2);
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
        MessageBox.Show(this,
            $"Feche o {_emulatorProfile?.EmulatorName ?? "emulador"} antes. Ele regrava esse arquivo e desfaria a mudança.",
            "MouseFix", MessageBoxButton.OK, MessageBoxImage.Information);
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

    // ---------- Tests and settings ----------

    private void TestButton_Click(object sender, RoutedEventArgs e) =>
        new PrecisionTestWindow { Owner = this }.ShowDialog();

    private void DistanceTestButton_Click(object sender, RoutedEventArgs e) =>
        new DistanceTestWindow { Owner = this }.ShowDialog();

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

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
            return;

        e.Cancel = true;
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        AutoStartToggle.IsChecked = AutoStart.IsEnabled;
        Refresh();
    }
}
