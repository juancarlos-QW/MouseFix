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
            ? $"Medido no teste de distância em {date:dd/MM/yyyy}."
            : "Use o DPI configurado no software do mouse (ex.: Razer Synapse) ou faça o teste de distância para medir o real.";
        SpeedSlider.Value = settings.PointerSpeed;
        CrossLabel.Text = $"cm para atravessar a tela ({_screenWidth} px)";
        EmulatorNote.Text = settings.EmulatorUsesWindowsCursor switch
        {
            true => "Pelo teste do emulador, a mira segue o cursor do Windows: esses números também valem no jogo (junto com a sensibilidade do próprio jogo).",
            false => "Pelo teste do emulador, a mira lê o sensor direto: no jogo vale só o DPI e a sensibilidade do jogo, não a velocidade do Windows.",
            null => "Faça o teste do emulador para saber se a velocidade do Windows também muda a mira no jogo.",
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
            NewDpiResult.Text = "Digite o DPI do mouse acima.";
            OneToOneTip.Text = "";
            return;
        }

        EdpiValue.Text = SensitivityCalculator.EffectiveDpi(dpi, speed).ToString("0", CultureInfo.CurrentCulture);
        PixelsPerCmValue.Text = SensitivityCalculator.PixelsPerCm(dpi, speed).ToString("0.0", CultureInfo.CurrentCulture);
        CrossValue.Text = SensitivityCalculator.CmForPixels(_screenWidth, dpi, speed).ToString("0.0", CultureInfo.CurrentCulture) + " cm";

        double oneToOneDpi = SensitivityCalculator.DpiForOneToOne(dpi, speed);
        OneToOneTip.Text = speed == MouseSettings.OneToOneSpeed
            ? "Você já está em 1:1 (velocidade 10), o jeito mais preciso."
            : $"Para ficar 1:1 exato com a mesma sensação: coloque {oneToOneDpi:0} DPI no mouse e a velocidade 10 no Windows.";

        if (ParseDpi(NewDpiBox.Text) is not double newDpi)
        {
            NewDpiResult.Text = "Digite o DPI que você quer usar para ver qual velocidade do Windows mantém a mesma sensação.";
            return;
        }

        var suggestion = SensitivityCalculator.SpeedForNewDpi(dpi, speed, newDpi);
        string speedText = suggestion.Speed == MouseSettings.OneToOneSpeed ? "10/20 (1:1)" : $"{suggestion.Speed}/20";
        NewDpiResult.Text = suggestion.IsExact
            ? $"Com {newDpi:0} DPI, use a velocidade {speedText} no Windows: fica igual ao que você usa hoje."
            : $"Com {newDpi:0} DPI, a mais próxima é a velocidade {speedText}, que fica {Math.Abs(suggestion.FeelRatio - 1) * 100:0}% "
              + $"{(suggestion.FeelRatio > 1 ? "mais rápida" : "mais lenta")}. Nenhuma velocidade do Windows dá exatamente igual.";
    }

    private static double? ParseDpi(string text) =>
        double.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out double dpi) && dpi is >= 50 and <= 50000 ? dpi : null;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
