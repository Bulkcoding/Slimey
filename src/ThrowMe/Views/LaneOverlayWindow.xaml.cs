using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ThrowMe.Services;
using Canvas = System.Windows.Controls.Canvas;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using ColorConverter = System.Windows.Media.ColorConverter;
using Polygon = System.Windows.Shapes.Polygon;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace ThrowMe.Views;

/// <summary>
/// 볼링판(레인) 오버레이 — 주 모니터 작업영역을 덮는 클릭 통과 창.
/// 나무 레인 + 거터 + 파울 라인 + 조준 화살표 + 핀덱을 그린다.
/// </summary>
public partial class LaneOverlayWindow : Window
{
    private readonly MonitorLayoutService _monitors;
    private double _scaleX = 1, _scaleY = 1;
    private Rect _wa;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);

    public LaneOverlayWindow(MonitorLayoutService monitors)
    {
        _monitors = monitors;
        InitializeComponent();
    }
    // 창을 띄우지 않는 개발용 렌더에서 실제 레인 그리기 코드를 그대로 검증한다.
    internal Canvas PreviewRoot => Root;
    internal void PreparePreview(Rect bounds)
    {
        _wa = bounds;
        _scaleX = _scaleY = 1;
        Root.Width = bounds.Width;
        Root.Height = bounds.Height;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
        {
            Matrix m = src.CompositionTarget.TransformToDevice;
            _scaleX = m.M11 > 0 ? m.M11 : 1; _scaleY = m.M22 > 0 ? m.M22 : 1;
        }
        _wa = _monitors.PrimaryWorkingArea;
        Left = _wa.Left / _scaleX; Top = _wa.Top / _scaleY;
        Width = _wa.Width / _scaleX; Height = _wa.Height / _scaleY;
    }

    // 물리 px → 창 로컬 DIP
    private double LX(double px) => (px - _wa.Left) / _scaleX;
    private double LY(double py) => (py - _wa.Top) / _scaleY;
    private double LW(double w) => w / _scaleX;
    private double LH(double h) => h / _scaleY;

    // 원근 지오메트리(물리 px). 위(_topY)=핀쪽(좁음), 아래(_botY)=투구쪽(넓음).
    private double _centerX, _topY, _botY;
    private double _laneHalfTop, _laneHalfBot, _alleyHalfTop, _alleyHalfBot;

    private double HalfAt(double top, double bot, double y)
    {
        double t = (_botY - _topY) <= 0 ? 0 : (y - _topY) / (_botY - _topY);
        t = Math.Clamp(t, 0, 1);
        return top + (bot - top) * t;
    }
    private double LaneXp(double y, int side) => _centerX + side * HalfAt(_laneHalfTop, _laneHalfBot, y);
    private double AlleyXp(double y, int side) => _centerX + side * HalfAt(_alleyHalfTop, _alleyHalfBot, y);

    /// <summary>원근 레인 그리기. 좌표는 모두 물리 px.</summary>
    public void Setup(double centerX, double topY, double botY, double foulY,
                      double laneHalfTop, double laneHalfBot,
                      double alleyHalfTop, double alleyHalfBot,
                      double deckBotY, double arrowsY)
    {
        _centerX = centerX; _topY = topY; _botY = botY;
        _laneHalfTop = laneHalfTop; _laneHalfBot = laneHalfBot;
        _alleyHalfTop = alleyHalfTop; _alleyHalfBot = alleyHalfBot;
        Root.Children.Clear();

        // 거터: 바깥 립, 깊은 홈, 레인 쪽 경계의 3단 구조.
        var gutterBrush = VGrad(("#3D4652", 0), ("#111720", 0.40), ("#252D38", 1));
        Root.Children.Add(Poly(gutterBrush, 0.98,
            (AlleyXp(topY, -1), topY), (LaneXp(topY, -1), topY),
            (LaneXp(botY, -1), botY), (AlleyXp(botY, -1), botY)));
        Root.Children.Add(Poly(gutterBrush, 0.98,
            (LaneXp(topY, +1), topY), (AlleyXp(topY, +1), topY),
            (AlleyXp(botY, +1), botY), (LaneXp(botY, +1), botY)));

        double railTop = Math.Max(2.0, (_alleyHalfTop - _laneHalfTop) * 0.16);
        double railBot = Math.Max(5.0, (_alleyHalfBot - _laneHalfBot) * 0.16);
        var rail = VGrad(("#697584", 0), ("#343E4B", 0.55), ("#596573", 1));
        Root.Children.Add(Poly(rail, 0.95,
            (AlleyXp(topY, -1), topY), (AlleyXp(topY, -1) + railTop, topY),
            (AlleyXp(botY, -1) + railBot, botY), (AlleyXp(botY, -1), botY)));
        Root.Children.Add(Poly(rail, 0.95,
            (AlleyXp(topY, +1) - railTop, topY), (AlleyXp(topY, +1), topY),
            (AlleyXp(botY, +1), botY), (AlleyXp(botY, +1) - railBot, botY)));

        // 따뜻한 단풍나무 베이스. 아래로 갈수록 밝아져 원근이 자연스럽게 드러난다.
        var wood = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        wood.GradientStops.Add(new GradientStop(C("#9D6B35"), 0.0));
        wood.GradientStops.Add(new GradientStop(C("#C99755"), 0.18));
        wood.GradientStops.Add(new GradientStop(C("#E0B871"), 0.62));
        wood.GradientStops.Add(new GradientStop(C("#F0D39A"), 1.0));
        wood.Freeze();
        Root.Children.Add(Poly(wood, 0.99,
            (LaneXp(topY, -1), topY), (LaneXp(topY, +1), topY),
            (LaneXp(botY, +1), botY), (LaneXp(botY, -1), botY)));

        // 20개 판재를 미세하게 교차 착색하고 이음선을 소실점으로 수렴시킨다.
        const int boards = 20;
        var boardLight = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xF2, 0xCC)); boardLight.Freeze();
        var boardDark = new SolidColorBrush(Color.FromArgb(0x10, 0x61, 0x35, 0x13)); boardDark.Freeze();
        for (int i = 0; i < boards; i++)
        {
            double s0 = -1.0 + 2.0 * i / boards;
            double s1 = -1.0 + 2.0 * (i + 1) / boards;
            Root.Children.Add(Poly(i % 4 is 0 or 3 ? boardLight : boardDark, 1.0,
                (_centerX + s0 * _laneHalfTop, topY),
                (_centerX + s1 * _laneHalfTop, topY),
                (_centerX + s1 * _laneHalfBot, botY),
                (_centerX + s0 * _laneHalfBot, botY)));
        }
        var seam = new SolidColorBrush(Color.FromArgb(0x35, 0x6B, 0x42, 0x1E)); seam.Freeze();
        for (int i = 1; i < boards; i++)
        {
            double s = -1.0 + 2.0 * i / boards;
            Root.Children.Add(PlankLine(
                _centerX + s * _laneHalfTop, topY,
                _centerX + s * _laneHalfBot, botY, seam, 0.85));
        }

        // 핀덱과 뒤쪽 피트는 바닥과 명확히 분리해 핀이 떠 보이지 않게 한다.
        Root.Children.Add(Poly(VGrad(("#8A5B30", 0), ("#C48E4D", 0.40), ("#D8AF6C", 1)), 0.88,
            (LaneXp(topY, -1), topY), (LaneXp(topY, +1), topY),
            (LaneXp(deckBotY, +1), deckBotY), (LaneXp(deckBotY, -1), deckBotY)));
        double pitY = topY + Math.Min((deckBotY - topY) * 0.20, _laneHalfTop * 0.20);
        Root.Children.Add(Poly(VGrad(("#242A31", 0), ("#59616A", 1)), 0.92,
            (AlleyXp(topY, -1), topY), (AlleyXp(topY, +1), topY),
            (LaneXp(pitY, +1), pitY), (LaneXp(pitY, -1), pitY)));
        Root.Children.Add(PlankLine(LaneXp(deckBotY, -1), deckBotY, LaneXp(deckBotY, +1), deckBotY,
            new SolidColorBrush(Color.FromArgb(0x70, 0x6A, 0x42, 0x1C)), 2.0));

        // 레인 안쪽 립과 그림자를 마지막에 올려 거터의 깊이를 만든다.
        var lipLight = new SolidColorBrush(Color.FromArgb(0xA8, 0xD3, 0xC0, 0x91)); lipLight.Freeze();
        var lipShade = new SolidColorBrush(Color.FromArgb(0x82, 0x18, 0x1C, 0x23)); lipShade.Freeze();
        Root.Children.Add(PlankLine(LaneXp(topY, -1), topY, LaneXp(botY, -1), botY, lipShade, 4.2));
        Root.Children.Add(PlankLine(LaneXp(topY, +1), topY, LaneXp(botY, +1), botY, lipShade, 4.2));
        Root.Children.Add(PlankLine(LaneXp(topY, -1) + 1.3, topY, LaneXp(botY, -1) + 3.2, botY, lipLight, 1.0));
        Root.Children.Add(PlankLine(LaneXp(topY, +1) - 1.3, topY, LaneXp(botY, +1) - 3.2, botY, lipLight, 1.0));

        // 중앙 오일 패턴: 투구 방향으로 길게 이어지는 은은한 반사.
        double sheenTop = _laneHalfTop * 0.42;
        double sheenBot = _laneHalfBot * 0.34;
        var sheen = HGrad(("#00FFFFFF", 0), ("#34FFF8DD", 0.5), ("#00FFFFFF", 1));
        Root.Children.Add(Poly(sheen, 0.72,
            (_centerX - sheenTop, deckBotY), (_centerX + sheenTop, deckBotY),
            (_centerX + sheenBot, foulY), (_centerX - sheenBot, foulY)));

        // 7개 조준 다트.
        var arrowBrush = new SolidColorBrush(C("#6E3D18")); arrowBrush.Freeze();
        double aHalf = HalfAt(_laneHalfTop, _laneHalfBot, arrowsY);
        double aSize = aHalf * 0.072;
        for (int i = 0; i < 7; i++)
        {
            double t = (i - 3) / 3.0;
            double ax = _centerX + t * (aHalf * 0.72);
            double ay = arrowsY + Math.Abs(t) * (aHalf * 0.24);
            Root.Children.Add(Arrow(ax, ay, aSize, arrowBrush));
        }

        // 파울 라인 뒤의 어프로치 도트.
        double approachY = foulY + (botY - foulY) * 0.48;
        double approachHalf = HalfAt(_laneHalfTop, _laneHalfBot, approachY);
        for (int i = -2; i <= 2; i++)
            Root.Children.Add(Dot(_centerX + i * approachHalf * 0.31, approachY, Math.Max(2.2, approachHalf * 0.016), arrowBrush));

        // 짙은 청회색 파울 라인과 앞면 하이라이트.
        Root.Children.Add(PlankLine(LaneXp(foulY, -1), foulY, LaneXp(foulY, +1), foulY,
            new SolidColorBrush(C("#273746")), Math.Max(3.2, _laneHalfBot * 0.018)));
        Root.Children.Add(PlankLine(LaneXp(foulY, -1), foulY + 2.2, LaneXp(foulY, +1), foulY + 2.2,
            new SolidColorBrush(Color.FromArgb(0x78, 0xFF, 0xE8, 0xB7)), 1.0));
    }

    // ── 그리기 헬퍼(물리 px 입력) ──
    private Polygon Poly(Brush fill, double opacity, params (double x, double y)[] pts)
    {
        var pc = new PointCollection();
        foreach (var (x, y) in pts) pc.Add(new Point(LX(x), LY(y)));
        return new Polygon { Points = pc, Fill = fill, Opacity = opacity, IsHitTestVisible = false };
    }

    // 두 점을 잇는 선(원근 결·경계선). thickness 는 물리 px.
    private System.Windows.Shapes.Line PlankLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness = 1.4)
        => new()
        {
            X1 = LX(x1), Y1 = LY(y1), X2 = LX(x2), Y2 = LY(y2),
            Stroke = stroke, StrokeThickness = Math.Max(1, LW(thickness)), IsHitTestVisible = false,
        };

    // 위를 향한 삼각형(핀쪽 조준 다트)
    private Polygon Arrow(double cx, double topY, double size, Brush fill)
    {
        double w = LW(size), h = LH(size * 1.6);
        double x = LX(cx), y = LY(topY);
        return new Polygon
        {
            Fill = fill,
            Opacity = 0.85,
            IsHitTestVisible = false,
            Points = new PointCollection { new Point(x, y), new Point(x - w, y + h), new Point(x + w, y + h) },
        };
    }

    private Ellipse Dot(double cx, double cy, double radius, Brush fill)
    {
        var dot = new Ellipse
        {
            Width = LW(radius * 2),
            Height = LH(radius * 2),
            Fill = fill,
            Opacity = 0.82,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dot, LX(cx) - dot.Width / 2);
        Canvas.SetTop(dot, LY(cy) - dot.Height / 2);
        return dot;
    }

    private static Brush VGrad(params (string hex, double off)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (hex, off) in stops) b.GradientStops.Add(new GradientStop(C(hex), off));
        b.Freeze();
        return b;
    }

    private static Brush HGrad(params (string hex, double off)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        foreach (var (hex, off) in stops) b.GradientStops.Add(new GradientStop(C(hex), off));
        b.Freeze();
        return b;
    }
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
