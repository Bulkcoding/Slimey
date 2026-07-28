using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ThrowMe.Models;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace ThrowMe.Views;

/// <summary>
/// 모니터 최상단 중앙에 뜨는 점수판(클릭 통과·항상 위). 큰 숫자 하나.
/// 좌/우 골대 각각의 모니터에 하나씩 두며, 득점은 SlimeWindow 가 교차로 올린다
/// (좌측 골대가 먹히면 우측 점수판 +, 반대도 마찬가지).
/// </summary>
public partial class ScoreboardWindow : Window
{
    private double _scaleX = 1, _scaleY = 1;
    private readonly double _targetLeftPx, _targetTopPx, _panelWpx, _panelHpx;
    private TextBlock _num = null!;
    private ScaleTransform _pop = null!;

    #region Win32 클릭 통과
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    #endregion

    /// <param name="centerXFraction">모니터 내 가로 배치(0~1). 0.5=중앙. 단일 모니터에서 두 점수판을 좌/우로 벌릴 때 사용.</param>
    public ScoreboardWindow(Rect monitorArea, double centerXFraction, AppSettings settings)
    {
        InitializeComponent();

        double s = settings.SlimeSize;
        _panelWpx = s * 2.6;
        _panelHpx = s * 1.35;
        double left = monitorArea.Left + monitorArea.Width * centerXFraction - _panelWpx / 2.0;
        _targetLeftPx = Math.Clamp(left, monitorArea.Left, Math.Max(monitorArea.Left, monitorArea.Right - _panelWpx));
        _targetTopPx = monitorArea.Top + s * 0.22;

        Box.Child = BuildPanel(s);
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
        Left = _targetLeftPx / _scaleX;
        Top = _targetTopPx / _scaleY;
        Width = _panelWpx / _scaleX;
        Height = _panelHpx / _scaleY;
    }

    private FrameworkElement BuildPanel(double s)
    {
        var label = new TextBlock
        {
            Text = "SCORE",
            FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold,
            FontSize = s * 0.24,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x90)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, s * 0.04),
        };
        _num = new TextBlock
        {
            Text = "0",
            FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.Bold,
            FontSize = s * 0.78,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0x8A, 0x30)),
            HorizontalAlignment = HorizontalAlignment.Center,
            LineHeight = s * 0.78, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        _pop = new ScaleTransform(1, 1);
        _num.RenderTransform = _pop;
        _num.RenderTransformOrigin = new Point(0.5, 0.5);

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(label);
        stack.Children.Add(_num);

        return new Border
        {
            Width = _panelWpx, Height = _panelHpx,
            CornerRadius = new CornerRadius(s * 0.2),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x1C, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x22)),
            BorderThickness = new Thickness(s * 0.05),
            Child = stack,
        };
    }

    /// <summary>점수 갱신 + 살짝 팝 애니메이션.</summary>
    public void SetScore(int n)
    {
        _num.Text = n.ToString();
        var pop = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
        pop.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.35, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.08))));
        pop.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.28)))
        { EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        _pop.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        _pop.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }
}
