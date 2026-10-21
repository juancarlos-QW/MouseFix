using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MouseFix.App;

/// <summary>
/// Grid area that draws the cursor's recent path as a fading blue trail, so the user can feel the
/// movement right away. Purely visual; the precision test is what actually measures acceleration.
/// </summary>
public sealed class MovementTrail : FrameworkElement
{
    private const double GridStep = 28;
    private static readonly TimeSpan TrailLife = TimeSpan.FromMilliseconds(650);

    private readonly List<(Point Point, DateTime Time)> _points = [];
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(MovementTrail),
        new FrameworkPropertyMetadata(Color.FromRgb(0x1A, 0x7C, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(MovementTrail),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public MovementTrail()
    {
        ClipToBounds = true;
        _timer.Tick += (_, _) =>
        {
            _points.RemoveAll(p => DateTime.Now - p.Time > TrailLife);
            InvalidateVisual();
            if (_points.Count == 0)
                _timer.Stop();
        };
    }

    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _points.Add((e.GetPosition(this), DateTime.Now));
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Transparent fill so the whole area receives mouse moves, not just the drawn lines.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var gridPen = new Pen(GridBrush, 1);
        gridPen.Freeze();
        for (double x = GridStep; x < ActualWidth; x += GridStep)
            dc.DrawLine(gridPen, new Point(x + 0.5, 0), new Point(x + 0.5, ActualHeight));
        for (double y = GridStep; y < ActualHeight; y += GridStep)
            dc.DrawLine(gridPen, new Point(0, y + 0.5), new Point(ActualWidth, y + 0.5));

        var now = DateTime.Now;
        for (int i = 1; i < _points.Count; i++)
        {
            double life = 1 - (now - _points[i].Time).TotalMilliseconds / TrailLife.TotalMilliseconds;
            if (life <= 0)
                continue;
            var from = _points[i - 1].Point;
            var to = _points[i].Point;
            // Soft wide glow under a thinner bright line; both fade out with age.
            dc.DrawLine(RoundPen(Color.FromArgb((byte)(life * 50), Accent.R, Accent.G, Accent.B), 12 * life), from, to);
            dc.DrawLine(RoundPen(Color.FromArgb((byte)(life * 255), Accent.R, Accent.G, Accent.B), 1 + 3 * life), from, to);
        }

        if (_points.Count > 0 && now - _points[^1].Time < TrailLife)
        {
            var head = _points[^1].Point;
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, Accent.R, Accent.G, Accent.B)), null, head, 10, 10);
            dc.DrawEllipse(new SolidColorBrush(Accent), null, head, 3.5, 3.5);
        }
    }

    private static Pen RoundPen(Color color, double thickness) =>
        new(new SolidColorBrush(color), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
}
