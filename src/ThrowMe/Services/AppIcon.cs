using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using DrawingIcon = System.Drawing.Icon;
using SystemInformation = System.Windows.Forms.SystemInformation;

namespace ThrowMe.Services;

/// <summary>
/// 앱 아이콘(Resources/ThrowMe.ico)을 트레이·창에서 함께 쓰도록 로드해 준다.
///
/// 같은 .ico 가 두 경로로 들어간다:
///   - csproj 의 &lt;ApplicationIcon&gt; → exe 의 Win32 아이콘(탐색기·작업표시줄·바로가기)
///   - csproj 의 &lt;Resource&gt;        → 여기서 스트림으로 읽어 트레이·창에 지정
/// Win32 리소스는 WPF 에서 스트림으로 열 수 없어 이렇게 두 번 포함한다.
///
/// .ico 는 16~256px 프레임을 담고 있어, 트레이는 현재 DPI 에 맞는 크기를 골라 쓴다.
/// </summary>
public static class AppIcon
{
    private const string ResourcePath = "Resources/ThrowMe.ico";

    private static ImageSource? _windowIcon;
    private static bool _windowIconTried;

    /// <summary>.ico 리소스 스트림을 연다. 없으면 null.</summary>
    private static Stream? OpenStream()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri(ResourcePath, UriKind.Relative));
            return info?.Stream;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open icon resource '{ResourcePath}'.", ex);
            return null;
        }
    }

    /// <summary>
    /// 트레이용 아이콘. 현재 DPI 의 작은 아이콘 크기에 맞는 프레임을 고른다.
    /// 실패하면 null — 호출측이 대체 아이콘을 그린다.
    /// 반환된 Icon 은 호출측이 Dispose 한다.
    /// </summary>
    public static DrawingIcon? CreateTrayIcon()
    {
        try
        {
            using var stream = OpenStream();
            if (stream == null) return null;

            var size = SystemInformation.SmallIconSize; // 100% 에서 16px, 고DPI 에서 20/24px
            int w = size.Width > 0 ? size.Width : 16;
            int h = size.Height > 0 ? size.Height : 16;
            return new DrawingIcon(stream, w, h);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to create tray icon from resource.", ex);
            return null;
        }
    }

    /// <summary>창(작업표시줄·Alt+Tab)용 아이콘. 실패하면 null.</summary>
    public static ImageSource? WindowIcon
    {
        get
        {
            if (_windowIconTried) return _windowIcon;
            _windowIconTried = true;

            try
            {
                using var stream = OpenStream();
                if (stream == null) return null;

                // .ico 의 가장 큰 프레임을 쓴다. OnLoad 로 스트림을 잡아두지 않는다.
                var decoder = new IconBitmapDecoder(
                    stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

                BitmapFrame? best = null;
                foreach (var frame in decoder.Frames)
                    if (best == null || frame.PixelWidth > best.PixelWidth) best = frame;

                if (best != null && best.CanFreeze) best.Freeze();
                _windowIcon = best;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load window icon from resource.", ex);
            }

            return _windowIcon;
        }
    }

    /// <summary>창에 아이콘을 지정한다(리소스가 없으면 조용히 넘어감).</summary>
    public static void Apply(Window window)
    {
        var icon = WindowIcon;
        if (icon != null) window.Icon = icon;
    }
}
