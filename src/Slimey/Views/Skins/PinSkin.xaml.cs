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

    /// <summary>핀 실루엣의 최대폭(캔버스 단위).</summary>
    public const double PinMaxWidth = 52.0;

    /// <summary>기존 볼링 연쇄 충돌 감각을 유지하는 물리 폭(캔버스 단위).</summary>
    public const double CollisionWidth = 44.0;

    // 320x320 캔버스 안 핀 실루엣. 머리→목→어깨→배로 이어지는 실제 핀의 S 곡선을 살렸다.
    // 중심 x=160, 머리 y=54, 밑동 y=210(물리 박스 하단), 길이 156, 최대폭 52.
    private const string PinPath =
        "M160,54 " +
        "C169.3,54 173.2,60.5 173.2,69.5 " +
        "C173.2,80 168.2,88 168.8,98 " +
        "C169.4,107 174.5,116 178.5,128 " +
        "C183.5,142 186.5,159 186,175 " +
        "C185.5,191 180,202 176.5,208 " +
        "Q160,213 143.5,208 " +
        "C140,202 134.5,191 134,175 " +
        "C133.5,159 136.5,142 141.5,128 " +
        "C145.5,116 150.6,107 151.2,98 " +
        "C151.8,88 146.8,80 146.8,69.5 " +
        "C146.8,60.5 150.7,54 160,54 Z";
    public PinSkin()
    {
        InitializeComponent();
        Build();
    }

    private void Build()
    {
        var geo = Geometry.Parse(PinPath);
        geo.Freeze();

        // 중앙은 따뜻한 백색, 양 가장자리에는 청회색 음영을 넣어 도자기 볼륨을 만든다.
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0.35), EndPoint = new Point(1, 0.65) };
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xC9, 0xD0, 0xD8), 0.0));
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xF5, 0xF7, 0xF8), 0.18));
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFC), 0.46));
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xEE, 0xF1, 0xF4), 0.76));
        fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xB9, 0xC2, 0xCD), 1.0));
        fill.Freeze();
        PinCanvas.Children.Add(new Path { Data = geo, Fill = fill, IsHitTestVisible = false });

        // 얇은 테두리와 유광 그라데이션을 가진 전통적인 빨간 두 줄.
        var stripes = new Canvas { Width = Box, Height = Box, Clip = geo, IsHitTestVisible = false };
        var red = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        red.GradientStops.Add(new GradientStop(Color.FromRgb(0xA8, 0x12, 0x21), 0));
        red.GradientStops.Add(new GradientStop(Color.FromRgb(0xF3, 0x38, 0x3E), 0.38));
        red.GradientStops.Add(new GradientStop(Color.FromRgb(0xC5, 0x19, 0x2B), 1));
        red.Freeze();
        var redEdge = new SolidColorBrush(Color.FromRgb(0x8F, 0x13, 0x20)); redEdge.Freeze();
        stripes.Children.Add(Band(redEdge, 96.0, 9.0));
        stripes.Children.Add(Band(red, 97.0, 7.0));
        stripes.Children.Add(Band(redEdge, 109.0, 9.0));
        stripes.Children.Add(Band(red, 110.0, 7.0));
        PinCanvas.Children.Add(stripes);

        // 아래쪽 접지 음영.
        var shade = new LinearGradientBrush { StartPoint = new Point(0.5, 0.35), EndPoint = new Point(0.5, 1) };
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x25, 0x32, 0x42), 0.56));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x08, 0x25, 0x32, 0x42), 0.76));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x2E, 0x25, 0x32, 0x42), 1.0));
        shade.Freeze();
        PinCanvas.Children.Add(new Path { Data = geo, Fill = shade, IsHitTestVisible = false });

        // 왼쪽 어깨를 따라 흐르는 부드러운 유광 하이라이트.
        var glossCanvas = new Canvas { Width = Box, Height = Box, Clip = geo, IsHitTestVisible = false };
        var gloss = new Ellipse
        {
            Width = 13,
            Height = 88,
            Fill = new SolidColorBrush(Color.FromArgb(0x74, 0xFF, 0xFF, 0xFF)),
            RenderTransform = new RotateTransform(8),
            Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 3.2 },
        };
        Canvas.SetLeft(gloss, 145);
        Canvas.SetTop(gloss, 112);
        glossCanvas.Children.Add(gloss);
        var headGloss = new Ellipse
        {
            Width = 8,
            Height = 15,
            Fill = new SolidColorBrush(Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF)),
        };
        Canvas.SetLeft(headGloss, 151);
        Canvas.SetTop(headGloss, 59);
        glossCanvas.Children.Add(headGloss);
        PinCanvas.Children.Add(glossCanvas);

        // 작은 크기에서도 형태를 잡아 주되 만화처럼 세지 않은 청회색 외곽선.
        var outline = new SolidColorBrush(Color.FromRgb(0x56, 0x62, 0x70)); outline.Freeze();
        PinCanvas.Children.Add(new Path
        {
            Data = geo,
            Stroke = outline,
            StrokeThickness = 1.65,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        });

        var baseLine = new SolidColorBrush(Color.FromArgb(0x88, 0x58, 0x63, 0x70)); baseLine.Freeze();
        PinCanvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M144,208 Q160,212.2 176,208"),
            Stroke = baseLine,
            StrokeThickness = 1.1,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        });
    }
    private static Rectangle Band(Brush fill, double y, double h)
    {
        var r = new Rectangle { Width = 64, Height = h, Fill = fill };
        Canvas.SetLeft(r, 128);  // 핀 폭을 넉넉히 덮고 실루엣 클립으로 잘린다
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
