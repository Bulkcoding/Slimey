using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Slimey.Models;
using Slimey.Physics;
using Slimey.Services;
using Slimey.Views.Skins;

namespace Slimey.Views;

/// <summary>
/// 볼링핀 — 클릭 통과되는 가벼운 창. 창 크기는 공과 동일(SlimeSize)이라 물리 엔진과 일관되며,
/// 핀은 그 안에 작게 그려진다. 물리/충돌은 SlimeWindow 의 볼링 루프가 구동한다.
/// </summary>
public partial class PinWindow : Window
{
    private readonly AppSettings _settings;
    private readonly PinSkin _skin;
    private double _scaleX = 1, _scaleY = 1;

    public SlimePhysicsEngine Physics { get; }

    /// <summary>이미 쓰러진 핀인가.</summary>
    public bool Knocked { get; private set; }

    /// <summary>세워졌을 때의 중심(물리 px) — 넘어짐/득점 판정 기준.</summary>
    public Vector2 StandCenter { get; }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);

    /// <summary>
    /// 창 한 변 = SlimeSize * BoxFactor. PinSkin 의 320 캔버스와 1:1 대응하며,
    /// 핀을 완전히 눕혀도 잘리지 않는 크기다.
    /// </summary>
    public const double BoxFactor = 10.0 / 3.0;

    /// <summary>원근 레인에서 핀이 공보다 과하게 커 보이지 않도록 적용하는 표시 배율.</summary>
    public const double VisualScale = 0.72;

    public PinWindow(AppSettings settings, MonitorLayoutService monitors, Vector2 startCenter)
    {
        _settings = settings;
        InitializeComponent();
        Host.Content = _skin = new PinSkin();
        ApplySizes(1, 1);

        double half = settings.SlimeSize / 2.0;
        StandCenter = startCenter;
        Physics = new SlimePhysicsEngine(settings, monitors)
        {
            Position = startCenter - new Vector2(half, half),
        };
    }

    /// <summary>
    /// 창 = 핀 크기의 (1+2*PadFactor)배. PinSkin 의 디자인 캔버스가 이 창 전체에 1:1 대응하며,
    /// 핀 밑동이 물리 박스(중앙 SlimeSize 영역) 하단에 놓인다.
    /// </summary>
    private void ApplySizes(double sx, double sy)
    {
        double box = _settings.SlimeSize * BoxFactor;
        Width = box / sx;
        Height = box / sy;
        if (Host != null)
        {
            Host.Width = box * VisualScale / sx;
            Host.Height = box * VisualScale / sy;
        }
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
        ApplySizes(_scaleX, _scaleY);
        ApplyPosition();
    }

    /// <summary>핀을 쓰러뜨린다(한 번만). dirSign: 넘어지는 방향(+오른쪽/-왼쪽).</summary>
    public void Knock(int dirSign)
    {
        if (Knocked) return;
        Knocked = true;
        _skin.SetKnocked(true, dirSign);
    }

    public void ApplyPosition()
    {
        // 창에 여백이 있으므로 그만큼 왼쪽/위로 당겨야 핀이 물리 위치와 일치한다.
        double pad = _settings.SlimeSize * (BoxFactor - 1) / 2.0;
        Left = (Physics.Position.X - pad) / _scaleX;
        Top = (Physics.Position.Y - pad) / _scaleY;
    }
}
