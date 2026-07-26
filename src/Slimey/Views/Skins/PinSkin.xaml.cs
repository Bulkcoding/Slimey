using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using UserControl = System.Windows.Controls.UserControl;
using Canvas = System.Windows.Controls.Canvas;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Ellipse = System.Windows.Shapes.Ellipse;
using Path = System.Windows.Shapes.Path;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Slimey.Views.Skins;

/// <summary>
/// 표준 볼링핀(흰 몸통 + 빨간 목 줄무늬 2줄). 맞으면 밑동을 축으로 넘어진다(tip-over).
/// </summary>
public partial class PinSkin : UserControl
{
    /// <summary>디자인 캔버스 한 변(= 핀 창 전체 크기, SlimeSize*10/3 에 대응).</summary>
    public const double Box = 320.0;

    /// <summary>핀 최대폭(캔버스 단위). 물리 충돌 반경 계산에 쓰라고 공개한다.</summary>
    public const double PinMaxWidth = 44.0;

    // 320x320 캔버스 안 핀 실루엣. 참고 이미지보다 더 길고(높이:최대폭 ≈ 3.4:1)
    // 바닥은 더 좁게(바닥/최대폭 = 0.61) 잡았다.
    // 중심 x=160, 머리 y=58, 밑동 y=208(물리 박스 하단), 길이 150.
    private const string PinPath =
        "M160,58 " +
        "C170,58 172.8,63 172.8,74.5 C172.8,86 169.2,96 169.2,104.5 " +    // 돔 머리 → 가는 목
        "C169.2,118 171,128 175,143 C179,156 182,168 182,175 " +           // 어깨 → 배(가장 넓음)
        "C182,186 179,200 173.5,208 " +                                     // 배 → 좁은 바닥으로
        "L146.5,208 " +                                                     // 평평하고 좁은 바닥
        "C141,200 138,186 138,175 C138,168 141,156 145,143 " +
        "C149,128 150.8,118 150.8,104.5 C150.8,96 147.2,86 147.2,74.5 " +
        "C147.2,63 150,58 160,58 Z";

    public PinSkin()
    {
        InitializeComponent();
        Build();
    }

    private void Build()
    {
        var geo = Geometry.Parse(PinPath);
        geo.Freeze();

        // ── 몸통: 밝은 회백색(참고 이미지의 납작한 카툰 톤 + 약한 볼륨) ──
        var fill = new LinearGradientBrush { StartPoint = new Point(0.15, 0.1), EndPoint = new Point(0.95, 1.0) };
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0.0));
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xF0, 0xF0, 0xF2), 0.55));
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xDF, 0xDF, 0xE4), 1.0));
        fill.Freeze();
        PinCanvas.Children.Add(new Path { Data = geo, Fill = fill, IsHitTestVisible = false });

        // ── 목 줄무늬: 빨강 / 흰색 / 빨강 (각 밴드 위아래에 검정 구분선) ──
        var stripes = new Canvas { Width = Box, Height = Box, Clip = geo, IsHitTestVisible = false };
        var red = new SolidColorBrush(Color.FromRgb(0xE1, 0x33, 0x2B)); red.Freeze();
        var ink = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)); ink.Freeze();
        const double lw = 2.0;   // 검정 구분선 두께
        double y = 100.0;        // 목~어깨 구간
        stripes.Children.Add(Band(ink, y, lw)); y += lw;
        stripes.Children.Add(Band(red, y, 8.0)); y += 8.0;        // 빨강
        stripes.Children.Add(Band(ink, y, lw)); y += lw + 6.0;     // 흰색(몸통 색이 그대로 보임)
        stripes.Children.Add(Band(ink, y, lw)); y += lw;
        stripes.Children.Add(Band(red, y, 8.0)); y += 8.0;        // 빨강
        stripes.Children.Add(Band(ink, y, lw));
        PinCanvas.Children.Add(stripes);

        // ── 오른쪽 아래 그늘(살짝만) ──
        var shade = new LinearGradientBrush { StartPoint = new Point(0.35, 0.25), EndPoint = new Point(0.95, 1.0) };
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x24, 0x24, 0x2E), 0.6));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, 0x24, 0x24, 0x2E), 1.0));
        shade.Freeze();
        PinCanvas.Children.Add(new Path { Data = geo, Fill = shade, IsHitTestVisible = false });

        // ── 왼쪽 세로 하이라이트(광택) ──
        var hi = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        hi.GradientStops.Add(new GradientStop(Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF), 0.0));
        hi.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
        hi.Freeze();
        var glossCanvas = new Canvas { Width = Box, Height = Box, Clip = geo, IsHitTestVisible = false };
        var glossRect = new Rectangle { Width = 13, Height = Box, Fill = hi };
        Canvas.SetLeft(glossRect, 144);
        Canvas.SetTop(glossRect, 0);
        glossCanvas.Children.Add(glossRect);
        PinCanvas.Children.Add(glossCanvas);

        // ── 굵은 검정 외곽선(참고 이미지 핵심). 맨 위에 올려 또렷하게. ──
        var outline = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x15)); outline.Freeze();
        PinCanvas.Children.Add(new Path
        {
            Data = geo,
            Stroke = outline,
            StrokeThickness = 3.4,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        });
    }

    private static Rectangle Band(Brush fill, double y, double h)
    {
        var r = new Rectangle { Width = 56, Height = h, Fill = fill };
        Canvas.SetLeft(r, 132);  // 핀 폭(138~182)을 넉넉히 덮고 클립으로 잘린다
        Canvas.SetTop(r, y);
        return r;
    }

    /// <summary>넘어짐/일으켜세우기. dirSign: +1 오른쪽으로, -1 왼쪽으로 쓰러짐.</summary>
    public void SetKnocked(bool down, int dirSign)
    {
        double target = down ? 80.0 * (dirSign >= 0 ? 1 : -1) : 0.0;
        var anim = new DoubleAnimation(target, new Duration(TimeSpan.FromSeconds(down ? 0.22 : 0.18)))
        {
            EasingFunction = new CubicEase { EasingMode = down ? EasingMode.EaseIn : EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        Tip.BeginAnimation(RotateTransform.AngleProperty, anim);
    }
}
