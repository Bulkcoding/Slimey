using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using ThrowMe.Effects;
using ThrowMe.Services;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace ThrowMe.Views;

/// <summary>
/// 타격 문구("Hit!" 등, 메이플식)를 그리는 투명·클릭 통과 오버레이.
/// 파티클 오버레이와 같은 "작은 창을 무리 중심으로 이동"하는 저비용 패턴.
/// </summary>
public partial class HitTextOverlayWindow : Window
{
    private readonly MonitorLayoutService _monitors;
    private double _dpiScaleX = 1.0, _dpiScaleY = 1.0;

    // 문구 하나를 그리는 시각 요소(외곽선 8겹 + 채움 1). 고정 박스(320x120)로 중앙 정렬.
    private sealed class Pop
    {
        public Grid Root = null!;
        public ScaleTransform Scale = null!;
        public TextBlock Fill = null!;
        public TextBlock[] Outline = null!;
        public string Text = "";
    }

    private readonly List<Pop> _pool = new(16);

    private const double BoxW = 320, BoxH = 120;

    /// <summary>따라다니는 창 크기(물리 px). 리사이즈는 레이어드 창에서 동기 스톨을 유발하므로 고정.
    /// 이 크기가 <see cref="HitTextSystem.MaxSpreadPx"/> 의 근거다(퍼짐 + 문구 박스가 안에 들어와야 함).</summary>
    private const double FollowSizePx = 1100.0;

    private double _originPxX, _originPxY;
    private readonly double _sizePxW = FollowSizePx, _sizePxH = FollowSizePx;

