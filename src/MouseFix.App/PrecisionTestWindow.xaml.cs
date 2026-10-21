using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MouseFix.Core;
using Wpf.Ui.Controls;

namespace MouseFix.App;

/// <summary>
/// Reads the mouse sensor directly (Raw Input) and compares it with how far the cursor moved.
/// Movement is grouped in short windows; each window becomes one sample for the analyzer.
/// </summary>
public partial class PrecisionTestWindow : FluentWindow
{
    // A sample covers ~40 ms of movement; a pause longer than 100 ms starts a fresh sample.
    private const double SampleWindowMs = 40;
    private const double PauseMs = 100;
    private const int ScreenEdgeMargin = 2;

    private readonly AccelerationAnalyzer _analyzer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<(System.Windows.Controls.TextBlock Samples, System.Windows.Controls.TextBlock Ratio)> _rows = [];
    private readonly HwndSourceHook _hook;

    private int _rawDx, _rawDy;
    private long _rawPath;
    private POINT _startCursor;
    private double _windowStart, _lastInput = double.NegativeInfinity;
    private bool _needsStart = true;
    private double _lastUiUpdate;

    public PrecisionTestWindow()
    {
        InitializeComponent();
        _hook = WndProc;
        BuildResultRows();
        UpdateResults();
        SourceInitialized += (_, _) => RegisterRawMouse();
        Closed += (_, _) => RawMouse.Unregister();
    }

    private void BuildResultRows()
    {
        var buckets = _analyzer.Buckets;
        for (int i = 0; i < buckets.Count; i++)
        {
            var name = new Wpf.Ui.Controls.TextBlock { Text = buckets[i].Name, Margin = new Thickness(0, 8, 0, 0) };
            var samples = new Wpf.Ui.Controls.TextBlock { TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ratio = new Wpf.Ui.Controls.TextBlock
            {
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
                FontTypography = FontTypography.BodyStrong,
            };
            foreach (var (cell, column) in new[] { (name, 0), (samples, 1), (ratio, 2) })
            {
                System.Windows.Controls.Grid.SetRow(cell, i + 1);
                System.Windows.Controls.Grid.SetColumn(cell, column);
                ResultsGrid.Children.Add(cell);
            }
            _rows.Add((samples, ratio));
        }
    }

    private void RegisterRawMouse()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd).AddHook(_hook);
        RawMouse.Register(hwnd);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == RawMouse.WM_INPUT && RawMouse.TryRead(lParam, out var packet) && (packet.Dx != 0 || packet.Dy != 0))
            OnRawMove(packet.Dx, packet.Dy);
        return 0;
    }

    private void OnRawMove(int dx, int dy)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        if (now - _lastInput > PauseMs)
            _needsStart = true;
        _lastInput = now;

        if (!_needsStart)
        {
            _rawDx += dx;
            _rawDy += dy;
            _rawPath += Math.Abs(dx) + Math.Abs(dy);
            if (now - _windowStart < SampleWindowMs)
                return;
        }

        // The cursor moves as soon as input arrives, but WM_INPUT is handled later. While more raw input
        // is still queued, the cursor is ahead of what was counted, so wait until the queue drains.
        if (RawMouse.IsMoreQueued)
            return;
        GetPhysicalCursorPos(out var cursor);

        if (_needsStart)
        {
            // The cursor already includes every packet so far, so it becomes the starting point.
            StartWindow(now, cursor);
            _needsStart = false;
            return;
        }

        // At a screen edge the cursor stops while the sensor keeps counting, so those samples are invalid.
        if (!IsNearScreenEdge(cursor) && !IsNearScreenEdge(_startCursor))
            _analyzer.AddSample(_rawDx, _rawDy, _rawPath, cursor.X - _startCursor.X, cursor.Y - _startCursor.Y, now - _windowStart);

        StartWindow(now, cursor);

        if (now - _lastUiUpdate > 150)
        {
            _lastUiUpdate = now;
            UpdateResults();
        }
    }

    private void StartWindow(double now, POINT cursor)
    {
        _windowStart = now;
        _startCursor = cursor;
        _rawDx = _rawDy = 0;
        _rawPath = 0;
    }

    private void UpdateResults()
    {
        var buckets = _analyzer.Buckets;
        for (int i = 0; i < buckets.Count; i++)
        {
            _rows[i].Samples.Text = buckets[i].Samples.ToString();
            _rows[i].Ratio.Text = buckets[i].MedianRatio is double r ? $"{r:0.00}" : "—";
        }

        AreaHint.Visibility = buckets.Sum(b => b.Samples) > 0 ? Visibility.Collapsed : Visibility.Visible;

        (SymbolRegular icon, string brush, string title, string subtitle) = _analyzer.Verdict switch
        {
            AccelerationVerdict.OneToOne => (SymbolRegular.CheckmarkCircle24, "SystemFillColorSuccessBrush",
                "1:1 confirmado: sem aceleração",
                "Cada ponto lido pelo sensor move o cursor exatamente 1 pixel, em qualquer velocidade."),
            AccelerationVerdict.Scaled => (SymbolRegular.Warning24, "SystemFillColorCautionBrush",
                $"Sem aceleração, mas o cursor anda {_analyzer.OverallRatio:0.00}x",
                "A relação é constante, então não há aceleração. Não é 1:1 porque a velocidade não está em 10/20, o que é normal se você escolheu outra velocidade."),
            AccelerationVerdict.Accelerated => (SymbolRegular.DismissCircle24, "SystemFillColorCriticalBrush",
                $"Aceleração detectada: {_analyzer.Spread:0.0}x mais rápido em movimentos rápidos",
                "O cursor anda mais por ponto quando a mão vai rápido. Verifique o MouseFix e o software do fabricante do mouse."),
            _ => (SymbolRegular.Gauge24, "TextFillColorSecondaryBrush",
                "Coletando dados…",
                $"Faça movimentos lentos e rápidos até ter pelo menos {AccelerationAnalyzer.MinSamplesPerBucket} amostras em uma linha lenta e em uma rápida."),
        };

        VerdictIcon.Symbol = icon;
        VerdictIcon.SetResourceReference(ForegroundProperty, brush);
        VerdictText.Text = title;
        VerdictSubtext.Text = subtitle;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _analyzer.Reset();
        _needsStart = true;
        UpdateResults();
    }

    private static bool IsNearScreenEdge(POINT p)
    {
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN), top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int right = left + GetSystemMetrics(SM_CXVIRTUALSCREEN) - 1, bottom = top + GetSystemMetrics(SM_CYVIRTUALSCREEN) - 1;
        return p.X <= left + ScreenEdgeMargin || p.X >= right - ScreenEdgeMargin
            || p.Y <= top + ScreenEdgeMargin || p.Y >= bottom - ScreenEdgeMargin;
    }

    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetPhysicalCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
