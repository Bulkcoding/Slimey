using System;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;

namespace ThrowMe.Views.Skins;

/// <summary>
/// 농구공 스킨. 주황 구체 + 검정 씸(seam) 무늬. 단단한 공(Rigid).
/// 정통 농구공 무늬(세로선 + 가로선 + 양옆 곡선)를 그리고,
/// 튈 때마다 <see cref="OnBounce"/> 로 전체 회전/곡선 폭을 임의로 바꿔
/// "공이 굴러 방향이 바뀐 것처럼" 매번 다른 모양을 보여 준다(참고 이미지의 9개 방향처럼).
/// </summary>
public partial class BasketballSkin : UserControl, ISkinBounce
{
    // 씸 좌표계: SeamHost(96 - 여백6*2 = 84) 안, 중심 (42,42), 반경 40.
    private const double C = 42.0;
    private const double R = 40.0;

    private static readonly Color BallOrange = Color.FromRgb(0xE0, 0x6C, 0x22);

    private readonly Random _rng = new();

    public BasketballSkin()
    {
        InitializeComponent();
        Body.Fill = SphereBrush(BallOrange);
        SeamHost.Clip = new EllipseGeometry(new Point(C, C), C, C); // 씸이 공 밖으로 삐져나오지 않게
        ApplySeams(rx: R * 0.52, angleDeg: 10);                     // 설정창 미리보기용 기본 정면
    }

    /// <summary>튈 때마다 무늬 변경(임의 회전 + 양옆 곡선 폭 변형 = 다른 방향에서 본 공).</summary>
    public void OnBounce()
    {
        double rx = R * (0.40 + _rng.NextDouble() * 0.34); // 0.40R~0.74R
        double angle = _rng.NextDouble() * 360.0;
        ApplySeams(rx, angle);
    }

    private void ApplySeams(double rx, double angleDeg)
    {
        Seams.Data = BuildSeams(rx);
        SeamRotate.Angle = angleDeg;
    }

    /// <summary>정통 농구공 무늬: 세로 대원 + 가로 대원 + 양옆으로 부푼 곡선(세로 타원).</summary>
    private static Geometry BuildSeams(double rx)
    {
        var g = new GeometryGroup();
        g.Children.Add(new LineGeometry(new Point(C, C - R), new Point(C, C + R)));  // 세로선(대원)
        g.Children.Add(new LineGeometry(new Point(C - R, C), new Point(C + R, C)));  // 가로선(대원)
        g.Children.Add(new EllipseGeometry(new Point(C, C), rx, R));                 // 양옆 두 곡선(경선)
        g.Freeze();
        return g;
    }

    private static Brush SphereBrush(Color c)
    {
        var b = new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.5, 0.5), RadiusX = 0.72, RadiusY = 0.72 };
        b.GradientStops.Add(new GradientStop(Lighten(c, 0.45), 0.0));
        b.GradientStops.Add(new GradientStop(c, 0.5));
        b.GradientStops.Add(new GradientStop(Darken(c, 0.28), 0.85));
        b.GradientStops.Add(new GradientStop(Darken(c, 0.48), 1.0));
        b.Freeze();
        return b;
    }

    private static Color Lighten(Color c, double f) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
    private static Color Darken(Color c, double f) => Color.FromRgb(
        (byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));
}
