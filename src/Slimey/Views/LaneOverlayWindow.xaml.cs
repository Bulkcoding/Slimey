using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Slimey.Services;
using Canvas = System.Windows.Controls.Canvas;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using ColorConverter = System.Windows.Media.ColorConverter;
using Polygon = System.Windows.Shapes.Polygon;
using TextBlock = System.Windows.Controls.TextBlock;
using FontFamily = System.Windows.Media.FontFamily;

namespace Slimey.Views;

/// <summary>
/// 볼링판(레인) 오버레이 — 주 모니터 작업영역을 덮는 클릭 통과 창.
/// 나무 레인 + 거터 + 파울 라인 + 조준 화살표 + 핀덱 + 점수를 그린다.
/// </summary>
public partial class LaneOverlayWindow : Window
{
    private readonly MonitorLayoutService _monitors;
    private double _scaleX = 1, _scaleY = 1;
    private Rect _wa;
    private TextBlock? _score;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);

    public LaneOverlayWindow(MonitorLayoutService monitors)
    {
        _monitors = monitors;
        InitializeComponent();
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

        // ── 거터(양옆 어두운 홈, 사다리꼴) ──
        var gutterBrush = VGrad(("#22252D", 0), ("#0E1016", 0.5), ("#24272F", 1));
        // 왼쪽
        Root.Children.Add(Poly(gutterBrush, 0.92,
            (AlleyXp(topY, -1), topY), (LaneXp(topY, -1), topY),
            (LaneXp(botY, -1), botY), (AlleyXp(botY, -1), botY)));
        // 오른쪽
        Root.Children.Add(Poly(gutterBrush, 0.92,
            (LaneXp(topY, +1), topY), (AlleyXp(topY, +1), topY),
            (AlleyXp(botY, +1), botY), (LaneXp(botY, +1), botY)));
        // 거터 안쪽 하이라이트(레인과 경계)
        Root.Children.Add(Poly(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), 1.0,
            (LaneXp(topY, -1), topY), (LaneXp(topY, -1) + 2, topY),
            (LaneXp(botY, -1) + 4, botY), (LaneXp(botY, -1), botY)));

        // ── 레인 바닥(단풍나무, 사다리꼴; 아래로 갈수록 밝아 원근 강조) ──
        var wood = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        wood.GradientStops.Add(new GradientStop(C("#B98E4F"), 0.0));   // 원경(어두움)
        wood.GradientStops.Add(new GradientStop(C("#D8B678"), 0.5));
        wood.GradientStops.Add(new GradientStop(C("#ECD199"), 1.0));   // 근경(밝음)
        wood.Freeze();
        Root.Children.Add(Poly(wood, 0.95,
            (LaneXp(topY, -1), topY), (LaneXp(topY, +1), topY),
            (LaneXp(botY, +1), botY), (LaneXp(botY, -1), botY)));

        // 널(결) 라인 — 소실점으로 수렴(원근)
        var plankBrush = new SolidColorBrush(Color.FromArgb(0x2A, 0x5A, 0x3A, 0x12)); plankBrush.Freeze();
        int planks = 9;
        for (int i = 1; i < planks; i++)
        {
            double s = -1.0 + 2.0 * i / planks;
            Root.Children.Add(PlankLine(
                _centerX + s * _laneHalfTop, topY,
                _centerX + s * _laneHalfBot, botY, plankBrush));
        }

        // ── 핀덱(뒤쪽 살짝 어두운 판, 사다리꼴) ──
        Root.Children.Add(Poly(VGrad(("#A9834C", 0), ("#C6A46B", 1)), 0.85,
            (LaneXp(topY, -1), topY), (LaneXp(topY, +1), topY),
            (LaneXp(deckBotY, +1), deckBotY), (LaneXp(deckBotY, -1), deckBotY)));
        Root.Children.Add(PlankLine(LaneXp(deckBotY, -1), deckBotY, LaneXp(deckBotY, +1), deckBotY,
            new SolidColorBrush(Color.FromArgb(0x55, 0x4A, 0x30, 0x10)), 2.2));

        // ── 조준 화살표(가운데가 위로 솟은 7개 다트, 원근 크기) ──
        var arrowBrush = new SolidColorBrush(C("#8A5A22")); arrowBrush.Freeze();
        double aHalf = HalfAt(_laneHalfTop, _laneHalfBot, arrowsY);
        double aSize = aHalf * 0.085;
        for (int i = 0; i < 7; i++)
        {
            double t = (i - 3) / 3.0;
            double ax = _centerX + t * (aHalf * 0.72);
            double ay = arrowsY + Math.Abs(t) * (aHalf * 0.28);
            Root.Children.Add(Arrow(ax, ay, aSize, arrowBrush));
        }

        // ── 파울 라인(빨강, 레인 폭 전체) ──
        Root.Children.Add(PlankLine(LaneXp(foulY, -1), foulY, LaneXp(foulY, +1), foulY,
            new SolidColorBrush(C("#C62A24")), Math.Max(2.5, HalfAt(0, 0, foulY) + _laneHalfBot * 0.012)));

        // ── 점수 텍스트(레인 상단 중앙) ──
        _score = new TextBlock
        {
            Text = "1F · 1구 · 0점",
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = LH(_laneHalfBot * 0.16),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xD6)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = 0.85 },
        };
        _score.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_score, LX(_centerX) - _score.DesiredSize.Width / 2);
        Canvas.SetTop(_score, LY(topY) - _score.DesiredSize.Height - LH(_laneHalfBot * 0.03));
        Root.Children.Add(_score);
    }

    private string? _statusText;
    private Color _statusColor;

    /// <summary>상단 상태 표시(프레임/투구/점수, 또는 STRIKE·거터 같은 배너).</summary>
    public void SetStatus(string text, Color color)
    {
        if (_score == null) return;
        if (_statusText == text && _statusColor == color) return; // 매 프레임 재할당 방지
        _statusText = text;
        _statusColor = color;
        _score.Text = text;
        var b = new SolidColorBrush(color); b.Freeze();
        _score.Foreground = b;
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

    private static Brush VGrad(params (string hex, double off)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (hex, off) in stops) b.GradientStops.Add(new GradientStop(C(hex), off));
        b.Freeze();
        return b;
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
