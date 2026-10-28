using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using MouseFix.Core;
using Wpf.Ui.Controls;

namespace MouseFix.App;

/// <summary>eDPI, pixels per cm and cm to cross the screen, plus which Windows speed keeps the feel with a new DPI.</summary>
public partial class SensitivityWindow : FluentWindow
{
    private const int SM_CXSCREEN = 0;
    private const double DefaultDpi = 800;

    private readonly int _screenWidth = GetSystemMetrics(SM_CXSCREEN);
    private readonly bool _ready;

    public SensitivityWindow(AppSettings settings)
    {
        InitializeComponent();

        DpiBox.Text = (settings.MeasuredDpi ?? DefaultDpi).ToString("0", CultureInfo.CurrentCulture);
        DpiHint.Text = settings.MeasuredDpi is not null && settings.MeasuredDpiDate is DateTime date
            ? L.T($"Medido no teste de distância em {date:dd/MM/yyyy}.", $"Measured in the distance test on {date:d}.")
            : L.T("Use o DPI configurado no software do mouse (ex.: Razer Synapse) ou faça o teste de distância para medir o real.", "Use the DPI set in the mouse software (e.g. Razer Synapse), or run the distance test to measure the real one.");
        SpeedSlider.Value = settings.PointerSpeed;
        CrossLabel.Text = L.T($"cm para atravessar a tela ({_screenWidth} px)", $"cm to cross the screen ({_screenWidth} px)");
        EmulatorNote.Text = settings.EmulatorUsesWindowsCursor switch
        {
            true => L.T("Pelo teste do emulador, a mira segue o cursor do Windows: esses números também valem no jogo (junto com a sensibilidade do próprio jogo).", "By the emulator test, the aim follows the Windows cursor: these numbers apply in the game too (together with the game's own sensitivity)."),
            false => L.T("Pelo teste do emulador, a mira lê o sensor direto: no jogo vale só o DPI e a sensibilidade do jogo, não a velocidade do Windows.", "By the emulator test, the aim reads the sensor directly: in the game only the DPI and the game's sensitivity count, not the Windows speed."),
            null => L.T("Faça o teste do emulador para saber se a velocidade do Windows também muda a mira no jogo.", "Run the emulator test to find out whether the Windows speed also changes the aim in the game."),
        };

        _ready = true;
        Update();
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Update();

    private void Input_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => Update();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Update()
    {
        if (!_ready)
            return;

        int speed = (int)SpeedSlider.Value;
        SpeedLabel.Text = speed == MouseSettings.OneToOneSpeed
            ? "10/20 (1:1)"
            : $"{speed}/20 ({MouseSettings.SpeedMultiplier(speed):0.###}x)";

        if (ParseDpi(DpiBox.Text) is not double dpi)
        {
            EdpiValue.Text = PixelsPerCmValue.Text = CrossValue.Text = "—";
            NewDpiResult.Text = L.T("Digite o DPI do mouse acima.", "Type the mouse DPI above.");
            OneToOneTip.Text = "";
            return;
        }

        EdpiValue.Text = SensitivityCalculator.EffectiveDpi(dpi, speed).ToString("0", CultureInfo.CurrentCulture);
        PixelsPerCmValue.Text = SensitivityCalculator.PixelsPerCm(dpi, speed).ToString("0.0", CultureInfo.CurrentCulture);
        CrossValue.Text = SensitivityCalculator.CmForPixels(_screenWidth, dpi, speed).ToString("0.0", CultureInfo.CurrentCulture) + " cm";

        double oneToOneDpi = SensitivityCalculator.DpiForOneToOne(dpi, speed);
        OneToOneTip.Text = speed == MouseSettings.OneToOneSpeed
            ? L.T("Você já está em 1:1 (velocidade 10), o jeito mais preciso.", "You are already at 1:1 (speed 10), the most precise way.")
            : L.T($"Para ficar 1:1 exato com a mesma sensação: coloque {oneToOneDpi:0} DPI no mouse e a velocidade 10 no Windows.", $"For exact 1:1 with the same feel: set {oneToOneDpi:0} DPI in the mouse and speed 10 in Windows.");

        if (ParseDpi(NewDpiBox.Text) is not double newDpi)
        {
            NewDpiResult.Text = L.T("Digite o DPI que você quer usar para ver qual velocidade do Windows mantém a mesma sensação.", "Type the DPI you want to use to see which Windows speed keeps the same feel.");
            return;
        }

        var suggestion = SensitivityCalculator.SpeedForNewDpi(dpi, speed, newDpi);
        string speedText = suggestion.Speed == MouseSettings.OneToOneSpeed ? "10/20 (1:1)" : $"{suggestion.Speed}/20";
        NewDpiResult.Text = suggestion.IsExact
            ? L.T($"Com {newDpi:0} DPI, use a velocidade {speedText} no Windows: fica igual ao que você usa hoje.", $"With {newDpi:0} DPI, use speed {speedText} in Windows: it feels the same as what you use today.")
            : L.T($"Com {newDpi:0} DPI, a mais próxima é a velocidade {speedText}, que fica {Math.Abs(suggestion.FeelRatio - 1) * 100:0}% ", $"With {newDpi:0} DPI, the closest is speed {speedText}, which is {Math.Abs(suggestion.FeelRatio - 1) * 100:0}% ")
              + L.T($"{(suggestion.FeelRatio > 1 ? "mais rápida" : "mais lenta")}. Nenhuma velocidade do Windows dá exatamente igual.", $"{(suggestion.FeelRatio > 1 ? "faster" : "slower")}. No Windows speed gives exactly the same.");
    }

    private static double? ParseDpi(string text) =>
        double.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out double dpi) && dpi is >= 50 and <= 50000 ? dpi : null;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
