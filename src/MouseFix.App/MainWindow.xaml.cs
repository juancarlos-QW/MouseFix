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
    // The slider raises ValueChanged while XAML loads and when its value is set from code; ignore those.
    private bool _initializing = true;

    public MainWindow(MouseFixService service, AppSettings settings, ProtectionMonitor monitor)
    {
        _service = service;
        _settings = settings;
        _monitor = monitor;

        InitializeComponent();
        SystemThemeWatcher.Watch(this);
        FitToWorkArea();

        AutoReapplyToggle.IsChecked = settings.AutoReapply;
        AutoStartToggle.IsChecked = AutoStart.IsEnabled;
        UpdateCheckToggle.IsChecked = settings.CheckForUpdates;
        // The slider is the default speed; a running program's profile may be in effect instead.
        SpeedSlider.Value = settings.PointerSpeed;
        VersionText.Text = $"Versão {CurrentVersion.ToString(3)}";
        AboutText.Text = $"MouseFix {CurrentVersion.ToString(3)}";
        _initializing = false;
        ShowPage("Home");
        Refresh();
    }

    private void ShowPage(string page)
    {
        var pages = new Dictionary<string, (UIElement Page, Wpf.Ui.Controls.Button Nav)>
        {
            ["Home"] = (PageHome, NavHome),
            ["Profiles"] = (PageProfiles, NavProfiles),
            ["Emulator"] = (PageEmulator, NavEmulator),
            ["Tests"] = (PageTests, NavTests),
            ["Settings"] = (PageSettings, NavSettings),
        };
        foreach (var (name, (element, nav)) in pages)
        {
            bool selected = name == page;
            element.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            nav.Appearance = selected ? ControlAppearance.Secondary : ControlAppearance.Transparent;
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string page })
            ShowPage(page);
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e) =>
        Run(() => ProblemReporter.OpenInBrowser("https://github.com/juancarlos-QW/MouseFix"));

    private static Version CurrentVersion => typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Raised when the user turns update checks on, so the app checks right away.</summary>
    public event EventHandler? CheckForUpdatesRequested;

    // Never taller than the screen minus the taskbar, and centred in that area.
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Height = Math.Min(Height, area.Height - 16);
        Width = Math.Min(Width, area.Width - 16);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    /// <summary>When false, closing the window only hides it to the tray.</summary>
    public bool AllowClose { get; set; }

    public event EventHandler? HiddenToTray;

    public void Refresh()
    {
        var current = _service.Current;
        var target = _service.BuildTarget();
        bool curvesLinear = current.SmoothMouseXCurve.AsSpan().SequenceEqual(target.SmoothMouseXCurve)
                         && current.SmoothMouseYCurve.AsSpan().SequenceEqual(target.SmoothMouseYCurve);
        bool speedMatches = current.Speed == target.Speed;

        (SymbolRegular icon, string brush, string title, string subtitle) = (current.IsAccelerationEnabled, speedMatches, curvesLinear) switch
        {
            (true, _, _) => (SymbolRegular.DismissCircle24, "SystemFillColorCriticalBrush",
                "Aceleração ativa", "O Windows está alterando o movimento conforme a velocidade da mão."),
            (false, false, _) => (SymbolRegular.Warning24, "SystemFillColorCautionBrush",
                "Sem aceleração, outra velocidade",
                $"O Windows está em {current.Speed}/20, mas você escolheu {target.Speed}/20. Clique em \"Remover aceleração\" para aplicar."),
            (false, true, false) => (SymbolRegular.Warning24, "SystemFillColorCautionBrush",
                "Sem aceleração, sem proteção", "Aplique para impedir que jogos antigos religuem a aceleração."),
            _ when target.Speed == MouseSettings.OneToOneSpeed => (SymbolRegular.CheckmarkCircle24, "SystemFillColorSuccessBrush",
                "Aceleração removida", "Movimento 1:1: o cursor segue exatamente o sensor do mouse."),
            _ => (SymbolRegular.CheckmarkCircle24, "SystemFillColorSuccessBrush",
                "Aceleração removida",
                $"Velocidade {target.Speed}/20: todo movimento é multiplicado por {MouseSettings.SpeedMultiplier(target.Speed):0.###}x, devagar ou rápido."),
        };

        if (_monitor.ActiveProfile is { } active && !current.IsAccelerationEnabled && speedMatches)
            subtitle = $"Perfil \"{active.DisplayName}\" ativo. {subtitle}";

        StatusIcon.Symbol = icon;
        StatusIcon.SetResourceReference(ForegroundProperty, brush);
        StatusText.Text = title;
        StatusSubtext.Text = subtitle;

        AccelValue.Text = current.IsAccelerationEnabled ? "Ligada" : "Desligada";
        SpeedValue.Text = SpeedLabel(current.Speed);
        CurvesValue.Text = curvesLinear ? "Lineares" : "Padrão do Windows";

        int chosen = _settings.PointerSpeed;
        SpeedChoiceValue.Text = SpeedLabel(chosen);
        SpeedChoiceHint.Text = chosen switch
        {
            MouseSettings.OneToOneSpeed => "1:1 exato: cada ponto do sensor move 1 pixel. Mudar a velocidade nunca liga a aceleração.",
            < MouseSettings.OneToOneSpeed => "Mais lento e sem aceleração. Abaixo de 10, o cursor precisa de mais de um ponto do sensor para andar 1 pixel.",
            _ => "Mais rápido e sem aceleração. Acima de 10, o cursor pula pixels; para ganhar rapidez sem isso, aumente o DPI do mouse.",
        };
        if (_settings.Profiles.Count > 0)
            SpeedChoiceHint.Text = "Usada quando nenhum programa com perfil está aberto. " + SpeedChoiceHint.Text;
        SpeedResetButton.Visibility = chosen == MouseSettings.OneToOneSpeed ? Visibility.Collapsed : Visibility.Visible;

        RefreshProfiles();
        RefreshUpdate();

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

    private static string SpeedLabel(int speed) => speed == MouseSettings.OneToOneSpeed
        ? $"{speed}/20 (1:1)"
        : $"{speed}/20 ({MouseSettings.SpeedMultiplier(speed):0.###}x)";

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing)
            return;
        int speed = (int)Math.Round(e.NewValue);
        if (speed == _settings.PointerSpeed)
            return;

        // Choosing a speed means "this speed, no acceleration": apply it right away (with backup on first use).
        Run(() =>
        {
            _settings.PointerSpeed = speed;
            _settings.Save(AppSettings.DefaultPath);
            _service.RemoveAcceleration();
            Log.Info($"Velocidade do ponteiro escolhida: {speed}/20");
        });
    }

    private void SpeedResetButton_Click(object sender, RoutedEventArgs e) =>
        SpeedSlider.Value = MouseSettings.OneToOneSpeed;

    // Speed in effect while the emulator runs: its profile if there is one (BlueStacks-family emulators run as HD-Player).
    private int EmulatorSpeed =>
        _settings.Profiles.FirstOrDefault(p => p.ProcessName.Equals("HD-Player", StringComparison.OrdinalIgnoreCase))?.Speed
        ?? _settings.PointerSpeed;

    private void RefreshProfiles()
    {
        ProfilesList.Children.Clear();
        ProfilesEmpty.Visibility = _settings.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProfilesHint.Text = "Quando um programa da lista abrir (ex.: o emulador), o MouseFix troca a velocidade sozinho e volta " +
                            $"à velocidade padrão, {SpeedLabel(_settings.PointerSpeed)}, quando ele fechar. Se vários estiverem abertos, vale o primeiro " +
                            "da lista. A aceleração fica sempre desligada.";

        foreach (var profile in _settings.Profiles)
        {
            bool isActive = _monitor.ActiveProfile == profile;
            var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

            var name = new Wpf.Ui.Controls.TextBlock
            {
                Text = isActive ? $"{profile.DisplayName}  · ativo agora" : profile.DisplayName,
                FontTypography = isActive ? FontTypography.BodyStrong : FontTypography.Body,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"{profile.ProcessName}.exe",
            };
            var speed = new Wpf.Ui.Controls.TextBlock
            {
                Text = SpeedLabel(profile.Speed),
                FontTypography = FontTypography.BodyStrong,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 4, 0),
            };
            var remove = new Wpf.Ui.Controls.Button
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

            System.Windows.Controls.Grid.SetColumn(speed, 1);
            System.Windows.Controls.Grid.SetColumn(remove, 2);
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
                _service.RemoveAcceleration();
            _monitor.CheckNow();
        });
    }

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
