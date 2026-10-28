using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MouseFix.Core;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using TextBlock = System.Windows.Controls.TextBlock;

namespace MouseFix.App;

/// <summary>
/// One simple main screen (status, "Remover aceleração", pointer speed, verification), plus History and Settings pages.
/// Every change is applied right away, read back from Windows to verify it, and recorded in the history.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MouseFixService _service;
    private readonly AppSettings _settings;
    private readonly ProtectionMonitor _monitor;
    private readonly ChangeHistory _history;
    private readonly GameModeService _gameMode;
    private EmulatorGameProfile? _emulatorProfile;
    // Controls raise change events while XAML loads and when set from code; ignore those.
    private bool _initializing = true;

    // The speed slider applies once the user stops moving it, not on every notch.
    private readonly DispatcherTimer _speedApplyTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    // Keeps the status honest while the window is open, even if nothing in MouseFix triggered a refresh.
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow(MouseFixService service, AppSettings settings, ProtectionMonitor monitor, ChangeHistory history,
        GameModeService gameMode)
    {
        _service = service;
        _settings = settings;
        _monitor = monitor;
        _history = history;
        _gameMode = gameMode;

        InitializeComponent();
        FitToWorkArea();

        AutoReapplyToggle.IsChecked = settings.AutoReapply;
        AutoStartToggle.IsChecked = AutoStart.IsEnabled;
        UpdateCheckToggle.IsChecked = settings.CheckForUpdates;
        SpeedSlider.Value = settings.PointerSpeed;
        AccelToggle.IsChecked = settings.AccelerationEnabled;
        GameModeAutoCheck.IsChecked = settings.GameModeWithEmulator;
        AboutText.Text = $"MouseFix {CurrentVersion.ToString(3)}";
        TitleVersion.Text = $"v{CurrentVersion.ToString(3)}";
        LanguageBox.SelectedItem = LanguageBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (settings.Language ?? ""))
            ?? LanguageBox.Items[0];
        _initializing = false;

        _speedApplyTimer.Tick += (_, _) => ApplySpeedNow();
        _statusTimer.Tick += (_, _) => RefreshStatus();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                _statusTimer.Start();
            else
                _statusTimer.Stop();
        };
        // Rebuilding the list is only worth it while the History page is on screen; it's rebuilt when opened.
        _history.Changed += (_, _) =>
        {
            if (IsVisible && PageHistory.Visibility == Visibility.Visible)
                RefreshHistory();
            else if (IsVisible)
                RefreshVerification();
        };
        ShowPage(Page.Home);
        Refresh();
    }

    private enum Page { Home, History, Settings }

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

    // ---------- Pages ----------

    private void ShowPage(Page page)
    {
        PageHome.Visibility = page == Page.Home ? Visibility.Visible : Visibility.Collapsed;
        PageHistory.Visibility = page == Page.History ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = page == Page.Settings ? Visibility.Visible : Visibility.Collapsed;
        MainHeaderButtons.Visibility = page == Page.Home ? Visibility.Visible : Visibility.Collapsed;
        SubPageHeader.Visibility = page == Page.Home ? Visibility.Collapsed : Visibility.Visible;
        PageTitleText.Text = page == Page.History ? L.T("Histórico", "History") : L.T("Configurações", "Settings");
        if (page == Page.History)
            RefreshHistory();
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e) => ShowPage(Page.History);

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowPage(Page.Settings);

    private void BackButton_Click(object sender, RoutedEventArgs e) => ShowPage(Page.Home);

    private void GitHubButton_Click(object sender, RoutedEventArgs e) =>
        Run(() => ProblemReporter.OpenInBrowser("https://github.com/juancarlos-QW/MouseFix"));

    // ---------- Apply + verify + record ----------

    /// <summary>Applies the settings in <see cref="_settings"/>, verifies them and records the result.</summary>
    private void ApplyAndRecord(string description)
    {
        _settings.Save(AppSettings.DefaultPath);
        string? problem = _service.ApplyTarget();
        Record(ChangeSource.User, description, problem);
    }

    private void Record(ChangeSource source, string description, string? problem)
    {
        _history.Add(new ChangeEntry(DateTime.Now, source, description, problem is null, problem));
        Log.Info(problem is null ? $"{description} (verificado)" : $"{description} — NÃO aplicado: {problem}");
    }

    private void RemoveAccelButton_Click(object sender, RoutedEventArgs e) => Run(() => RemoveAccelerationNow());

    /// <summary>
    /// One click: turns acceleration off in Windows (keeping the chosen speed and scrolling) and in the emulator's aim
    /// controls when possible, verifies it and records it. Returns a short summary of what was done.
    /// </summary>
    public string RemoveAccelerationNow()
    {
        _settings.AccelerationEnabled = false;
        SetSilently(() => AccelToggle.IsChecked = false);
        _settings.Save(AppSettings.DefaultPath);
        string? problem = _service.ApplyTarget();

        string summary = problem is null ? L.T("Aceleração do Windows removida.", "Windows acceleration removed.") : L.T($"Não foi possível remover tudo: {problem}.", $"Could not remove everything: {problem}.");
        var (emulatorSummary, emulatorProblem) = DisableEmulatorAcceleration();
        if (emulatorSummary is not null)
            summary += " " + emulatorSummary;
        Record(ChangeSource.User, summary, problem ?? emulatorProblem);
        return summary;
    }

    /// <summary>
    /// Turns off "Mouse acceleration" in every Free Fire mapping of every supported emulator that has it on.
    /// Returns a sentence for the user (null when there was nothing to do) and what failed, if anything.
    /// Never throws, so a locked emulator file can't hide that the Windows part already worked.
    /// </summary>
    private (string? Summary, string? Problem) DisableEmulatorAcceleration()
    {
        try
        {
            var withAcceleration = BlueStacksProfiles.Find(BlueStacksProfiles.DefaultRoot).Where(p => p.AnyAcceleration).ToList();
            if (withAcceleration.Count == 0)
                return (null, null);

            string names = string.Join(", ", withAcceleration.Select(p => $"{p.EmulatorName} ({p.GameName})").Distinct());
            if (BlueStacksProfiles.IsEmulatorRunning)
                return (L.T($"Feche o emulador e clique de novo para desligar a aceleração de: {names}.", $"Close the emulator and click again to turn off acceleration in: {names}."),
                        L.T("o emulador estava aberto", "the emulator was open"));

            var stillOn = new List<string>();
            foreach (var profile in withAcceleration)
            {
                // Edit the user's copy: the emulator prefers it, and updates can replace its built-in mappings.
                string file = BlueStacksProfiles.EnsureUserCopy(profile.FilePath);
                BlueStacksProfiles.DisableMouseAcceleration(file);
                if (BlueStacksProfiles.Load(profile.EmulatorName, profile.GameName, file).AnyAcceleration)
                    stillOn.Add($"{profile.EmulatorName} ({profile.GameName})");
            }
            return stillOn.Count == 0
                ? (L.T($"A aceleração do emulador também foi desligada ({names}).", $"The emulator's acceleration was turned off too ({names})."), null)
                : (L.T($"A aceleração continua ligada em: {string.Join(", ", stillOn)}.", $"Acceleration is still on in: {string.Join(", ", stillOn)}."), L.T("o arquivo do emulador não mudou", "the emulator's file did not change"));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Error("Não foi possível alterar o arquivo do emulador", ex);
            return (L.T($"Não foi possível alterar o arquivo do emulador ({ex.Message}).", $"Could not change the emulator's file ({ex.Message})."), L.T("arquivo do emulador bloqueado", "emulator file locked"));
        }
    }

    // Changes a control from code without its change handler treating it as a user action.
    private void SetSilently(Action change)
    {
        bool wasInitializing = _initializing;
        _initializing = true;
        try
        {
            change();
        }
        finally
        {
            _initializing = wasInitializing;
        }
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing)
            return;
        RefreshSpeedText((int)Math.Round(e.NewValue));
        _speedApplyTimer.Stop();
        _speedApplyTimer.Start();
    }

    private void ApplySpeedNow()
    {
        _speedApplyTimer.Stop();
        int speed = (int)Math.Round(SpeedSlider.Value);
        if (speed == _settings.PointerSpeed && _service.HasBackup)
            return;
        Run(() =>
        {
            _settings.PointerSpeed = speed;
            ApplyAndRecord(L.T($"Velocidade do ponteiro: {SpeedLabel(speed)}", $"Pointer speed: {SpeedLabel(speed)}"));
        });
    }

    private void SpeedResetButton_Click(object sender, RoutedEventArgs e) => SpeedSlider.Value = MouseSettings.OneToOneSpeed;

    private void AccelToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        bool on = AccelToggle.IsChecked == true;
        Run(() =>
        {
            _settings.AccelerationEnabled = on;
            ApplyAndRecord(on ? L.T("Aceleração ligada (escolha sua)", "Acceleration on (your choice)") : L.T("Aceleração desligada", "Acceleration off"));
        });
    }

    private void ScrollDown_Click(object sender, RoutedEventArgs e) => ChangeScroll(-1);

    private void ScrollUp_Click(object sender, RoutedEventArgs e) => ChangeScroll(+1);

    private void ChangeScroll(int delta) => Run(() =>
    {
        int lines = Math.Clamp((_settings.ScrollLines ?? CurrentScrollLines) + delta,
            TargetOptions.MinScrollLines, TargetOptions.MaxScrollLines);
        _settings.ScrollLines = lines;
        ApplyAndRecord(L.T($"Velocidade de rolagem: {LinesLabel(lines)}", $"Scroll speed: {LinesLabel(lines)}"));
    });

    private int CurrentScrollLines => _service.Current.ScrollLines ?? 3;

    private void RestoreButton_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        string? problem = _service.Restore();
        Record(ChangeSource.Restore, L.T("Configurações originais do Windows restauradas", "Original Windows settings restored"), problem);
    });

    // ---------- Refresh ----------

    /// <summary>Updates only what Windows has right now (cheap; safe to call often).</summary>
    public void RefreshQuick()
    {
        if (IsVisible)
            RefreshStatus();
    }

    public void Refresh()
    {
        RefreshStatus();

        AccelHint.Text = _settings.AccelerationEnabled
            ? L.T("Ligada: o cursor anda mais quando você move rápido. Não recomendado para jogos.", "On: the cursor moves further when you move fast. Not recommended for games.")
            : L.T("Desligada: movimento consistente e previsível (recomendado).", "Off: consistent, predictable movement (recommended).");
        int lines = _settings.ScrollLines ?? CurrentScrollLines;
        ScrollValue.Text = L.T($"{LinesLabel(lines)} por giro da roda", $"{LinesLabel(lines)} per wheel notch");
        ScrollNumber.Text = lines.ToString();
        ScrollDown.IsEnabled = lines > TargetOptions.MinScrollLines;
        ScrollUp.IsEnabled = lines < TargetOptions.MaxScrollLines;
        LoginInfo.IsOpen = _service.HasBackup;
        RestoreButton.IsEnabled = _service.HasBackup;

        var mouseSoftware = MouseSoftware.FindRunning();
        MouseSoftwareInfo.IsOpen = mouseSoftware.Count > 0;
        MouseSoftwareInfo.Title = L.T($"{string.Join(", ", mouseSoftware)} aberto", $"{string.Join(", ", mouseSoftware)} is open");
        MouseSoftwareInfo.Message = L.T("Programas de mouse podem aplicar aceleração no driver, onde o MouseFix não alcança. ", "Mouse programs can add acceleration in the driver, where MouseFix can't reach. ") +
                                    L.T("O teste de distância mostra se isso acontece.", "The distance test shows whether that happens.");

        RefreshProfiles();
        RefreshUpdate();
        RefreshEmulator();
    }

    /// <summary>
    /// The cheap part: what Windows has right now. Also runs every second while the window is visible, so a change
    /// made by another program shows up even when automatic protection is off.
    /// </summary>
    private void RefreshStatus()
    {
        var current = _service.Current;
        int target = _service.TargetSpeed;
        var active = _monitor.ActiveProfile;
        string profile = active is null ? "" : L.T($" · perfil {active.DisplayName}", $" · profile {active.DisplayName}");

        (string text, string sub, SymbolRegular icon, string iconBrush, string background) = current.IsAccelerationEnabled
            ? _settings.AccelerationEnabled
                ? (L.T("Aceleração ligada", "Acceleration on"), L.T("Você escolheu deixar ligada (Configurações › Avançado).", "You chose to keep it on (Settings › Advanced)."), SymbolRegular.Warning24, "MfWarning", "#33291A")
                : (L.T("Aceleração ligada", "Acceleration on"), L.T("O Windows está acelerando o movimento. Clique no botão abaixo.", "Windows is accelerating the movement. Click the button below."), SymbolRegular.DismissCircle24, "MfDanger", "#3A1A20")
            : current.Speed == target
                ? (L.T("Sem aceleração", "No acceleration"), L.T($"Velocidade {SpeedLabel(current.Speed)}{profile}", $"Speed {SpeedLabel(current.Speed)}{profile}"), SymbolRegular.CheckmarkCircle24, "MfSuccess", "#123526")
                : (L.T("Sem aceleração", "No acceleration"), L.T($"Velocidade {current.Speed}/20, mas a escolhida é {target}/20", $"Speed {current.Speed}/20, but the chosen one is {target}/20"), SymbolRegular.Warning24, "MfWarning", "#33291A");
        StatusText.Text = text;
        StatusSubtext.Text = sub;
        StatusIcon.Symbol = icon;
        StatusIcon.SetResourceReference(ForegroundProperty, iconBrush);
        StatusCircle.Background = (Brush)new BrushConverter().ConvertFromString(background)!;

        if (!_speedApplyTimer.IsEnabled)
            RefreshSpeedText(_settings.PointerSpeed);
        RefreshVerification();

        RefreshGameMode();

        AccelValue.Text = current.IsAccelerationEnabled ? L.T("Ligada", "On") : L.T("Desligada", "Off");
        SpeedValue.Text = SpeedLabel(current.Speed);
        ScrollCurrentValue.Text = current.ScrollLines is int l ? LinesLabel(l) : "—";
        CurvesValue.Text = current.SmoothMouseXCurve.AsSpan().SequenceEqual(SmoothMouseCurve.WindowsDefaultX) ? L.T("Padrão do Windows", "Windows default") : L.T("Lineares", "Linear");
    }

    private void RefreshSpeedText(int speed)
    {
        SpeedChoiceValue.Text = speed == MouseSettings.OneToOneSpeed ? "10 / 20 · 1:1" : $"{speed} / 20";
        string factor = speed switch
        {
            MouseSettings.OneToOneSpeed => L.T("1:1 exato: cada ponto do sensor move 1 pixel.", "Exact 1:1: each sensor count moves 1 pixel."),
            < MouseSettings.OneToOneSpeed => L.T($"{MouseSettings.SpeedMultiplier(speed):0.###}x, mais lento. Só 10 é 1:1 exato.", $"{MouseSettings.SpeedMultiplier(speed):0.###}x, slower. Only 10 is exact 1:1."),
            _ => L.T($"{MouseSettings.SpeedMultiplier(speed):0.###}x, mais rápido; acima de 10 o cursor pula pixels.", $"{MouseSettings.SpeedMultiplier(speed):0.###}x, faster; above 10 the cursor skips pixels."),
        };
        SpeedChoiceHint.Text = L.T($"{factor} Muda só o Windows, não o DPI do sensor do mouse.", $"{factor} Changes only Windows, not the mouse sensor's DPI.");
        SpeedResetButton.Visibility = speed == MouseSettings.OneToOneSpeed ? Visibility.Collapsed : Visibility.Visible;
    }

    // Shows the latest recorded change if it was verified or failed; otherwise checks Windows right now.
    private void RefreshVerification()
    {
        var last = _history.Entries.FirstOrDefault();
        string? live = _service.HasBackup ? _service.VerifyTarget() : null;

        (SymbolRegular icon, string brush, string text) = (last, live) switch
        {
            ({ Verified: false } l, _) when DateTime.Now - l.Time < TimeSpan.FromMinutes(10) && live is not null =>
                (SymbolRegular.ErrorCircle24, "MfDanger", L.T($"Não foi possível aplicar: {l.Problem}.", $"Could not apply: {l.Problem}.")),
            (_, null) when !_service.HasBackup =>
                (SymbolRegular.Info24, "MfTextMuted", L.T("O MouseFix ainda não aplicou nada. Clique em \"Remover aceleração\".", "MouseFix hasn't applied anything yet. Click \"Remove acceleration\".")),
            (_, not null) =>
                (SymbolRegular.Warning24, "MfWarning", L.T($"O Windows está diferente do escolhido: {live}.", $"Windows differs from your choice: {live}.")),
            ({ Verified: true } l, null) when l.Time.Date == DateTime.Today =>
                (SymbolRegular.CheckmarkCircle24, "MfSuccess", L.T($"Aplicado e verificado às {l.Time:HH:mm}.", $"Applied and verified at {l.Time:HH:mm}.")),
            _ => (SymbolRegular.CheckmarkCircle24, "MfSuccess", L.T("Configuração verificada agora.", "Settings verified just now.")),
        };
        VerifyIcon.Symbol = icon;
        VerifyIcon.SetResourceReference(ForegroundProperty, brush);
        VerifyText.Text = text;
    }

    // ---------- Game mode ----------

    private void RefreshGameMode()
    {
        bool active;
        string planName;
        try
        {
            active = _gameMode.IsActive;
            planName = _gameMode.ActivePlanName;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            GameModeText.Text = L.T("Não foi possível ler o plano de energia do Windows.", "Could not read the Windows power plan.");
            return;
        }

        GameModeChipText.Text = active ? L.T("ATIVO", "ON") : L.T("DESLIGADO", "OFF");
        GameModeChipText.SetResourceReference(TextBlock.ForegroundProperty, active ? "MfSuccess" : "MfTextMuted");
        GameModeChip.Background = (Brush)new BrushConverter().ConvertFromString(active ? "#123526" : "#1A2434")!;
        GameModeIcon.SetResourceReference(ForegroundProperty, active ? "MfAccent" : "MfTextMuted");

        GameModeText.Text = active
            ? L.T($"Plano de energia \"{GameModeService.PlanName}\": processador sempre em 100%, sem economia de energia e o emulador com prioridade alta.", $"Power plan \"{GameModeService.PlanName}\": processor always at 100%, no power saving, and the emulator at high priority.") +
              (_gameMode.ActivatedAutomatically ? L.T(" Ligado porque o emulador está aberto; volta sozinho quando fechar.", " On because the emulator is open; it goes back by itself when it closes.") : "")
            : L.T($"Plano de energia atual: {planName}. O modo jogo deixa o PC no desempenho máximo enquanto você joga.", $"Current power plan: {planName}. Game mode keeps the PC at maximum performance while you play.");
        GameModeButton.Content = active ? L.T("Voltar ao plano normal", "Back to the normal plan") : L.T("Ativar modo jogo", "Turn on game mode");
        GameModeButton.Appearance = active ? ControlAppearance.Secondary : ControlAppearance.Primary;
    }

    private void GameModeButton_Click(object sender, RoutedEventArgs e) => Run(() => _monitor.ToggleGameMode());

    private void GameModeAutoCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        Run(() =>
        {
            _settings.GameModeWithEmulator = GameModeAutoCheck.IsChecked == true;
            _settings.Save(AppSettings.DefaultPath);
            Log.Info($"Modo jogo automático com emulador: {(_settings.GameModeWithEmulator ? "ligado" : "desligado")}");
            // Turning it on with the emulator already open should take effect now, not on the next launch.
            if (_settings.GameModeWithEmulator)
                _monitor.RecheckEmulator();
        });
    }

    private static string SpeedLabel(int speed) => speed == MouseSettings.OneToOneSpeed
        ? $"{speed}/20 (1:1)"
        : $"{speed}/20 ({MouseSettings.SpeedMultiplier(speed):0.###}x)";

    private static string LinesLabel(int lines) => lines == 1 ? L.T("1 linha", "1 line") : L.T($"{lines} linhas", $"{lines} lines");

    // ---------- History ----------

    private void RefreshHistory()
    {
        HistoryList.Children.Clear();
        HistoryEmpty.Visibility = _history.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearHistoryButton.IsEnabled = _history.Entries.Count > 0;

        foreach (var entry in _history.Entries.Take(100))
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var (sourceIcon, who) = entry.Source switch
            {
                ChangeSource.User => (SymbolRegular.Person24, L.T("Você", "You")),
                ChangeSource.Profile => (SymbolRegular.AppsList24, entry.Program is null ? L.T("Perfil", "Profile") : L.T($"Perfil {entry.Program}", $"Profile {entry.Program}")),
                ChangeSource.OtherProgram => (SymbolRegular.Warning24, L.T($"Outro programa: {entry.Program ?? "desconhecido"}", $"Other program: {entry.Program ?? "unknown"}")),
                ChangeSource.MouseFix => (SymbolRegular.ShieldCheckmark24, "MouseFix"),
                ChangeSource.GameMode => (SymbolRegular.Flash24, L.T("Modo jogo", "Game mode")),
                _ => (SymbolRegular.ArrowUndo24, L.T("Restaurar", "Restore")),
            };
            var icon = new SymbolIcon(sourceIcon) { FontSize = 20, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
            icon.SetResourceReference(ForegroundProperty, entry.Source == ChangeSource.OtherProgram ? "MfWarning" : "MfAccent");

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = entry.Description, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
            var meta = new TextBlock
            {
                Text = $"{entry.Time.ToString(L.T("dd/MM HH:mm", "MMM d, HH:mm"), System.Globalization.CultureInfo.InvariantCulture)} · {who}{(entry.Count > 1 ? $" · {entry.Count}x" : "")}",
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
            };
            meta.SetResourceReference(ForegroundProperty, "MfTextMuted");
            text.Children.Add(meta);
            if (!entry.Verified && entry.Problem is not null)
            {
                var problem = new TextBlock { Text = L.T($"Não aplicado: {entry.Problem}", $"Not applied: {entry.Problem}"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
                problem.SetResourceReference(ForegroundProperty, "MfDanger");
                text.Children.Add(problem);
            }

            var badge = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 2, 0, 0) };
            var badgeIcon = new SymbolIcon(entry.Verified ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.ErrorCircle24) { FontSize = 16 };
            badgeIcon.SetResourceReference(ForegroundProperty, entry.Verified ? "MfSuccess" : "MfDanger");
            var badgeText = new TextBlock { Text = entry.Verified ? L.T("Verificado", "Verified") : L.T("Falhou", "Failed"), FontSize = 12, Margin = new Thickness(4, 0, 0, 0) };
            badgeText.SetResourceReference(ForegroundProperty, entry.Verified ? "MfSuccess" : "MfDanger");
            badge.Children.Add(badgeIcon);
            badge.Children.Add(badgeText);

            Grid.SetColumn(text, 1);
            Grid.SetColumn(badge, 2);
            row.Children.Add(icon);
            row.Children.Add(text);
            row.Children.Add(badge);
            if (HistoryList.Children.Count > 0)
                HistoryList.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("MfDivider") });
            HistoryList.Children.Add(row);
        }
        RefreshVerification();
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e) => Run(_history.Clear);

    // ---------- Profiles ----------

    // Speed in effect while the emulator runs: its profile if there is one (BlueStacks-family emulators run as HD-Player).
    private int EmulatorSpeed =>
        _settings.Profiles.FirstOrDefault(p => p.ProcessName.Equals("HD-Player", StringComparison.OrdinalIgnoreCase))?.Speed
        ?? _settings.PointerSpeed;

    private void RefreshProfiles()
    {
        ProfilesList.Children.Clear();
        ProfilesEmpty.Visibility = _settings.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProfilesHint.Text = L.T("Quando um programa da lista abrir (ex.: o emulador), a velocidade troca sozinha e volta à padrão, ", "When a program on the list opens (e.g. the emulator), the speed switches by itself and goes back to the default, ") +
                            L.T($"{SpeedLabel(_settings.PointerSpeed)}, quando ele fechar.", $"{SpeedLabel(_settings.PointerSpeed)}, when it closes.");

        foreach (var profile in _settings.Profiles)
        {
            bool isActive = _monitor.ActiveProfile == profile;
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = profile.DisplayName, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
            var sub = new TextBlock { Text = isActive ? L.T($"{profile.ProcessName}.exe · ativo agora", $"{profile.ProcessName}.exe · active now") : $"{profile.ProcessName}.exe", FontSize = 12 };
            sub.SetResourceReference(ForegroundProperty, isActive ? "MfAccent" : "MfTextMuted");
            name.Children.Add(sub);
            var speed = new TextBlock
            {
                Text = SpeedLabel(profile.Speed),
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 4, 0),
            };
            var remove = new Button { Icon = new SymbolIcon(SymbolRegular.Delete24), Appearance = ControlAppearance.Transparent, ToolTip = L.T("Remover perfil", "Remove profile") };
            remove.Click += (_, _) => Run(() =>
            {
                _settings.Profiles.Remove(profile);
                _settings.Save(AppSettings.DefaultPath);
                Log.Info($"Perfil removido: {profile.DisplayName}");
                _monitor.CheckNow();
            });

            Grid.SetColumn(speed, 1);
            Grid.SetColumn(remove, 2);
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
                Record(ChangeSource.User, L.T("Configurações do MouseFix aplicadas", "MouseFix settings applied"), _service.ApplyTarget());
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
            UpdateTitle.Text = L.T($"MouseFix {available!.ToString(3)} disponível (você tem a {CurrentVersion.ToString(3)})", $"MouseFix {available!.ToString(3)} is available (you have {CurrentVersion.ToString(3)})");
    }

    /// <summary>Raised when an update was downloaded, verified and handed over; the app must exit for it to finish.</summary>
    public event EventHandler? UpdateHandedOver;

    private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateNowButton.IsEnabled = false;
        UpdatePageButton.Visibility = Visibility.Collapsed;
        UpdateStatus.Text = L.T("Procurando a versão nova…", "Looking for the new version…");
        try
        {
            // Ask GitHub again: the download links aren't kept between checks.
            var update = await UpdateChecker.CheckAsync(CurrentVersion);
            if (update is null)
            {
                UpdateStatus.Text = L.T("Você já tem a versão mais recente.", "You already have the latest version.");
                UpdateCard.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateProgress.Visibility = Visibility.Visible;
            var progress = new Progress<double>(p =>
            {
                UpdateProgress.Value = p;
                UpdateStatus.Text = L.T($"Baixando a versão {update.Version.ToString(3)}… {p:P0}", $"Downloading version {update.Version.ToString(3)}… {p:P0}");
            });
            await SelfUpdater.StartUpdateAsync(update, progress);

            UpdateStatus.Text = SelfUpdater.IsInstalled ? L.T("Instalando… o MouseFix vai reabrir sozinho.", "Installing… MouseFix will reopen by itself.") : L.T("Reiniciando na versão nova…", "Restarting in the new version…");
            _history.Add(new ChangeEntry(DateTime.Now, ChangeSource.User, L.T($"Atualização para a versão {update.Version.ToString(3)} baixada e verificada", $"Update to version {update.Version.ToString(3)} downloaded and verified"), true));
            _history.Flush();
            UpdateHandedOver?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao atualizar dentro do app", ex);
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateStatus.Text = ex is System.Net.Http.HttpRequestException or TaskCanceledException
                ? L.T("Não foi possível baixar (sem internet ou o GitHub não respondeu). Tente de novo ou abra a página.", "Could not download (no internet, or GitHub didn't answer). Try again or open the page.")
                : L.T($"Não foi possível atualizar: {ex.Message}", $"Could not update: {ex.Message}");
            UpdatePageButton.Visibility = Visibility.Visible;
            UpdateNowButton.IsEnabled = true;
        }
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
        bool othersFound = RefreshOtherEmulators();

        EmulatorCard.Visibility = profile?.SelectedAim is null ? Visibility.Collapsed : Visibility.Visible;
        EmulatorEmpty.Visibility = profile?.SelectedAim is null && !othersFound ? Visibility.Visible : Visibility.Collapsed;
        if (profile?.SelectedAim is not { } aim)
            return;

        EmulatorTitle.Text = $"{profile.EmulatorName} · {profile.GameName}";
        EmulatorAccelValue.Text = profile.AnyAcceleration ? L.T("Ligada", "On") : L.T("Desligada", "Off");
        EmulatorSchemeValue.Text = aim.SchemeName;
        EmulatorSensValue.Text = $"{aim.Sensitivity:0.##} / {aim.SensitivityRatioY:P0}";
        EmulatorFixButton.Visibility = profile.AnyAcceleration ? Visibility.Visible : Visibility.Collapsed;

        EmulatorTestButton.Content = _settings.EmulatorUsesWindowsCursor is null ? L.T("Testar emulador", "Test emulator") : L.T("Testar de novo", "Test again");
        EmulatorAdvice.Text = profile.AnyAcceleration
            ? L.T("O emulador está aplicando aceleração própria na mira. Feche o emulador e desligue abaixo.", "The emulator is adding its own acceleration to the aim. Close the emulator and turn it off below.")
            : EmulatorAdviceFor(profile.GameName, aim);

        var suggested = SuggestedSensitivity(aim);
        bool needsAdjust = suggested is double s && Math.Abs(s - aim.Sensitivity) >= 0.01;
        EmulatorSensButton.Visibility = needsAdjust ? Visibility.Visible : Visibility.Collapsed;
        EmulatorSensButton.Content = L.T($"Aplicar sensibilidade {suggested:0.00}", $"Apply sensitivity {suggested:0.00}");
    }

    // LDPlayer, MuMu or GameLoop: MouseFix handles Windows for them, but can't read their own mapping files.
    private bool RefreshOtherEmulators()
    {
        var others = OtherEmulators.FindInstalled();
        EmulatorOthers.Visibility = others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (others.Count == 0)
            return false;

        EmulatorOthersTitle.Text = string.Join(", ", others);
        string cursor = _settings.EmulatorUsesWindowsCursor switch
        {
            true => L.T(" Pelo teste, a mira segue o cursor do Windows: a velocidade do MouseFix chega até o jogo.", " By the test, the aim follows the Windows cursor: MouseFix's speed reaches the game."),
            false => L.T(" Pelo teste, a mira lê o sensor direto: ajuste pela sensibilidade do emulador ou pelo DPI do mouse.", " By the test, the aim reads the sensor directly: adjust with the emulator's sensitivity or the mouse DPI."),
            null => L.T(" Faça o teste para saber se a velocidade do Windows muda a mira no jogo.", " Run the test to find out whether the Windows speed changes the aim in the game."),
        };
        EmulatorOthersText.Text =
            L.T("O MouseFix já cuida do Windows para ele: aceleração desligada, velocidade, perfis, modo jogo e prioridade. ", "MouseFix already takes care of Windows for it: acceleration off, speed, profiles, game mode and priority. ") +
            L.T("A aceleração e a sensibilidade de dentro do emulador ficam no mapeamento de teclas dele, que o MouseFix ainda não lê.", "The acceleration and sensitivity inside the emulator are in its key mapping, which MouseFix can't read yet.") + cursor;
        EmulatorOthersTestButton.Content = _settings.EmulatorUsesWindowsCursor is null ? L.T("Testar emulador", "Test emulator") : L.T("Testar de novo", "Test again");
        return true;
    }

    private string EmulatorAdviceFor(string game, EmulatorAim aim) => _settings.EmulatorUsesWindowsCursor switch
    {
        null => L.T($"Faça o teste do emulador para saber se as configurações do Windows afetam a mira no {game}.", $"Run the emulator test to find out whether the Windows settings affect the aim in {game}."),
        false => L.T($"Teste feito: o emulador lê o sensor do mouse direto. A velocidade do Windows não muda a mira no {game}; ", $"Tested: the emulator reads the mouse sensor directly. The Windows speed doesn't change the aim in {game}; ") +
                 L.T("ajuste pela sensibilidade do emulador ou pelo DPI do mouse.", "adjust with the emulator's sensitivity or the mouse DPI."),
        true => SuggestedSensitivity(aim) is double s
            ? Math.Abs(s - aim.Sensitivity) < 0.01
                ? L.T($"Teste feito: a mira do {game} segue o cursor do Windows. Sensibilidade já ajustada para a velocidade atual.", $"Tested: the aim in {game} follows the Windows cursor. Sensitivity already adjusted for the current speed.")
                : L.T($"Teste feito: a mira do {game} segue o cursor do Windows. Antes do MouseFix a velocidade era ", $"Tested: the aim in {game} follows the Windows cursor. Before MouseFix the speed was ") +
                  L.T($"{_service.Original!.Speed}/20 e agora é {EmulatorSpeed}/20, por isso a mira mudou. ", $"{_service.Original!.Speed}/20 and now it is {EmulatorSpeed}/20, which is why the aim changed. ") +
                  L.T($"Com sensibilidade X {s:0.00} a sensação volta a ser a de antes.", $"With X sensitivity {s:0.00} it feels like before again.")
            : L.T($"Teste feito: a mira do {game} segue o cursor do Windows, então as configurações do MouseFix chegam até o jogo.", $"Tested: the aim in {game} follows the Windows cursor, so MouseFix's settings reach the game."),
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
        string file = BlueStacksProfiles.EnsureUserCopy(_emulatorProfile.FilePath);
        BlueStacksProfiles.SetSelectedSensitivity(file, suggested);
        var after = BlueStacksProfiles.Load(_emulatorProfile.EmulatorName, _emulatorProfile.GameName, file).SelectedAim;
        Record(ChangeSource.User, L.T($"Sensibilidade do emulador: {aim.Sensitivity:0.##} → {suggested:0.##}", $"Emulator sensitivity: {aim.Sensitivity:0.##} → {suggested:0.##}"),
            after is not null && Math.Abs(after.Sensitivity - suggested) < 0.005 ? null : L.T("o arquivo do emulador não ficou com o valor novo", "the emulator's file didn't keep the new value"));
    });

    private bool EnsureEmulatorClosed()
    {
        if (!BlueStacksProfiles.IsEmulatorRunning)
            return true;
        MessageBox.Show(this,
            L.T($"Feche o {_emulatorProfile?.EmulatorName ?? "emulador"} antes. Ele regrava esse arquivo e desfaria a mudança.", $"Close {_emulatorProfile?.EmulatorName ?? "the emulator"} first. It rewrites that file and would undo the change."),
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
        if (!EnsureEmulatorClosed())
            return;
        var (summary, problem) = DisableEmulatorAcceleration();
        if (summary is not null)
            Record(ChangeSource.User, summary, problem);
    });

    // ---------- Tests and settings ----------

    private void TestButton_Click(object sender, RoutedEventArgs e) =>
        new PrecisionTestWindow { Owner = this }.ShowDialog();

    private void DistanceTestButton_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var window = new DistanceTestWindow { Owner = this };
        window.ShowDialog();
        if (window.MeasuredDpi is not double dpi)
            return;
        _settings.MeasuredDpi = Math.Round(dpi);
        _settings.MeasuredDpiDate = DateTime.Now;
        _settings.Save(AppSettings.DefaultPath);
        Log.Info($"DPI medido no teste de distância: {_settings.MeasuredDpi:0}");
    });

    private void SensitivityButton_Click(object sender, RoutedEventArgs e) =>
        new SensitivityWindow(_settings) { Owner = this }.ShowDialog();

    private void AutoReapplyToggle_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        _settings.AutoReapply = AutoReapplyToggle.IsChecked == true;
        _settings.Save(AppSettings.DefaultPath);
    });

    /// <summary>Raised when MouseFix must reopen (to show the chosen language).</summary>
    public event EventHandler? RestartRequested;

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;
        string? language = tag.Length == 0 ? null : tag;
        if (language == _settings.Language)
            return;
        _settings.Language = language;
        _settings.Save(AppSettings.DefaultPath);
        Log.Info($"Idioma: {language ?? "automático"}");
        _history.Flush();
        RestartRequested?.Invoke(this, EventArgs.Empty);
    }

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
            Log.Error("Erro ao aplicar", ex);
            MessageBox.Show(this, ex.Message, L.T("Erro", "Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        AutoStartToggle.IsChecked = AutoStart.IsEnabled;
        Refresh();
    }
}
