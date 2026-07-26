using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Slimey.Physics;
using Slimey.Services;

namespace Slimey.Views;

/// <summary>큐대 조준 오버레이(전 데스크톱, 클릭 통과). 큐대 선 + 조준 가이드를 그린다.</summary>
public partial class AimOverlayWindow : Window
{
    private readonly MonitorLayoutService _monitors;
    private double _scaleX = 1, _scaleY = 1;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);

    public AimOverlayWindow(MonitorLayoutService monitors)
    {
        _monitors = monitors;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
        {
            Matrix m = src.CompositionTarget.TransformToDevice;
            _scaleX = m.M11 > 0 ? m.M11 : 1; _scaleY = m.M22 > 0 ? m.M22 : 1;
        }
        var vb = _monitors.VirtualBounds;
        Left = vb.Left / _scaleX; Top = vb.Top / _scaleY;
        Width = vb.Width / _scaleX; Height = vb.Height / _scaleY;
    }

    /// <summary>큐대 표시 여부. 농구 조준에서는 큐대를 숨기고 유도선만 보여 준다.</summary>
    public void SetCueVisible(bool visible) => Cue.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>큐/직선 가이드를 숨기고 포물선 궤적만 쓰는 모드(농구 조준).</summary>
    public void SetArcMode(bool arc)
    {
        Cue.Visibility = arc ? Visibility.Collapsed : Visibility.Visible;
        GuideLine.Visibility = arc ? Visibility.Collapsed : Visibility.Visible;
        Arc.Visibility = arc ? Visibility.Visible : Visibility.Collapsed;
        ArcTip.Visibility = arc ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 포물선 궤적 갱신. 물리 엔진과 <b>동일한 적분 순서</b>(중력 → 마찰 감쇠 → 이동)로 시뮬레이션하므로
    /// 그려진 선이 공이 실제로 지나는 경로와 일치한다.
    /// 경로는 시작점부터 <paramref name="showLenPx"/> 길이만큼만 표시한다(앞부분 짧게).
    /// </summary>
    public void UpdateArc(Vector2 start, Vector2 vel, double gravityY, double friction, double showLenPx)
    {
        var vb = _monitors.VirtualBounds;
        double sx = _scaleX <= 0 ? 1 : _scaleX;
        System.Windows.Point L(Vector2 p) => new((p.X - vb.Left) / sx, (p.Y - vb.Top) / _scaleY);

        var pts = new System.Windows.Media.PointCollection();
        Vector2 p0 = start, v0 = vel;
        const double dt = 1.0 / 240.0;   // 세밀하게 적분(엔진 substep 과 동등)
        const int maxSteps = 2000;
        double travelled = 0;

        for (int i = 0; i < maxSteps; i++)
        {
            pts.Add(L(p0));
            // 엔진과 같은 순서: 중력 → 마찰(지수 감쇠) → 위치 적분
            v0 = new Vector2(v0.X, v0.Y + gravityY * dt);
            v0 *= Math.Exp(-friction * dt);
            Vector2 step = v0 * dt;
            p0 += step;
            travelled += step.Length;
            if (travelled >= showLenPx) { pts.Add(L(p0)); break; }
            if (p0.Y > vb.Bottom || p0.X < vb.Left - 200 || p0.X > vb.Right + 200) break;
        }
        Arc.Points = pts;

        if (pts.Count > 0)
        {
            var tip = pts[pts.Count - 1];
            Canvas.SetLeft(ArcTip, tip.X - ArcTip.Width / 2);
            Canvas.SetTop(ArcTip, tip.Y - ArcTip.Height / 2);
        }
    }

    private const double CueLenPx = 320;   // 큐대 고정 길이(물리 px)
    private const double PullVisualMax = 240; // 시각적으로 뒤로 빠지는 최대(px)

    /// <summary>
    /// 조준 갱신. 좌표는 물리 픽셀. dir=발사 방향(단위, 공이 나갈 방향), pull=당긴 거리(px).
    /// 큐대는 늘어나지 않고 고정 길이로 공 반대편(커서쪽)에서 pull 만큼 뒤로 물러난다.
    /// </summary>
    public void UpdateAim(Vector2 ballCenter, Vector2 cursor, Vector2 dir, double power01, double radius, double pull)
    {
        var vb = _monitors.VirtualBounds;
        double s = _scaleX <= 0 ? 1 : _scaleX;
        System.Windows.Point L(Vector2 p) => new((p.X - vb.Left) / s, (p.Y - vb.Top) / _scaleY);

        double pullVis = Math.Min(pull, PullVisualMax);
        // 큐 팁: 공 표면에서 pull 만큼 뒤로. 밑동: 팁에서 고정 길이만큼 더 뒤로.
        Vector2 tip = ballCenter - dir * (radius + pullVis);
        Vector2 butt = tip - dir * CueLenPx;
        Vector2 perp = new(-dir.Y, dir.X);

        var tipL = L(tip); var buttL = L(butt);
        double tipH = 3.0 / s, buttH = 8.5 / s;
        System.Windows.Point Off(System.Windows.Point c, double h, int sign)
            => new(c.X + perp.X * h * sign, c.Y + perp.Y * h * sign);

        Cue.Points = new System.Windows.Media.PointCollection
        {
            Off(tipL, tipH, +1), Off(tipL, tipH, -1),
            Off(buttL, buttH, -1), Off(buttL, buttH, +1),
        };
        CueBrush.StartPoint = buttL;
        CueBrush.EndPoint = tipL;

        double guideLen = 40 + power01 * 240;
        Vector2 gEnd = ballCenter + dir * guideLen;
        var bcL = L(ballCenter); var geL = L(gEnd);
        GuideLine.X1 = bcL.X; GuideLine.Y1 = bcL.Y;
        GuideLine.X2 = geL.X; GuideLine.Y2 = geL.Y;
    }
}
