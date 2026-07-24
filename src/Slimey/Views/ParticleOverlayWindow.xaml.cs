using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Slimey.Effects;
using Slimey.Models;
using Slimey.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace Slimey.Views;

/// <summary>
/// 모든 모니터를 덮는 투명·클릭 통과 오버레이. 파티클 등 시각 효과를 렌더한다.
/// 좌표는 물리 픽셀 → (VirtualBounds 원점 기준 / DPI 배율)로 로컬 DIP 변환.
///
/// 파티클 비주얼(색·모양)은 여기서만 정의하므로 디자인 트랙이 자유롭게 교체 가능.
/// </summary>
public partial class ParticleOverlayWindow : Window
{
    private readonly AppSettings _settings;
    private readonly MonitorLayoutService _monitors;

    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    // Ellipse 풀(GC 부담 최소화)
    private readonly List<Ellipse> _pool = new(64);

    #region Win32 클릭 통과
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    #endregion

    public ParticleOverlayWindow(AppSettings settings, MonitorLayoutService monitors)
    {
        _settings = settings;
        _monitors = monitors;
        InitializeComponent();
        _monitors.LayoutChanged += OnLayoutChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 클릭 통과 + 비활성 창으로 설정
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE,
            ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        UpdateDpiScale();
        ApplyBounds();
        PrewarmPool();
    }

    /// <summary>
    /// 첫 충돌 때 Ellipse 를 만들며 비주얼 트리를 건드리면 히칭이 생기므로
    /// 최대 개수만큼 미리 만들어 두고 숨겨 둔다.
    /// </summary>
    private void PrewarmPool()
    {
        int warm = Math.Max(_settings.ParticleMaxCount, 8);
        for (int i = _pool.Count; i < warm; i++)
        {
            var el = new Ellipse { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            _pool.Add(el);
            ParticleCanvas.Children.Add(el);
        }
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

    // 오버레이는 전체 데스크톱이 아니라 "작은 창"으로 두고 파티클 무리를 따라다닌다.
    // (AllowsTransparency 창의 소프트웨어 합성 비용은 면적에 비례하며, 작은 창을
    //  매 프레임 '이동'하는 것은 DWM 가 저렴하게 처리한다. 실측으로 확정.)
    private const double FollowSizePx = 1300.0;

    // 현재 창 좌상단(물리 픽셀). 파티클 좌표 변환 기준.
    private double _originPxX, _originPxY;

    private void ApplyBounds()
    {
        Width = FollowSizePx / _dpiScaleX;
        Height = FollowSizePx / _dpiScaleY;

        // 초기 위치: 주 모니터 중앙(첫 파티클 방출 시 무리 중심으로 재배치됨).
        Rect wa = _monitors.PrimaryWorkingArea;
        _originPxX = wa.Left + (wa.Width - FollowSizePx) / 2.0;
        _originPxY = wa.Top + (wa.Height - FollowSizePx) / 2.0;
        Left = _originPxX / _dpiScaleX;
        Top = _originPxY / _dpiScaleY;
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        UpdateDpiScale();
        ApplyBounds();
    }

    /// <summary>
    /// 파티클 목록을 화면에 반영. 렌더 루프에서 매 프레임 호출.
    ///
    /// [성능] Show/Hide/Resize 는 레이어드 창에서 동기 스톨(~100ms)을 유발하므로
    /// 절대 하지 않는다. 창은 작은 고정 크기(FollowSizePx)로 지속 표시하고,
    /// 파티클이 있을 때만 무리 중심으로 Left/Top 을 '이동'(저비용)시킨다.
    /// 좌표는 현재 창 좌상단(_originPx*) 기준 로컬 DIP.
    /// </summary>
    public void Render(IReadOnlyList<Particle> particles)
    {
        int n = particles.Count;

        if (n > 0)
        {
            // 파티클 무리 중심 계산 → 창을 그 위치로 이동(이동만, 리사이즈/Show 없음)
            double cx = 0, cy = 0;
            for (int i = 0; i < n; i++)
            {
                cx += particles[i].Position.X;
                cy += particles[i].Position.Y;
            }
            cx /= n; cy /= n;

            _originPxX = cx - FollowSizePx / 2.0;
            _originPxY = cy - FollowSizePx / 2.0;
            Left = _originPxX / _dpiScaleX;
            Top = _originPxY / _dpiScaleY;
        }

        // 풀 보정(프리워밍으로 대개 불필요)
        while (_pool.Count < n)
        {
            var el = new Ellipse { IsHitTestVisible = false };
            _pool.Add(el);
            ParticleCanvas.Children.Add(el);
        }

        for (int i = 0; i < _pool.Count; i++)
        {
            Ellipse el = _pool[i];
            if (i < n)
            {
                Particle p = particles[i];
                double sizeDip = p.Size / _dpiScaleX;
                el.Width = sizeDip;
                el.Height = sizeDip;
                el.Fill = p.Spark ? SparkBrush : BrushFor(p.Tier);
                el.Opacity = p.LifeFraction;

                double localX = (p.Position.X - _originPxX) / _dpiScaleX - sizeDip / 2;
                double localY = (p.Position.Y - _originPxY) / _dpiScaleY - sizeDip / 2;
                Canvas.SetLeft(el, localX);
                Canvas.SetTop(el, localY);
                el.Visibility = Visibility.Visible;
            }
            else if (el.Visibility != Visibility.Collapsed)
            {
                el.Visibility = Visibility.Collapsed;
            }
        }
    }

    // 단계별 색(디자인 트랙에서 교체 가능한 임시 팔레트)
    private static readonly Brush BoingBrush = Freeze(Color.FromRgb(0x8C, 0xF5, 0xD8)); // 민트
    private static readonly Brush SplatBrush = Freeze(Color.FromRgb(0xB5, 0x7E, 0xDC)); // 보라
    private static readonly Brush BonkBrush = Freeze(Color.FromRgb(0xFF, 0x9E, 0xC4)); // 핑크
    private static readonly Brush SparkBrush = Freeze(Color.FromRgb(0xFF, 0xFF, 0xF2)); // 당구 쿠션 스파크(밝은 아이보리)

    private static Brush BrushFor(ImpactTier tier) => tier switch
    {
        ImpactTier.Bonk => BonkBrush,
        ImpactTier.Splat => SplatBrush,
        _ => BoingBrush,
    };

    private static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public void ShutdownCleanup()
    {
        _monitors.LayoutChanged -= OnLayoutChanged;
    }
}
