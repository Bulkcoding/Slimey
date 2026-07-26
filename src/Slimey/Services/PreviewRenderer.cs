using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Slimey.Models;
using Slimey.Views;
using Slimey.Views.Skins;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;

namespace Slimey.Services;

/// <summary>
/// 개발용 오프라인 렌더러. 농구공 스킨/골대를 창을 띄우지 않고 PNG로 저장한다.
/// 일반 실행 경로에는 영향 없음(커맨드라인 인자로만 진입).
/// </summary>
internal static class PreviewRenderer
{
    public static void Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var settings = new AppSettings { SlimeSize = 96 };

        // 농구공 기본 + 여러 바운스 무늬
        Save(Wrap(new BasketballSkin(), 220), 220, 220, Path.Combine(outDir, "ball_default.png"));
        for (int i = 0; i < 4; i++)
        {
            var b = new BasketballSkin();
            b.OnBounce();
            Save(Wrap(b, 220), 220, 220, Path.Combine(outDir, $"ball_{i}.png"));
        }

        // 골대(좌/우)
        SaveHoop(HoopSide.Left, settings, Path.Combine(outDir, "hoop_left.png"));
        SaveHoop(HoopSide.Right, settings, Path.Combine(outDir, "hoop_right.png"));
    }

    private static void SaveHoop(HoopSide side, AppSettings settings, string path)
    {
        var hoop = new HoopWindow(side, new Rect(0, 0, 1920, 1080), settings);
        Canvas root = hoop.PreviewRoot;
        int w = (int)root.Width, h = (int)root.Height;

        var host = new Border { Width = w, Height = h, Background = Gray };
        // Root 를 창에서 떼어내 host 에 넣는다(미리보기 전용).
        (root.Parent as System.Windows.Controls.Decorator)!.Child = null;
        host.Child = root;
        Save(host, w, h, path);
    }

    private static FrameworkElement Wrap(FrameworkElement child, int size)
    {
        return new Grid { Width = size, Height = size, Background = Gray, Children = { child } };
    }

    private static readonly Brush Gray = Freeze(Color.FromRgb(0x80, 0x80, 0x86));
    private static Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private static void Save(FrameworkElement fe, int w, int h, string path)
    {
        fe.Measure(new Size(w, h));
        fe.Arrange(new Rect(0, 0, w, h));
        fe.UpdateLayout();

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(fe);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
