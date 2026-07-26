using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using UserControl = System.Windows.Controls.UserControl;
using Canvas = System.Windows.Controls.Canvas;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Ellipse = System.Windows.Shapes.Ellipse;
using Path = System.Windows.Shapes.Path;

namespace Slimey.Views.Skins;

/// <summary>
/// 파란 마블 볼링공. 단단한 공(Rigid) — 찌그러지지 않는다.
/// 9가지 무늬 면(마블 소용돌이 + 손가락 구멍 배치)을 미리 만들어 두고,
/// 굴러간 회전수에 맞춰 보이는 면을 넘겨 "굴러가며 모양이 바뀌는" 느낌을 준다.
/// </summary>
public partial class BowlingSkin : UserControl, ISkinRolling
{
    // 84x84(표면 클립 내부) 좌표계에서 그린다.
    private const double Surf = 84.0;

    // 마블 팔레트(파랑)
    private static readonly Color BodyBase = Color.FromRgb(0x2C, 0x79, 0xC0);
    private static readonly Color SwirlLight = Color.FromRgb(0xB6, 0xDF, 0xFF);
    private static readonly Color SwirlBright = Color.FromRgb(0xEC, 0xF6, 0xFF);
    private static readonly Color SwirlMid = Color.FromRgb(0x59, 0xA6, 0xE2);
    private static readonly Color VeinDark = Color.FromRgb(0x12, 0x35, 0x5E);
    private static readonly Color VeinDarker = Color.FromRgb(0x08, 0x1C, 0x3A);

    // 9개 면. 각 면 = (마블 시드, 손가락 구멍 배치[cx,cy])
    private static readonly (int seed, (double x, double y)[] holes)[] Faces =
    {
        (101, new[] { (30d, 24d), (46d, 24d), (38d, 39d) }),                 // 3구 좌상 삼각
        (211, new[] { (37d, 22d), (53d, 22d), (45d, 37d) }),                 // 3구 상단 중앙
        (307, Array.Empty<(double, double)>()),                              // 무구멍(뒷면)
        (443, new[] { (32d, 30d), (49d, 35d) }),                             // 2구
        (521, Array.Empty<(double, double)>()),                              // 무구멍
        (659, new[] { (55d, 33d), (61d, 48d), (53d, 60d) }),                 // 3구 우측
        (733, Array.Empty<(double, double)>()),                              // 무구멍
        (817, new[] { (34d, 23d), (50d, 24d), (42d, 38d) }),                 // 3구 상단 뭉침
        (929, new[] { (36d, 27d), (52d, 26d), (44d, 41d) }),                 // 3구 상단
    };

    private readonly List<Canvas> _faceLayers = new();
    private int _index;
    private double _rollAccum; // 누적 회전수

    public BowlingSkin()
    {
        InitializeComponent();

        Body.Fill = SphereBrush(BodyBase);

        for (int i = 0; i < Faces.Length; i++)
        {
            var layer = BuildFace(Faces[i].seed, Faces[i].holes);
            layer.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
            PatternHost.Children.Add(layer);
            _faceLayers.Add(layer);
        }
    }

    // ── 굴러가며 면 교체 ────────────────────────────────────
    /// <summary>한 바퀴(1.0)를 굴러가는 동안 9면을 모두 지나간다.</summary>
    public void OnRoll(double revolutions)
    {
        if (_faceLayers.Count == 0) return;
        double step = 1.0 / _faceLayers.Count; // 면당 회전수
        _rollAccum += revolutions;

        while (_rollAccum >= step) { _rollAccum -= step; Advance(+1); }
        while (_rollAccum <= -step) { _rollAccum += step; Advance(-1); }
    }

    private void Advance(int dir)
    {
        int n = _faceLayers.Count;
        _faceLayers[_index].Visibility = Visibility.Collapsed;
        _index = ((_index + dir) % n + n) % n;
        _faceLayers[_index].Visibility = Visibility.Visible;
    }

