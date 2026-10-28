using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using MouseFix.Core;
using Wpf.Ui.Controls;

namespace MouseFix.App;

/// <summary>
/// The user drags the mouse over a known physical distance; each drag (left button down to up) is one stroke.
/// Counts come straight from Raw Input, so this sees what the mouse and its driver send, before Windows.
/// </summary>
public partial class DistanceTestWindow : FluentWindow
{
    private readonly DistanceTestAnalyzer _analyzer = new(10);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HwndSourceHook _hook;

    private bool _inStroke;
    private int _dx, _dy, _packets;
    private double _firstMove, _lastMove;

    public DistanceTestWindow()
    {
        InitializeComponent();
        _hook = WndProc;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd).AddHook(_hook);
            RawMouse.Register(hwnd);
        };
        Closed += (_, _) => RawMouse.Unregister();
        UpdateResults();
    }

    /// <summary>The DPI once there are enough drags to trust it (one drag can be off by a few millimetres).</summary>
    public double? MeasuredDpi => _analyzer.Strokes.Count >= DistanceTestAnalyzer.MinStrokesPerGroup ? _analyzer.Dpi : null;

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == RawMouse.WM_INPUT && RawMouse.TryRead(lParam, out var packet))
            OnPacket(packet);
        return 0;
    }

    private void OnPacket(RawMousePacket packet)
    {
        // Only drags that start on the test area count, so clicking buttons doesn't create strokes.
        if ((packet.ButtonFlags & RawMousePacket.LeftDown) != 0 && TestArea.IsMouseOver)
        {
            _inStroke = true;
            _dx = _dy = _packets = 0;
        }

        if (_inStroke && (packet.Dx != 0 || packet.Dy != 0))
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            if (_packets == 0)
                _firstMove = now;
            _lastMove = now;
            _dx += packet.Dx;
            _dy += packet.Dy;
            _packets++;
        }

        if ((packet.ButtonFlags & RawMousePacket.LeftUp) != 0 && _inStroke)
        {
            _inStroke = false;
            // Time between the first and last movement, so holding still before or after doesn't count.
            // The first packet opens the interval, so it isn't counted for the rate.
            _analyzer.Add(_dx, _dy, _lastMove - _firstMove, Math.Max(0, _packets - 1));
            UpdateResults();
        }
    }

    private void UpdateResults()
    {
        var strokes = _analyzer.Strokes;
        StrokesText.Text = strokes.Count == 0
            ? "Nenhuma arrastada ainda."
            : string.Join("\n", strokes.Select((s, i) =>
                $"#{i + 1,-3} {s.Counts,7:0} pontos   {s.DurationMs / 1000,5:0.00} s   {Speed(s),5:0} cm/s"));

        DpiValue.Text = _analyzer.Dpi is double dpi ? $"{dpi:0} DPI" : "—";
        PollingValue.Text = _analyzer.PollingHz is double hz ? $"~{RoundPolling(hz)} Hz" : "—";
        AreaHint.Text = strokes.Count == 0 ? "Segure o botão esquerdo aqui e arraste" : $"{strokes.Count} arrastada(s) registrada(s)";

        double diff = ((_analyzer.FastToSlowRatio ?? 1) - 1) * 100;
        (SymbolRegular icon, string brush, string title, string subtitle) = _analyzer.Verdict switch
        {
            DistanceVerdict.Consistent => (SymbolRegular.CheckmarkCircle24, "MfSuccess",
                "Sem aceleração no mouse ou no driver",
                $"Rápido e devagar deram a mesma contagem (diferença de {diff:+0;-0;0}%, dentro do erro de posicionar a mão)."),
            DistanceVerdict.Accelerated => (SymbolRegular.DismissCircle24, "MfDanger",
                $"Aceleração antes do Windows: rápido conta {diff:0}% a mais",
                "Procure opções de aceleração ou \"precisão aprimorada\" no software do mouse (ex.: Razer Synapse) e desligue."),
            DistanceVerdict.LosingCounts => (SymbolRegular.Warning24, "MfWarning",
                $"Movimentos rápidos contaram {-diff:0}% a menos",
                "Confira se as arrastadas rápidas foram até a marca. Se foram, o sensor pode estar perdendo rastreamento: limpe o sensor e o mousepad."),
            _ => (SymbolRegular.Ruler24, "MfTextMuted",
                "Coletando arrastadas…",
                $"Faça pelo menos {DistanceTestAnalyzer.MinStrokesPerGroup} bem devagar e {DistanceTestAnalyzer.MinStrokesPerGroup} bem rápidas (as rápidas precisam ser bem mais rápidas que as lentas)."),
        };
        VerdictIcon.Symbol = icon;
        VerdictIcon.SetResourceReference(ForegroundProperty, brush);
        VerdictText.Text = title;
        VerdictSubtext.Text = subtitle;
    }

    private double Speed(Stroke s) => s.DurationMs > 0 ? _analyzer.DistanceCm / (s.DurationMs / 1000) : 0;

    // Mice poll at standard rates; the measured value is a little under the true one.
    private static int RoundPolling(double hz) =>
        new[] { 125, 250, 500, 1000, 2000, 4000, 8000 }.OrderBy(r => Math.Abs(r - hz)).First() is var std && Math.Abs(std - hz) / std < 0.15
            ? std
            : (int)Math.Round(hz);

    private void DistanceBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var text = DistanceBox.Text.Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double cm) && cm is > 0 and <= 100)
        {
            _analyzer.DistanceCm = cm;
            if (IsLoaded)
                UpdateResults();
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _analyzer.Reset();
        _inStroke = false;
        UpdateResults();
    }
}
