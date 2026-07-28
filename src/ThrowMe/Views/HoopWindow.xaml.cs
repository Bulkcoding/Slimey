using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ThrowMe.Models;
using ThrowMe.Physics;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Ellipse = System.Windows.Shapes.Ellipse;
using Path = System.Windows.Shapes.Path;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ThrowMe.Views;

/// <summary>어느 벽에 붙는 골대인가.</summary>
public enum HoopSide { Left, Right }

/// <summary>
/// 모니터 벽에 붙는 농구 골대(기둥+지지대+넓은 백보드+빨간 수평 림+그물). 클릭 통과·항상 위.
/// 림이 수평이라 중력으로 떨어지는 공을 위→아래로 통과시키면 득점.
/// 빨간 림 가장자리와 백보드는 <b>단단한 충돌체</b>로 노출되어(SlimeWindow 가 판정) 공이 튕긴다.
/// 득점/충돌 시 그물이 노드-연결 스프링으로 파도치듯 펄럭인다.
/// </summary>
public partial class HoopWindow : Window
{
    public HoopSide Side { get; }

    /// <summary>오프라인 미리보기 렌더용 루트 캔버스 접근자.</summary>
    public Canvas PreviewRoot => Root;

    private double _scaleX = 1, _scaleY = 1;
    private readonly double _winLeftPx, _winTopPx, _winWpx, _winHpx;

    // ── 득점/충돌 판정용(물리 픽셀) ──
    /// <summary>림 개구부 중심(물리 px). 공이 이 높이를 아래로 통과하면 득점.</summary>
    public Vector2 RimCenter { get; }
    /// <summary>득점으로 인정하는 림 개구부 가로 반폭(물리 px).</summary>
    public double RimHalfWidth { get; }
    /// <summary>빨간 림의 양 끝(앞/뒤) 충돌 지점(물리 px). 공이 여기 맞으면 튕긴다.</summary>
    public Vector2[] RimEdges { get; }
    /// <summary>림 튜브(가장자리) 충돌 반경(물리 px).</summary>
    public double RimEdgeRadius { get; }
    /// <summary>뒷판 전체 충돌 사각형(물리 px). 공이 부딪히면 죽은 반발로 튕긴다.</summary>
    public Rect Backboard { get; }

    /// <summary>
    /// 스윗스팟(참고 이미지의 파란 사각형 영역, 물리 px). 뒷판 중 림 바로 위 이 구역을 맞히면
    /// 공이 림으로 떨어지도록 유도된다(뱅크샷). 나머지 뒷판은 유도 없이 그냥 죽은 반발.
    /// </summary>
    public Rect SweetSpot { get; }
    /// <summary>중복 득점 방지용 쿨다운 만료 시각(초). SlimeWindow 가 설정.</summary>
    public double ScoreCooldownUntil { get; set; }

    // ── 그물 모델(노드-연결 스프링 → 파도치는 펄럭임) ──
    private const int Rings = 6;      // 림(0) + 아래 6링
    private const int Strands = 12;   // 원주 분할
    private readonly Point[,] _rest = new Point[Rings + 1, Strands];
    private readonly Vector2[,] _disp = new Vector2[Rings + 1, Strands];
    private readonly Vector2[,] _vel = new Vector2[Rings + 1, Strands];
    private readonly double _netKickCap;
    private readonly double _maxDisp;
    private readonly Random _rng = new();

    private Path _netBack = null!, _netFront = null!;
    private Ellipse _glow = null!;
    private double _flash;

    #region Win32 클릭 통과
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    #endregion