    #region Win32 클릭 통과
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr h, int i, int v);
    #endregion

    private static readonly FontFamily HitFont = new("Segoe UI Black, Arial Black, 맑은 고딕");

    public HitTextOverlayWindow(MonitorLayoutService monitors)
    {
        _monitors = monitors;
        InitializeComponent();
        _monitors.LayoutChanged += OnLayoutChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        UpdateDpiScale();
        ApplyBounds();
        Prewarm();
    }

    private void UpdateDpiScale()
    {
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
        {
            Matrix m = src.CompositionTarget.TransformToDevice;
            _dpiScaleX = m.M11 > 0 ? m.M11 : 1.0;
            _dpiScaleY = m.M22 > 0 ? m.M22 : 1.0;
        }
    }

    private void ApplyBounds()
    {
        Width = _sizePxW / _dpiScaleX;
        Height = _sizePxH / _dpiScaleY;
        Rect wa = _monitors.PrimaryWorkingArea;
        _originPxX = wa.Left + (wa.Width - _sizePxW) / 2.0;
        _originPxY = wa.Top + (wa.Height - _sizePxH) / 2.0;
        Left = _originPxX / _dpiScaleX;
        Top = _originPxY / _dpiScaleY;
    }

    private void OnLayoutChanged(object? sender, EventArgs e) { UpdateDpiScale(); ApplyBounds(); }

    private static readonly double[] OffX = { -2, 2, 0, 0, -2, 2, -2, 2 };
    private static readonly double[] OffY = { 0, 0, -2, 2, -2, -2, 2, 2 };

    private Pop CreatePop()
    {
        var box = new Grid { Width = BoxW, Height = BoxH, IsHitTestVisible = false,
                             RenderTransformOrigin = new System.Windows.Point(0.5, 0.5), Visibility = Visibility.Collapsed };
        var scale = new ScaleTransform(1, 1);
        box.RenderTransform = scale;

        var outline = new TextBlock[8];
        for (int k = 0; k < 8; k++)
        {
            var ol = new TextBlock
            {
                FontFamily = HitFont, FontWeight = FontWeights.Black, FontStyle = FontStyles.Italic,
                Foreground = System.Windows.Media.Brushes.Black,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(OffX[k], OffY[k], -OffX[k], -OffY[k]),
                IsHitTestVisible = false,
            };
            outline[k] = ol;
            box.Children.Add(ol);
        }
        var fill = new TextBlock
        {
            FontFamily = HitFont, FontWeight = FontWeights.Black, FontStyle = FontStyles.Italic,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        box.Children.Add(fill);

        TextCanvas.Children.Add(box);
        return new Pop { Root = box, Scale = scale, Fill = fill, Outline = outline };
    }

    private void Prewarm()
    {
        for (int i = _pool.Count; i < 12; i++) _pool.Add(CreatePop());
    }

    // 채움 색(스타일별): 흰/노랑/주황
    private static readonly System.Windows.Media.Brush[] Fills =
    {
        Freeze(Color.FromRgb(0xFF, 0xFF, 0xFF)),
        Freeze(Color.FromRgb(0xFF, 0xE2, 0x4D)),
        Freeze(Color.FromRgb(0xFF, 0x8A, 0x3D)),
    };
    private static System.Windows.Media.Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>렌더 루프에서 매 프레임 호출. 문구 무리 중심으로 창 이동 + 풀 갱신.</summary>
    /// <summary>직전 프레임에 그린 문구 수. 0 → 0 이면 건너뛴다(고속 이동 중 프레임 비용 절감).</summary>
    private int _lastRendered;

    public void Render(IReadOnlyList<HitText> items)
    {
        int n = items.Count;
        if (n == 0 && _lastRendered == 0) return;
        _lastRendered = n;

        if (n > 0)
        {
            // 창은 고정 크기를 유지하고(리사이즈는 레이어드 창에서 동기 스톨) 이동만 한다.
            // 무게중심이 아니라 **경계 중심**에 놓는다 — 무게중심은 문구가 한쪽에 몰릴 때
            // 치우쳐서 반대쪽 문구가 창 밖으로 잘린다.
            // "모든 활성 문구가 창 안에 들어온다"는 불변식은 HitTextSystem 이
            // 퍼짐 한계(MaxSpreadPx)를 넘는 순간 오래된 문구를 비워서 보장한다.
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                double x = items[i].CurrentX, y = items[i].CurrentY;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            _originPxX = (minX + maxX) / 2.0 - _sizePxW / 2.0;
            _originPxY = (minY + maxY) / 2.0 - _sizePxH / 2.0;
            Left = _originPxX / _dpiScaleX;
            Top = _originPxY / _dpiScaleY;
        }

        while (_pool.Count < n) _pool.Add(CreatePop());

        for (int i = 0; i < _pool.Count; i++)
        {
            Pop pop = _pool[i];
            if (i < n)
            {
                HitText h = items[i];
                if (pop.Text != h.Text)
                {
                    pop.Text = h.Text;
                    pop.Fill.Text = h.Text;
                    for (int k = 0; k < 8; k++) pop.Outline[k].Text = h.Text;
                }
                double fontSize = 24 + h.Power01 * 26;   // 24~50
                pop.Fill.FontSize = fontSize;
                for (int k = 0; k < 8; k++) pop.Outline[k].FontSize = fontSize;
                pop.Fill.Foreground = Fills[h.Style % Fills.Length];

                double s = h.Scale;
                pop.Scale.ScaleX = s; pop.Scale.ScaleY = s;
                pop.Root.Opacity = h.Opacity;

                double localX = (h.CurrentX - _originPxX) / _dpiScaleX - BoxW / 2.0;
                double localY = (h.CurrentY - _originPxY) / _dpiScaleY - BoxH / 2.0;
                Canvas.SetLeft(pop.Root, localX);
                Canvas.SetTop(pop.Root, localY);
                if (pop.Root.Visibility != Visibility.Visible) pop.Root.Visibility = Visibility.Visible;
            }
            else if (pop.Root.Visibility != Visibility.Collapsed)
            {
                pop.Root.Visibility = Visibility.Collapsed;
            }
        }
    }

    public void ShutdownCleanup() => _monitors.LayoutChanged -= OnLayoutChanged;
}
