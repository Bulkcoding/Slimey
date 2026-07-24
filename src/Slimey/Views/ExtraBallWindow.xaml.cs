using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Slimey.Models;
using Slimey.Physics;
using Slimey.Services;
using Slimey.Views.Skins;
using Color = System.Windows.Media.Color;

namespace Slimey.Views;

/// <summary>
/// 던져진 당구공(빨강/노랑 등) — 클릭 통과되는 가벼운 창. 물리/충돌은 SlimeWindow 의 당구 루프가 구동한다.
/// </summary>
public partial class ExtraBallWindow : Window
{
    private readonly AppSettings _settings;
    private double _scaleX = 1, _scaleY = 1;

    public SlimePhysicsEngine Physics { get; }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);

    public ExtraBallWindow(Color color, AppSettings settings, MonitorLayoutService monitors, Vector2 startPos, Vector2 startVel)
    {
        _settings = settings;
        InitializeComponent();
        Host.Content = new BilliardSkin(color);
        Width = settings.SlimeSize;
        Height = settings.SlimeSize;
        Physics = new SlimePhysicsEngine(settings, monitors) { Position = startPos, Velocity = startVel };
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
        Width = _settings.SlimeSize / _scaleX;
        Height = _settings.SlimeSize / _scaleY;
        ApplyPosition();
    }

    public void ApplyPosition()
    {
        Left = Physics.Position.X / _scaleX;
        Top = Physics.Position.Y / _scaleY;
    }
}