    public HoopWindow(HoopSide side, Rect workingArea, AppSettings settings)
    {
        Side = side;
        InitializeComponent();

        double s = settings.SlimeSize;
        Rect wa = workingArea;

        double poleW = s * 0.17;
        double bbBack = s * 0.38, bbFront = s * 0.72;   // 뒷판(벽 기준 거리)
        double bbH = s * 3.30;                          // 뒷판 높이(크게 — 실제 백보드 비율)
        double rimRx = s * 0.69;                        // 림 개구부 가로 반경(공이 깨끗이 통과할 만큼 넓게)
        double rimRy = s * 0.15;                        // 림 개구부 세로 반경(원근)
        double rimGap = s * 0.34;                       // 백보드와 림 사이 간격(짧은 연결대로 이음)
        double rimCX = bbFront + rimGap + rimRx;         // 림을 백보드에서 살짝 띄움
        double netH = s * 1.50;

        _netKickCap = s * 2.55;     // 그물 킥 상한(펄럭임 약 5% ↑, 그래도 절제)
        _maxDisp = s * 0.38;        // 노드 변위 상한(림까지 못 올라가게)
        RimEdgeRadius = s * 0.07;
        // 득점 인정 폭. 겹침 없이 깨끗이 지나는 폭은 rimRx - RimEdgeRadius - 0.44S(≈0.18S)뿐인데,
        // 빠른 공은 프레임 단위 이동 때문에 림 높이에서 이미 살짝 겹친 상태로 통과한다(눈에는 들어간 골).
        // 공 표면이 림 튜브 중심(rimRx)에 닿는 지점까지 허용: 0.69 - 0.44 ≈ 0.25S.
        RimHalfWidth = s * 0.24;

        double rimYphys = wa.Top + wa.Height * 0.42;    // 골대 높이(조금 아래로)
        double bbCenterY = rimYphys - s * 1.25;         // 뒷판 대부분이 림 위로(현실적)
        int dir = side == HoopSide.Left ? +1 : -1;
        double wallX = side == HoopSide.Left ? wa.Left : wa.Right;
        double X(double distFromWall) => wallX + dir * distFromWall;

        RimCenter = new Vector2(X(rimCX), rimYphys);
        RimEdges = new[]
        {
            new Vector2(X(rimCX - rimRx), rimYphys), // 뒤(벽 쪽) 가장자리
            new Vector2(X(rimCX + rimRx), rimYphys), // 앞(코트 쪽) 가장자리
        };
        double bx0 = X(bbBack), bx1 = X(bbFront);
        double bLeft = Math.Min(bx0, bx1), bWidth = Math.Abs(bx1 - bx0);
        Backboard = new Rect(bLeft, bbCenterY - bbH / 2, bWidth, bbH);
        // 스윗스팟: 림 바로 위 좁은 띠(참고 이미지의 파란 사각형에 해당)
        double ssTop = rimYphys - s * 1.20, ssBottom = rimYphys - s * 0.05;
        SweetSpot = new Rect(bLeft, ssTop, bWidth, ssBottom - ssTop);

        // 창 경계(물리 px). 커진 뒷판 상단까지 덮도록 위 여백 확대.
        _winTopPx = rimYphys - s * 3.00;
        _winHpx = (rimYphys + netH + s * 0.35) - _winTopPx;
        _winWpx = rimCX + rimRx + s * 0.20;
        _winLeftPx = side == HoopSide.Left ? wallX : wallX - _winWpx;

        Root.Width = _winWpx;
        Root.Height = _winHpx;

        double LX(double distFromWall) => side == HoopSide.Left ? distFromWall : _winWpx - distFromWall;
        double LY(double physY) => physY - _winTopPx;
        var rimLocal = new Point(LX(rimCX), LY(rimYphys));

        BuildStructure(side, LX, LY, poleW, bbBack, bbFront, bbCenterY, bbH, rimCX, rimRx, rimYphys, s);
        BuildRimAndNet(rimLocal, rimRx, rimRy, netH, s);
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
        Left = _winLeftPx / _scaleX;
        Top = _winTopPx / _scaleY;
        Width = _winWpx / _scaleX;
        Height = _winHpx / _scaleY;
    }

