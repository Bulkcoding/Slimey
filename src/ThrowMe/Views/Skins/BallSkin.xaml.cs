using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ThrowMe.Models;
using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Rectangle = System.Windows.Shapes.Rectangle;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace ThrowMe.Views.Skins;

/// <summary>
/// 3D 느낌의 포켓몬 볼 스킨(공통 구조). 위 절반 색 + 상단 마킹만 종류별로 바꾼다.
/// 몬스터볼/하이퍼볼(울트라)/마스터볼을 하나의 컨트롤로 처리한다.
/// </summary>
public partial class BallSkin : UserControl, ISkinClickEffect
{
    public BallSkin(SlimeSkinKind kind)
    {
        InitializeComponent();
        Configure(kind);
    }

    private void Configure(SlimeSkinKind kind)
    {
        switch (kind)
        {
            case SlimeSkinKind.Ultra: // 하이퍼볼: 검정 + 노랑 H
                TopHalf.Fill = VGrad(("#4A4A54", 0.0), ("#2A2A32", 0.55), ("#141418", 1.0));
                AddUltraMark();
                break;

            case SlimeSkinKind.Master: // 마스터볼: 보라 + 분홍 M/점
                TopHalf.Fill = VGrad(("#A05FD0", 0.0), ("#7A3FB0", 0.45), ("#5A2A90", 0.8), ("#431C70", 1.0));
                AddMasterMark();
                break;

            default: // 몬스터볼: 빨강
                TopHalf.Fill = VGrad(("#FF7563", 0.0), ("#F0401F", 0.42), ("#D21B08", 0.8), ("#B31404", 1.0));
                break;
        }
    }

    /// <summary>세로 방향 LinearGradientBrush 생성.</summary>
    private static Brush VGrad(params (string hex, double offset)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (hex, offset) in stops)
            b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hex), offset));
        b.Freeze();
        return b;
    }

    // 하이퍼볼: 노란 "H"
    private void AddUltraMark()
    {
        var gold = (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F6C324"));
        gold.Freeze();
        AddRect(gold, 38, 12, 6, 20);  // 왼쪽 세로
        AddRect(gold, 52, 12, 6, 20);  // 오른쪽 세로
        AddRect(gold, 38, 19, 20, 6);  // 가로 연결
    }

    // 마스터볼: 분홍 "M" + 양쪽 점
    private void AddMasterMark()
    {
        var pink = (Color)ColorConverter.ConvertFromString("#F0619E");
        var pinkBrush = new SolidColorBrush(pink); pinkBrush.Freeze();

        // 양쪽 점
        AddDot(pinkBrush, 24, 13, 9);
        AddDot(pinkBrush, 63, 13, 9);

        // "M"
        var m = new Polyline
        {
            Stroke = pinkBrush,
            StrokeThickness = 3.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Points = new PointCollection
            {
                new Point(35, 31), new Point(38, 16), new Point(48, 25),
                new Point(58, 16), new Point(61, 31),
            },
        };
        Decoration.Children.Add(m);
    }

    private void AddRect(Brush fill, double x, double y, double w, double h)
    {
        var r = new Rectangle { Width = w, Height = h, Fill = fill };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        Decoration.Children.Add(r);
    }

    private void AddDot(Brush fill, double x, double y, double d)
    {
        var e = new Ellipse { Width = d, Height = d, Fill = fill };
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        Decoration.Children.Add(e);
    }

    private bool _isOpen;
    public bool IsOpen => _isOpen;

    /// <summary>열림/닫힘 설정. 뚜껑(버튼 포함)이 위로, 그릇이 아래로 갈라져 내부가 드러난다. 빛 효과 없음.</summary>
    public void SetOpen(bool open)
    {
        if (open == _isOpen) return;
        _isOpen = open;
        double top = open ? -32 : 0;    // 이전보다 약 30% 덜 열림
        double bottom = open ? 21 : 0;
        TopShift.BeginAnimation(TranslateTransform.YProperty, To(top, 0.24));
        BottomShift.BeginAnimation(TranslateTransform.YProperty, To(bottom, 0.24));
    }

    /// <summary>value 로 sec 초에 걸쳐 이동하고 그 값을 유지하는 애니메이션(EaseOut).</summary>
    private static DoubleAnimation To(double value, double sec) => new(value, new Duration(TimeSpan.FromSeconds(sec)))
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        FillBehavior = FillBehavior.HoldEnd,
    };
}