    // ── 한 면(마블 무늬 + 구멍) 생성 ─────────────────────────
    private static Canvas BuildFace(int seed, (double x, double y)[] holes)
    {
        var c = new Canvas
        {
            Width = Surf,
            Height = Surf,
            IsHitTestVisible = false,
            // 살짝 흐리게 해서 소용돌이가 마블처럼 번지도록.
            Effect = new BlurEffect { Radius = 2.1, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
        };
        var rng = new Random(seed);

        // 넓은 밝은 소용돌이(면적감) → 중간 톤 → 얇고 옅은 정맥(음영) 순으로 겹쳐
        // 흐르는 마블 느낌을 만든다.
        int lightBands = 4 + rng.Next(0, 2);
        for (int i = 0; i < lightBands; i++)
            c.Children.Add(Swirl(rng, PickLight(rng), 15 + rng.NextDouble() * 10, 0.36 + rng.NextDouble() * 0.22));

        int midBands = 2 + rng.Next(0, 2);
        for (int i = 0; i < midBands; i++)
            c.Children.Add(Swirl(rng, SwirlMid, 9 + rng.NextDouble() * 6, 0.26 + rng.NextDouble() * 0.16));

        int veins = 1 + rng.Next(0, 2);
        for (int i = 0; i < veins; i++)
            c.Children.Add(Swirl(rng, rng.NextDouble() < 0.5 ? VeinDark : VeinDarker, 2.5 + rng.NextDouble() * 2.5, 0.30 + rng.NextDouble() * 0.16));

        // 밝은 하이라이트 실선 한 줄(광택 흐름)
        c.Children.Add(Swirl(rng, SwirlBright, 2.5 + rng.NextDouble() * 2.5, 0.30));

        // 손가락 구멍(무늬 위)
        foreach (var (hx, hy) in holes)
            c.Children.Add(Hole(hx, hy, 12.5));

        return c;
    }

    private static Color PickLight(Random rng) => rng.NextDouble() < 0.5 ? SwirlLight : SwirlBright;

    /// <summary>공을 가로질러 물결치는 마블 소용돌이 한 줄.</summary>
    private static Path Swirl(Random rng, Color color, double thickness, double opacity)
    {
        bool vertical = rng.NextDouble() < 0.5;
        double baseLine = 10 + rng.NextDouble() * 64;      // 소용돌이 중심선
        double amp = 9 + rng.NextDouble() * 15;            // 진폭
        double freq = 0.8 + rng.NextDouble() * 1.4;        // 주기 수(완만하게)
        double phase = rng.NextDouble() * Math.PI * 2;

        var pts = new PointCollection();
        for (double t = 0; t <= 1.0001; t += 0.05)
        {
            double along = -10 + t * (Surf + 20);          // 가장자리 너머까지 쓸어 클립되게
            double off = baseLine + amp * Math.Sin(t * freq * Math.PI * 2 + phase)
                         + (rng.NextDouble() - 0.5) * 4;   // 자연스러운 흔들림
            pts.Add(vertical ? new Point(off, along) : new Point(along, off));
        }

        var fig = new PathFigure { StartPoint = pts[0], IsClosed = false, IsFilled = false };
        fig.Segments.Add(new PolyLineSegment(pts, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return new Path
        {
            Data = geo,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = opacity,
            IsHitTestVisible = false,
        };
    }

    /// <summary>손가락 구멍(움푹한 깊이감).</summary>
    private static Ellipse Hole(double cx, double cy, double d)
    {
        var g = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.38, 0.30),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.58,
            RadiusY = 0.58,
        };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x35, 0x3B, 0x49), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x0C, 0x0E, 0x16), 0.7));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x02, 0x03, 0x07), 1.0));
        g.Freeze();

        var rim = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
        rim.Freeze();

        var e = new Ellipse
        {
            Width = d,
            Height = d,
            Fill = g,
            Stroke = rim,
            StrokeThickness = 0.7,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(e, cx - d / 2);
        Canvas.SetTop(e, cy - d / 2);
        return e;
    }

    // ── 구체 베이스 브러시(당구공 스킨과 동일 방식) ──────────
    private static Brush SphereBrush(Color c)
    {
        var b = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.35, 0.3),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.72,
            RadiusY = 0.72,
        };
        b.GradientStops.Add(new GradientStop(Lighten(c, 0.5), 0.0));
        b.GradientStops.Add(new GradientStop(c, 0.5));
        b.GradientStops.Add(new GradientStop(Darken(c, 0.32), 0.85));
        b.GradientStops.Add(new GradientStop(Darken(c, 0.55), 1.0));
        b.Freeze();
        return b;
    }

    private static Color Lighten(Color c, double f) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
    private static Color Darken(Color c, double f) => Color.FromRgb(
        (byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));
}