    // ── 기둥 + 지지대 + 백보드 ───────────────────────────────
    private void BuildStructure(HoopSide side, Func<double, double> LX, Func<double, double> LY,
        double poleW, double bbBack, double bbFront, double bbCenterY, double bbH,
        double rimCX, double rimRx, double rimYphys, double s)
    {
        var dark = Solid("#2A2C33");
        var poleFill = side == HoopSide.Left
            ? HGrad(("#7C808A", 0.0), ("#C8CCD6", 0.42), ("#63666F", 1.0))
            : HGrad(("#63666F", 0.0), ("#C8CCD6", 0.58), ("#7C808A", 1.0));

        double poleX = side == HoopSide.Left ? 0 : _winWpx - poleW;
        double armY = LY(rimYphys);

        // 기둥(창 전체 높이) + 밝은 세로 하이라이트
        var pole = new Rectangle
        {
            Width = poleW, Height = _winHpx, RadiusX = poleW * 0.4, RadiusY = poleW * 0.4,
            Fill = poleFill, Stroke = dark, StrokeThickness = s * 0.04,
        };
        Canvas.SetLeft(pole, poleX); Canvas.SetTop(pole, 0);
        Root.Children.Add(pole);
        var poleHi = new Rectangle
        {
            Width = poleW * 0.24, Height = _winHpx - s * 0.2, RadiusX = poleW * 0.12, RadiusY = poleW * 0.12,
            Fill = Solid("#E6EAF2"), Opacity = 0.55,
        };
        Canvas.SetLeft(poleHi, poleX + poleW * 0.32); Canvas.SetTop(poleHi, s * 0.1);
        Root.Children.Add(poleHi);

        // 대각 브레이스(기둥 → 백보드 위, 삼각 지지)
        double braceInnerX = side == HoopSide.Left ? poleW * 0.5 : _winWpx - poleW * 0.5;
        Root.Children.Add(new Path { Data = Triangle(
            new Point(braceInnerX, armY + s * 0.80),
            new Point(LX(bbBack), armY - s * 0.02),
            new Point(braceInnerX, armY - s * 0.10)), Fill = dark });

        // 수평 암(기둥 → 백보드)
        double ax0 = Math.Min(LX(poleW * 0.5), LX(bbBack));
        var arm = new Rectangle
        {
            Width = Math.Abs(LX(bbBack) - LX(poleW * 0.5)), Height = s * 0.18,
            RadiusX = s * 0.04, RadiusY = s * 0.04,
            Fill = HGrad(("#70747E", 0.0), ("#AEB2BC", 0.5), ("#63666F", 1.0)),
            Stroke = dark, StrokeThickness = s * 0.03,
        };
        Canvas.SetLeft(arm, ax0); Canvas.SetTop(arm, armY - s * 0.09);
        Root.Children.Add(arm);

        // 백보드: 실제처럼 "반투명 유리판 + 흰 프레임" → 회색 기둥/어두운 지지대와 확실히 구분된다.
        double bx = Math.Min(LX(bbBack), LX(bbFront));
        double bw = Math.Abs(LX(bbFront) - LX(bbBack));
        double bTop = LY(bbCenterY - bbH / 2);
        var glass = new Rectangle
        {
            Width = bw, Height = bbH, RadiusX = s * 0.04, RadiusY = s * 0.04,
            Fill = VGrad(("#9ED8F0", 0.0), ("#CFEAF7", 0.45), ("#7FBEDC", 1.0)),
            Opacity = 0.62,
        };
        Canvas.SetLeft(glass, bx); Canvas.SetTop(glass, bTop);
        Root.Children.Add(glass);
        // 흰 프레임(백보드 테두리)
        var frame = new Rectangle
        {
            Width = bw, Height = bbH, RadiusX = s * 0.04, RadiusY = s * 0.04,
            Stroke = Solid("#F4F7FA"), StrokeThickness = s * 0.07,
        };
        Canvas.SetLeft(frame, bx); Canvas.SetTop(frame, bTop);
        Root.Children.Add(frame);
        // 유리 하이라이트(세로 광택)
        var shine = new Rectangle
        {
            Width = bw * 0.22, Height = bbH - s * 0.30, RadiusX = s * 0.03, RadiusY = s * 0.03,
            Fill = Solid("#FFFFFF"), Opacity = 0.30,
        };
        Canvas.SetLeft(shine, bx + bw * 0.20); Canvas.SetTop(shine, bTop + s * 0.15);
        Root.Children.Add(shine);

        // 스윗스팟(참고 이미지의 파란 사각형): 림 바로 위 구역 — 여기 맞으면 잘 들어간다.
        var sweet = new Rectangle
        {
            Width = bw, Height = LY(SweetSpot.Bottom) - LY(SweetSpot.Top),
            Fill = Solid("#4A7FD0"), Opacity = 0.85,
            Stroke = Solid("#F4F7FA"), StrokeThickness = s * 0.03,
        };
        Canvas.SetLeft(sweet, bx); Canvas.SetTop(sweet, LY(SweetSpot.Top));
        Root.Children.Add(sweet);

        // 백보드 ↔ 림 연결대(짧은 빨간 넥): 백보드 앞면 → 림 뒤 가장자리
        double neckX0 = Math.Min(LX(bbFront), LX(rimCX - rimRx));
        double neckW = Math.Abs(LX(rimCX - rimRx) - LX(bbFront));
        double neckH = s * 0.15;
        var neck = new Rectangle
        {
            Width = neckW + s * 0.03, Height = neckH, RadiusX = s * 0.03, RadiusY = s * 0.03,
            Fill = Solid("#E8401A"), Stroke = Solid("#B0300C"), StrokeThickness = s * 0.03,
        };
        Canvas.SetLeft(neck, neckX0); Canvas.SetTop(neck, LY(rimYphys) - neckH / 2);
        Root.Children.Add(neck);
    }

