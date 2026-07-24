using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace Slimey.Views.Skins;

/// <summary>색상 파라미터화된 반들반들한 당구공. 기본 흰색(수구). 빨강/노랑 등으로 생성 가능.</summary>
public partial class BilliardSkin : UserControl
{
    public static readonly Color Cue = Color.FromRgb(0xF2, 0xF2, 0xF5);   // 수구(흰색)
    public static readonly Color Red = Color.FromRgb(0xDC, 0x28, 0x2E);
    public static readonly Color Yellow = Color.FromRgb(0xF0, 0xC4, 0x1E);
    private static readonly Color EightBody = Color.FromRgb(0x1C, 0x1C, 0x22);

    /// <summary>기본: 검은 8번공(테마 당구공).</summary>
    public BilliardSkin()
    {
        InitializeComponent();
        Body.Fill = SphereBrush(EightBody);
        NumBadge.Visibility = System.Windows.Visibility.Visible;
        NumText.Visibility = System.Windows.Visibility.Visible;
    }

    /// <summary>단색 당구공(수구=흰색, 빨강/노랑 등). 번호 없음.</summary>
    public BilliardSkin(Color baseColor)
    {
        InitializeComponent();
        Body.Fill = SphereBrush(baseColor);
    }

    private static Brush SphereBrush(Color c)
    {
        var b = new RadialGradientBrush { GradientOrigin = new System.Windows.Point(0.35, 0.3), Center = new System.Windows.Point(0.5, 0.5), RadiusX = 0.72, RadiusY = 0.72 };
        b.GradientStops.Add(new GradientStop(Lighten(c, 0.6), 0.0));
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