    // ── 림 + 그물 ────────────────────────────────────────────
    private void BuildRimAndNet(Point rimLocal, double rimRx, double rimRy, double netH, double s)
    {
        // 득점 섬광(림 뒤 발광)
        _glow = new Ellipse { Width = rimRx * 3.0, Height = rimRy * 6.0, Opacity = 0, Fill = RadialGlow() };
        Canvas.SetLeft(_glow, rimLocal.X - rimRx * 1.5);
        Canvas.SetTop(_glow, rimLocal.Y - rimRy * 3.0);
        Root.Children.Add(_glow);

        // 그물 rest + 경로(림보다 먼저 그려 그물 위에 림이 덮이도록)
        BuildNetRest(rimLocal, rimRx, rimRy, netH);
        _netBack = new Path { Stroke = new SolidColorBrush(Color.FromArgb(0xC8, 0x1A, 0x1A, 0x1E)), StrokeThickness = s * 0.05, StrokeLineJoin = PenLineJoin.Round };
        _netFront = new Path { Stroke = new SolidColorBrush(Color.FromArgb(0xF2, 0xF4, 0xF4, 0xF6)), StrokeThickness = s * 0.028, StrokeLineJoin = PenLineJoin.Round };
        Root.Children.Add(_netBack);
        Root.Children.Add(_netFront);
        RenderNet();

        // 빨간 수평 림(뒤 어두운 → 앞 밝은)
        Root.Children.Add(new Path
        {
            Data = new EllipseGeometry(rimLocal, rimRx, rimRy),
            Stroke = Solid("#B0300C"), StrokeThickness = s * 0.12,
        });
        Root.Children.Add(new Path
        {
            Data = new EllipseGeometry(rimLocal, rimRx * 0.99, rimRy * 0.95),
            Stroke = Solid("#F2451A"), StrokeThickness = s * 0.07,
        });
    }

    private void BuildNetRest(Point rimLocal, double rimRx, double rimRy, double netH)
    {
        for (int r = 0; r <= Rings; r++)
        {
            double f = (double)r / Rings;
            double tr = 1.0 - 0.45 * f;             // 아래로 갈수록 좁아짐
            double cx = rimLocal.X, cy = rimLocal.Y + f * netH;
            for (int k = 0; k < Strands; k++)
            {
                double th = 2 * Math.PI * k / Strands;
                _rest[r, k] = new Point(cx + rimRx * tr * Math.Cos(th), cy + rimRy * tr * Math.Sin(th));
            }
        }
    }

    // ── 득점/충돌 → 그물 출렁 ────────────────────────────────
    public void OnScored(Vector2 ballVel) => KickNet(ballVel, 1.0, flash: true);

    /// <summary>림/백보드에 공이 맞았을 때 그물을 살짝 흔든다(득점 아님).</summary>
    public void Nudge(Vector2 ballVel) => KickNet(ballVel, 0.4, flash: false);

    private void KickNet(Vector2 ballVel, double scale, bool flash)
    {
        if (flash) _flash = 1.0;
        Vector2 kick = ballVel * (0.10 * scale);
        if (kick.Length > _netKickCap) kick = kick.Normalized() * _netKickCap;
        for (int r = 1; r <= Rings; r++)
        {
            double f = (double)r / Rings;
            for (int k = 0; k < Strands; k++)
            {
                double lateral = (_rng.NextDouble() - 0.5) * _netKickCap * 0.25 * f; // 가닥별 무작위 → 자연스런 펄럭
                _vel[r, k] += kick * (0.2 + 0.8 * f);
                _vel[r, k] = new Vector2(_vel[r, k].X + lateral, _vel[r, k].Y + _netKickCap * 0.35 * f * scale);
            }
        }
    }

    /// <summary>
    /// 그물 스프링 적분(노드-연결) + 섬광 감쇠 + 렌더. 아직 움직이면 true.
    /// 각 노드는 rest·위 노드·이웃 가닥과 연결돼, 킥이 파도처럼 아래로 전파되며 오래 펄럭인다.
    /// </summary>
    public bool UpdateNet(double dt)
    {
        if (dt <= 0) return _flash > 0.02;

        const double kRest = 22, kUp = 130, kSide = 30, c = 7.1;
        int sub = 2;
        double sdt = dt / sub;
        bool moving = false;

        for (int step = 0; step < sub; step++)
        {
            for (int r = 1; r <= Rings; r++)
            for (int j = 0; j < Strands; j++)
            {
                Vector2 d = _disp[r, j];
                Vector2 v = _vel[r, j];
                Vector2 up = _disp[r - 1, j];                                   // 위 노드(림=0은 고정)
                Vector2 nb = (_disp[r, (j - 1 + Strands) % Strands] + _disp[r, (j + 1) % Strands]) * 0.5;
                Vector2 a = d * -kRest + (up - d) * kUp + (nb - d) * kSide - v * c;
                v += a * sdt;
                d += v * sdt;
                if (d.Length > _maxDisp) d = d.Normalized() * _maxDisp;         // 변위 상한(림까지 못 올라가게)
                _disp[r, j] = d; _vel[r, j] = v;
            }
        }

        for (int r = 1; r <= Rings; r++)
        for (int j = 0; j < Strands; j++)
            if (_disp[r, j].LengthSquared > 0.02 || _vel[r, j].LengthSquared > 0.02) { moving = true; r = Rings + 1; break; }

        if (_flash > 0) { _flash *= Math.Exp(-6.0 * dt); if (_flash < 0.02) _flash = 0; }
        _glow.Opacity = _flash * 0.9;

        bool active = moving || _flash > 0;
        if (active) RenderNet();
        return active;
    }

    private void RenderNet()
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int r = 0; r < Rings; r++)
            for (int j = 0; j < Strands; j++)
            {
                Point a = NodeAt(r, j);
                ctx.BeginFigure(a, false, false); ctx.LineTo(NodeAt(r + 1, (j + 1) % Strands), true, false);
                ctx.BeginFigure(a, false, false); ctx.LineTo(NodeAt(r + 1, (j - 1 + Strands) % Strands), true, false);
            }
        }
        geo.Freeze();
        _netBack.Data = geo;
        _netFront.Data = geo;
    }

    private Point NodeAt(int r, int j)
    {
        Point p = _rest[r, j];
        Vector2 d = _disp[r, j];
        return new Point(p.X + d.X, p.Y + d.Y);
    }

    // ── 브러시/도형 헬퍼 ─────────────────────────────────────
    private static SolidColorBrush Solid(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b;
    }

    private static Brush HGrad(params (string hex, double off)[] stops) => Grad(new Point(0, 0), new Point(1, 0), stops);
    private static Brush VGrad(params (string hex, double off)[] stops) => Grad(new Point(0, 0), new Point(0, 1), stops);

    private static Brush Grad(Point a, Point b2, (string hex, double off)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = a, EndPoint = b2 };
        foreach (var (hex, off) in stops)
            b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hex), off));
        b.Freeze();
        return b;
    }

    private static Geometry Triangle(Point a, Point b, Point c)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(a, true, true);
            ctx.LineTo(b, true, false);
            ctx.LineTo(c, true, false);
        }
        g.Freeze();
        return g;
    }

    private static Brush RadialGlow()
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xD8, 0x8A), 0.0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0xFF, 0xA0, 0x30), 0.5));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xA0, 0x30), 1.0));
        b.Freeze();
        return b;
    }
}
