using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using Slimey.Animation;
using Slimey.Effects;
using Slimey.Models;
using Slimey.Physics;
using Slimey.Services;
using Slimey.Views.Skins;
using WinFormsCursor = System.Windows.Forms.Cursor;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Application = System.Windows.Application;
using Ellipse = System.Windows.Shapes.Ellipse;
using Rectangle = System.Windows.Shapes.Rectangle;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Canvas = System.Windows.Controls.Canvas;

namespace Slimey.Views;

/// <summary>
/// 투명 슬라임 표시 + 마우스 입력 수집 + 렌더 루프(물리·애니메이션 tick).
/// 좌표 처리: 물리는 물리 스크린 픽셀, 창 배치 시 DPI 배율로 DIP 변환.
/// </summary>
public partial class SlimeWindow : Window
{
    private readonly AppSettings _settings;
    private readonly MonitorLayoutService _monitors;
    private readonly SlimePhysicsEngine _physics;
    private readonly ThrowInputTracker _tracker;
    private SlimeAnimationController _animation = null!;

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    private bool _isDragging;
    private Vector2 _dragOffset;          // 커서 - 슬라임 위치 (물리 px)
    private Vector2 _pressCursor;         // 버튼 누른 순간 커서 (클릭/드래그 판정)
    private double _grabbedSpeed;         // 잡는 순간의 속도(px/s) — 낚아채기/펀치 판정
    private bool _ballWasOpen;            // 잡는 순간 볼이 열려 있었는가(클릭 토글 기준)

    // 스핀 이펙트 요소(코드 생성)
    private readonly List<Ellipse> _sparkles = new();
    private readonly List<double> _sparklePhase = new();

    // 스핀: 드래그 곡선으로 각속도 충전
    private double _dragSpin;             // 충전 중 각속도(deg/s)
    private Vector2 _prevDragDir;         // 직전 드래그 진행 방향
    private Vector2 _lastDragCursor;      // 직전 스핀 샘플 커서
    private double _lastDragTime;         // 직전 스핀 샘플 시각

    private bool _renderingActive;
    private double _lastFrameTime;

    // 표정 상태 (요청: 속도/충돌에 따른 표정 변경)
    private SlimeExpression _expression = SlimeExpression.Normal;
    private double _dizzyUntil;                        // 이 시각(초)까지 Dizzy 유지
    private const double FlyingSpeedFraction = 0.28;   // MaxSpeed 대비 이 이상이면 Flying
    private const double DizzyDurationSeconds = 0.9;    // 강한 충돌 후 Dizzy 지속
    private const double DizzyImpactFraction = 0.55;    // MaxSpeed 대비 이 이상 충돌이면 Dizzy

    // Phase 4: 타격감(효과음·파티클). 파티클 렌더는 전 모니터 오버레이가 담당.
    private readonly AudioService _audio;
    private readonly ParticleSystem _particles;
    private ParticleOverlayWindow? _overlay;

    // 타격 문구("Hit!" 등, 메이플식) — 젤리 스킨 전용
    private readonly HitTextSystem _hitText;
    private HitTextOverlayWindow? _hitTextOverlay;

    public SlimeWindow(AppSettings settings, MonitorLayoutService monitors)
    {
        _settings = settings;
        _monitors = monitors;

        InitializeComponent();

        Topmost = _settings.AlwaysOnTop;
        _settings.PropertyChanged += OnSettingsChanged;
        ApplySkin();
        BuildSpinFx();

        _tracker = new ThrowInputTracker(_settings);
        // 충돌 판정은 MonitorLayoutService(IWalkableArea)에 위임 → 멀티 모니터 대응.
        _physics = new SlimePhysicsEngine(_settings, _monitors);

        _audio = new AudioService(_settings);
        _particles = new ParticleSystem(_settings);
        _hitText = new HitTextSystem();

        // 입력 이벤트
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;

        _monitors.LayoutChanged += OnMonitorLayoutChanged;
    }

    private double Now => _clock.Elapsed.TotalSeconds;

    // ── 초기화 ──────────────────────────────────────────────
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UpdateDpiScale();
        ApplyWindowSize();
        ResetPositionToCenter();
        ApplyWindowPosition();

        // 파티클/효과 렌더용 클릭 통과 오버레이(전 모니터). 입력을 막지 않는다.
        _overlay = new ParticleOverlayWindow(_settings, _monitors);
        _overlay.Show();

        _hitTextOverlay = new HitTextOverlayWindow(_monitors);
        _hitTextOverlay.Show();

        // 전역 잡기 단축키 등록
        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);
        RegisterCatchHotkey();

        // 시작은 정지 상태이므로 렌더 루프를 돌리지 않는다(CPU 절감).
    }

    // ── 전역 잡기 단축키 ────────────────────────────────────
    private const int WM_HOTKEY = 0x0312;
    private const int CatchHotkeyId = 0xB001;
    private const uint MOD_NOREPEAT = 0x4000;
    private IntPtr _hwnd;
    private HwndSource? _hwndSource;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ── 마우스 버튼 트리거(수정자+클릭) : 전역 저수준 마우스 훅 ──
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelMouseProc? _mouseProc;
    private IntPtr _mouseHook;

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc cb, IntPtr hMod, uint thread);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

    private void RegisterCatchHotkey()
    {
        if (_hwnd == IntPtr.Zero) return;
        // 키보드 트리거
        UnregisterHotKey(_hwnd, CatchHotkeyId);
        if (_settings.CatchHotkeyVk != 0)
            RegisterHotKey(_hwnd, CatchHotkeyId, (uint)_settings.CatchHotkeyMod | MOD_NOREPEAT, (uint)_settings.CatchHotkeyVk);

        // 마우스 트리거
        RemoveMouseTrigger();
        if (_settings.CatchHotkeyMouse != 0)
        {
            _mouseProc = MouseHookProc;
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
        }
    }

    private void RemoveMouseTrigger()
    {
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        _mouseProc = null;
    }

    private bool ModifiersHeld()
    {
        int m = _settings.CatchHotkeyMod;
        bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        if ((m & 2) != 0 && !Down(0x11)) return false; // Ctrl
        if ((m & 4) != 0 && !Down(0x10)) return false; // Shift
        if ((m & 1) != 0 && !Down(0x12)) return false; // Alt
        if ((m & 8) != 0 && !(Down(0x5B) || Down(0x5C))) return false; // Win
        return true;
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            int wantMsg = _settings.CatchHotkeyMouse switch { 2 => WM_RBUTTONDOWN, 3 => WM_MBUTTONDOWN, _ => WM_LBUTTONDOWN };
            if (msg == wantMsg && ModifiersHeld())
            {
                try { CatchToCursor(); } catch { }
                return (IntPtr)1; // 삼킴(다른 창으로 전달 안 함)
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == CatchHotkeyId)
        {
            CatchToCursor();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>단축키: 슬라임을 마우스 커서 위치로 데려와 정지(잡힘). 빠르게 날아가도 즉시 회수.</summary>
    private void CatchToCursor()
    {
        if (!IsVisible) { _settings.SlimeVisible = true; }
        Vector2 cursor = CursorPhysical();
        double half = _settings.SlimeSize / 2.0;
        _isDragging = false;
        _physics.Velocity = Vector2.Zero;
        _physics.AngularVelocity = 0;
        _physics.SurfaceSpin = 0;
        _dragSpin = 0;
        _physics.SetPositionClamped(cursor - new Vector2(half, half));
        _animation.OnImpact(_settings.MaxSpeed * 0.25); // 잡히는 작은 반응
        ApplyWindowPosition();
        EnsureRendering();
    }

    private void UpdateDpiScale()
    {
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
        {
            Matrix m = src.CompositionTarget.TransformToDevice; // DIP → device(px)
            _dpiScaleX = m.M11 > 0 ? m.M11 : 1.0;
            _dpiScaleY = m.M22 > 0 ? m.M22 : 1.0;
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _dpiScaleX = newDpi.DpiScaleX > 0 ? newDpi.DpiScaleX : 1.0;
        _dpiScaleY = newDpi.DpiScaleY > 0 ? newDpi.DpiScaleY : 1.0;
        ApplyWindowSize();
        ApplyWindowPosition();
    }

    // 창은 슬라임의 4배(양쪽 1.5*SlimeSize 패딩). 넓은 클릭 영역 → 빠른 슬라임도 근처 클릭으로 낚아채기.
    private double EffectPadPx => _settings.SlimeSize * 1.5;

    private void ApplyWindowSize()
    {
        double s = _settings.SlimeSize;
        // 창(넓은 잡기 영역) + 스핀 이펙트 박스(2.5배, 중앙 고정) + 중앙 슬라임 크기.
        Width = 4.0 * s / _dpiScaleX;
        Height = 4.0 * s / _dpiScaleY;
        SpinFxBox.Width = 2.5 * s / _dpiScaleX;
        SpinFxBox.Height = 2.5 * s / _dpiScaleY;
        SlimeBox.Width = s / _dpiScaleX;
        SlimeBox.Height = s / _dpiScaleY;
        // 스핀 조준 원(공의 66% 크기)
        SpinAim.Width = 0.66 * s / _dpiScaleX;
        SpinAim.Height = 0.66 * s / _dpiScaleY;
        SpinDotShift.X = _spinOffset.X * (SpinAim.Width / 2.0);
        SpinDotShift.Y = _spinOffset.Y * (SpinAim.Height / 2.0);

        // 애니메이션 컨트롤러는 XAML Transform 이 준비된 뒤 1회 생성.
        _animation ??= new SlimeAnimationController(SlimeScale, SlimeRotate, _settings);
        UpdateSkinBehavior();
    }

    /// <summary>스킨별 물리/애니메이션 동작 반영(당구공은 찌그러지지 않음).</summary>
    private void UpdateSkinBehavior()
    {
        if (_animation != null)
            _animation.Rigid = _settings.Skin != SlimeSkinKind.Jelly; // 젤리만 말랑, 나머지는 단단
    }

    private void ApplyWindowPosition()
    {
        // 물리 위치(슬라임 top-left)에서 패딩만큼 빼서 창 배치 → 슬라임은 화면상 Position 에 위치.
        double pad = EffectPadPx;
        Left = (_physics.Position.X - pad) / _dpiScaleX;
        Top = (_physics.Position.Y - pad) / _dpiScaleY;
    }

    /// <summary>스핀 이펙트 요소를 코드로 구성(불규칙 반짝이만; 좌우 블러 없음).</summary>
    private void BuildSpinFx()
    {
        var rnd = new Random(20240724);

        // 반짝이: 불규칙 위치/크기, 시간 기반 트윈클(렌더 루프에서 갱신 → 유휴 시 비용 없음)
        const int sparks = 10;
        for (int i = 0; i < sparks; i++)
        {
            double ang = rnd.NextDouble() * Math.PI * 2;
            double r = 56 + rnd.NextDouble() * 32;
            double size = 6 + rnd.NextDouble() * 9;
            var e = new Ellipse { Width = size, Height = size, Fill = SparkFill(), IsHitTestVisible = false };
            Canvas.SetLeft(e, 120 + r * Math.Cos(ang) - size / 2);
            Canvas.SetTop(e, 120 + r * Math.Sin(ang) - size / 2);
            SparkLayer.Children.Add(e);
            _sparkles.Add(e);
            _sparklePhase.Add(rnd.NextDouble() * Math.PI * 2);
        }
    }

    private static Brush SparkFill()
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 255), 0.0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xEE, 255, 0xEC, 0x8A), 0.35));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xCC, 255, 0xD2, 0x3A), 0.62));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 0xD2, 0x3A), 1.0));
        b.Freeze();
        return b;
    }

    /// <summary>스핀 이펙트 갱신: 각속도로 세기, 스핀각으로 궤도 회전, 시간으로 반짝임.</summary>
    private void UpdateSpinFx()
    {
        double mag = Math.Abs(_physics.AngularVelocity);
        double denom = Math.Max(1.0, _settings.MaxAngularVelocity - _settings.SpinFxMinAngular);
        double intensity = Math.Clamp((mag - _settings.SpinFxMinAngular) / denom, 0, 1);
        SpinFx.Opacity = intensity;
        if (intensity <= 0) return;

        // 반짝이는 스핀 각도를 그대로 따라가지 않고, 완만한 고정 속도로 자체 회전 + 트윈클.
        double t = Now;
        SparkRotate.Angle = t * 22.0; // 초당 22도(스핀 세기·방향과 무관)
        for (int i = 0; i < _sparkles.Count; i++)
        {
            double tw = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin(t * (3.0 + i * 0.5) + _sparklePhase[i]));
            _sparkles[i].Opacity = tw;
        }
    }

    private void ResetPositionToCenter()
    {
        var wa = _monitors.PrimaryWorkingArea;
        double x = wa.Left + (wa.Width - _settings.SlimeSize) / 2.0;
        double y = wa.Top + (wa.Height - _settings.SlimeSize) / 2.0;
        _physics.Velocity = Vector2.Zero;
        _physics.Position = new Vector2(x, y);
        _animation?.ResetToRest();
    }

    // ── 입력 ────────────────────────────────────────────────
    private static Vector2 CursorPhysical()
    {
        var p = WinFormsCursor.Position; // 물리 픽셀(PerMonitorV2)
        return new Vector2(p.X, p.Y);
    }

    private bool IsCueMode => _settings.CueStickMode && _settings.Skin == SlimeSkinKind.Billiard;

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var c = CursorPhysical();
        if (IsCueMode)
        {
            if (InSpinCircle(c)) BeginSpinDrag(c); // 공 안쪽 클릭 → 스핀 점 이동
            else BeginAim(c);                       // 공 주위 클릭 → 큐대 조준
        }
        else BeginGrab(c);
    }
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var c = CursorPhysical();
        if (_spinDragging) SetSpinFromCursor(c);
        else if (_aiming) UpdateAim(c);
        else if (_isDragging) DragTo(c);
    }
    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var c = CursorPhysical();
        if (_spinDragging) { _spinDragging = false; try { ReleaseMouseCapture(); } catch { } }
        else if (_aiming) ReleaseAim(c);
        else if (_isDragging) ReleaseGrab(c);
    }

    // ── 스핀 조준 점(큐대 모드) ──────────────────────────────
    private bool _spinDragging;
    private Vector2 _spinOffset; // -1~1 정규화(공 중심 기준)

    private double SpinRadiusPx => _settings.SlimeSize * 0.33;

    private bool InSpinCircle(Vector2 cursor)
    {
        double half = _settings.SlimeSize / 2.0;
        Vector2 ballCenter = _physics.Position + new Vector2(half, half);
        return (cursor - ballCenter).Length <= SpinRadiusPx;
    }

    private void BeginSpinDrag(Vector2 cursor)
    {
        _spinDragging = true;
        try { CaptureMouse(); } catch { }
        SetSpinFromCursor(cursor);
    }

    private void SetSpinFromCursor(Vector2 cursor)
    {
        double half = _settings.SlimeSize / 2.0;
        Vector2 ballCenter = _physics.Position + new Vector2(half, half);
        Vector2 off = (cursor - ballCenter) / SpinRadiusPx;
        if (off.Length > 1) off = off.Normalized();
        _spinOffset = off;
        double dipR = SpinAim.Width / 2.0;
        if (double.IsNaN(dipR) || dipR <= 0) dipR = _settings.SlimeSize * 0.33 / _dpiScaleX;
        SpinDotShift.X = off.X * dipR;
        SpinDotShift.Y = off.Y * dipR;
    }

    private void UpdateSpinAimVisibility()
    {
        SpinAim.Visibility = IsCueMode ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── 큐대 조준(당구공 전용) ──────────────────────────────
    private bool _aiming;
    private AimOverlayWindow? _aimOverlay;

    private void BeginAim(Vector2 cursor)
    {
        _aiming = true;
        _physics.Velocity = Vector2.Zero;
        _physics.AngularVelocity = 0;
        _physics.SurfaceSpin = 0;
        try { CaptureMouse(); } catch { }
        _aimOverlay ??= new AimOverlayWindow(_monitors);
        _aimOverlay.Show();
        UpdateAim(cursor);
    }

    private (Vector2 dir, double power) AimParams(Vector2 cursor)
    {
        double half = _settings.SlimeSize / 2.0;
        Vector2 ballCenter = _physics.Position + new Vector2(half, half);
        Vector2 toBall = ballCenter - cursor;
        double dist = toBall.Length;
        Vector2 dir = dist > 1e-3 ? toBall / dist : new Vector2(1, 0);
        double pull = Math.Max(0, dist - _settings.SlimeSize * 0.44);
        double power = Math.Min(pull * _settings.CuePowerScale, _settings.MaxThrowSpeed);
        return (dir, power);
    }

    private void UpdateAim(Vector2 cursor)
    {
        double half = _settings.SlimeSize / 2.0;
        Vector2 ballCenter = _physics.Position + new Vector2(half, half);
        double radius = _settings.SlimeSize * 0.44;
        var (dir, power) = AimParams(cursor);
        double pull = Math.Max(0, (ballCenter - cursor).Length - radius);
        _aimOverlay?.UpdateAim(ballCenter, cursor, dir, Math.Clamp(power / _settings.MaxThrowSpeed, 0, 1), radius, pull);
    }

    private void ReleaseAim(Vector2 cursor)
    {
        _aiming = false;
        try { ReleaseMouseCapture(); } catch { }
        _aimOverlay?.Hide();
        var (dir, power) = AimParams(cursor);
        if (power > 60) // 최소 파워 이상일 때만 발사
        {
            _physics.Velocity = dir * power;
            // 세로 점 = 끌어치기/밀어치기(표면 스핀): 12시(위,-y)=밀어치기(전진), 6시(아래,+y)=끌어치기(되돌아옴)
            _physics.SpinShotDir = dir;
            _physics.SurfaceSpin = -_spinOffset.Y * power;
            // 가로 점 = 사이드 스핀(마그누스로 옆으로 휨): 3시(+x)/9시(-x)
            _physics.AngularVelocity = _spinOffset.X * _settings.MaxAngularVelocity;
            EnsureRendering();
        }
    }

    /// <summary>지정 커서(물리 px)에서 슬라임을 잡는다. WPF 클릭·전역 훅 공용.</summary>
    private void BeginGrab(Vector2 cursor)
    {
        _isDragging = true;
        _pressCursor = cursor;
        _dragOffset = cursor - _physics.Position;
        // 잡는 순간 속도/볼 열림 상태 기록(놓을 때 판정). 잡으면 즉시 정지.
        _grabbedSpeed = _physics.Velocity.Length;
        _ballWasOpen = SkinHost.Content is ISkinClickEffect b && b.IsOpen;
        _physics.Velocity = Vector2.Zero;
        // 잡으면 회전 충전 초기화(스핀도 잡아 멈춤)
        _physics.AngularVelocity = 0;
        _physics.SurfaceSpin = 0;
        _dragSpin = 0;
        _prevDragDir = Vector2.Zero;
        _lastDragCursor = cursor;
        _lastDragTime = Now;
        _tracker.Reset();
        _tracker.AddSample(cursor, Now);
        try { CaptureMouse(); } catch { /* 훅 경로에서는 무시 */ }
        EnsureRendering();
    }

    private void DragTo(Vector2 cursor)
    {
        double now = Now;

        // 드래그 곡선(curl)으로 스핀을 "충전"한다(관성). 한 방향으로 계속 돌리면
        // 스핀이 쌓여 유지되고, 직선 구간에서도 급히 사라지지 않는다.
        double dtm = now - _lastDragTime;
        Vector2 delta = cursor - _lastDragCursor;
        if (dtm > 1e-4 && delta.Length > 1.0)
        {
            Vector2 dir = delta.Normalized();
            if (_prevDragDir.LengthSquared > 1e-6)
            {
                double cross = _prevDragDir.X * dir.Y - _prevDragDir.Y * dir.X;
                double dot = _prevDragDir.X * dir.X + _prevDragDir.Y * dir.Y;
                double turnedDeg = Math.Atan2(cross, dot) * (180.0 / Math.PI); // 이번 샘플에서 꺾인 각(부호)
                _dragSpin += turnedDeg * _settings.SpinChargeGain;             // 누적(관성). 드래그 중엔 감쇠하지 않아 유지된다(던져야 소모).
                _dragSpin = Math.Clamp(_dragSpin, -_settings.MaxAngularVelocity, _settings.MaxAngularVelocity);
                _physics.AngularVelocity = _dragSpin;
            }
            _prevDragDir = dir;
            _lastDragCursor = cursor;
            _lastDragTime = now;
        }

        _physics.SetPositionClamped(cursor - _dragOffset);
        _tracker.AddSample(cursor, now);
        ApplyWindowPosition();
    }

    private void ReleaseGrab(Vector2 cursor)
    {
        _isDragging = false;
        try { ReleaseMouseCapture(); } catch { }

        double moved = (cursor - _pressCursor).Length;

        switch (ClassifyRelease(moved, _grabbedSpeed))
        {
            case ReleaseAction.Throw:
                CloseBallIfOpen(); // 던지면 열린 볼은 닫힘
                if (_settings.ThrowMode)
                    _physics.Velocity = _tracker.ComputeThrowVelocity(Now);
                break;

            case ReleaseAction.CatchHold:
                CloseBallIfOpen();
                if (_settings.Skin == SlimeSkinKind.Jelly)
                    _animation.Punch(); // 젤리만 잡히는 느낌의 작은 스쿼시
                break;

            case ReleaseAction.Click:
                if (_settings.PunchMode)
                    DoClickEffect(cursor);
                break;
        }

        EnsureRendering();
    }

    private void CloseBallIfOpen()
    {
        if (SkinHost.Content is ISkinClickEffect b && b.IsOpen) b.SetOpen(false);
    }

    private enum ReleaseAction { Throw, CatchHold, Click }

    /// <summary>놓을 때 동작 판정. 많이 움직였으면 던지기, 조금이라도 움직이던 걸 잡았으면 낚아채기, 그 외 클릭.</summary>
    private ReleaseAction ClassifyRelease(double movedPx, double grabbedSpeed)
    {
        if (movedPx >= _settings.ClickMoveThreshold) return ReleaseAction.Throw;
        if (grabbedSpeed > _settings.CatchSpeedThreshold) return ReleaseAction.CatchHold;
        return ReleaseAction.Click;
    }

    // ── 렌더 루프 ───────────────────────────────────────────
    private void EnsureRendering()
    {
        if (_renderingActive) return;
        _renderingActive = true;
        _lastFrameTime = Now;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_renderingActive) return;
        _renderingActive = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = Now;
        double dt = now - _lastFrameTime;
        _lastFrameTime = now;
        if (dt <= 0) return;
        if (dt > _settings.MaxFrameDeltaSeconds)
            dt = _settings.MaxFrameDeltaSeconds; // 큰 점프(스톨/포커스 복귀) 방지

        // 파티클은 어떤 상태에서도 항상 진행·렌더(드래그/일시정지 중에도 잔여 효과 소멸).
        bool particlesAlive = _particles.Update(dt);
        _overlay?.Render(_particles.Active);
        bool hitTextAlive = _hitText.Update(dt);
        _hitTextOverlay?.Render(_hitText.Active);

        if (_isDragging)
        {
            SetExpression(SlimeExpression.Normal);
            _physics.SpinAngle += _physics.AngularVelocity * dt; // 충전 중 시각 회전
            ApplyWindowPosition();
            _animation.Tick(dt, Vector2.Zero, _physics.SpinAngle);
            UpdateSpinFx();
            return;
        }

        if (_settings.Paused)
        {
            _animation.Tick(dt, Vector2.Zero, _physics.SpinAngle);
            if (_physics.IsAtRest && _animation.IsResting && !particlesAlive && !hitTextAlive)
                StopRendering();
            return;
        }

        PhysicsStepResult r = _physics.Update(dt);
        if (r.Collided)
        {
            _animation.OnImpact(r.MaxImpactSpeed);
            TriggerImpactEffects(r.MaxImpactSpeed, r.CollisionNormal);
            if (r.MaxImpactSpeed > _settings.MaxSpeed * DizzyImpactFraction)
                _dizzyUntil = now + DizzyDurationSeconds;
        }

        ApplyWindowPosition();
        _animation.Tick(dt, _physics.Velocity, _physics.SpinAngle);
        UpdateSpinFx();

        // 표정: 어질(충돌 직후) > 신남(빠름) > 평상
        SetExpression(
            now < _dizzyUntil ? SlimeExpression.Dizzy
            : _physics.Velocity.Length > _settings.MaxSpeed * FlyingSpeedFraction ? SlimeExpression.Flying
            : SlimeExpression.Normal);

        // 완전히 멈추고 형태도 안정되고 파티클도 없고 표정도 원상복귀되면 루프 정지(유휴).
        if (r.Sleeping && _animation.IsResting && !particlesAlive && !hitTextAlive && now >= _dizzyUntil)
            StopRendering();
    }

    /// <summary>충돌 세기를 단계로 분류해 스킨에 맞는 이펙트를 발동한다.</summary>
    private void TriggerImpactEffects(double impactSpeed, Vector2 normal)
    {
        ImpactTier tier = ImpactClassifier.Classify(impactSpeed, _settings);
        if (tier == ImpactTier.None) return;

        double intensity = ImpactClassifier.Intensity01(impactSpeed, _settings);
        Vector2 center = _physics.Position + new Vector2(_settings.SlimeSize / 2.0, _settings.SlimeSize / 2.0);

        if (_settings.Skin != SlimeSkinKind.Jelly)
        {
            // 단단한 스킨(당구공/몬스터볼): 슬라임 스플랫 대신 "쿠션에 탁!"
            // 벽 접점에서 접선 방향 스파크 + 딱딱한 소리
            Vector2 contact = center - normal * (_settings.SlimeSize / 2.0);
            _particles.EmitCushion(contact, normal, intensity, ImpactTier.Bonk);
            _audio.Play(ImpactTier.Bonk, intensity);
        }
        else
        {
            _particles.Emit(center, intensity, tier);
            _audio.Play(tier, intensity);
            _hitText.Spawn(center, intensity); // 젤리: 벽에 부딪히면 "Hit!" 문구
        }
    }

    /// <summary>정지 상태 클릭 반응(스킨별). 젤리=펀치, 당구공=딱 튕김, 몬스터볼=열림 이펙트.</summary>
    private void DoClickEffect(Vector2 cursor)
    {
        Vector2 center = _physics.Position + new Vector2(_settings.SlimeSize / 2.0, _settings.SlimeSize / 2.0);

        if (SkinHost.Content is ISkinClickEffect ball)
        {
            // 볼 계열: 클릭 시 열림/닫힘 토글(잡을 때 상태 기준). 빛/파티클 없이 조용히.
            ball.SetOpen(!_ballWasOpen);
            _audio.Play(ImpactTier.Bonk, 0.45);
            return;
        }

        Vector2 dir = (center - cursor).Normalized();
        if (dir.LengthSquared < 1e-6) dir = new Vector2(0, -1);
        _physics.Velocity = dir * _settings.PunchImpulse;

        if (_settings.Skin == SlimeSkinKind.Billiard)
        {
            // 당구공: 찌그러짐 없이 딱 튕김
            _audio.PlayPunch(0.6);
            return;
        }

        // 젤리: 찌그러지며 튕김 + 작은 파티클 + 타격 문구
        _animation.Punch();
        _particles.Emit(cursor, 0.4, ImpactTier.Boing);
        _hitText.Spawn(center, 0.55); // 때리면 "Pow!" 등
        _audio.PlayPunch(0.6);
    }

    // ── 모니터 구성 변경 ────────────────────────────────────
    private void OnMonitorLayoutChanged(object? sender, EventArgs e)
    {
        // MonitorLayoutService.Refresh() 는 이미 수행된 상태(IWalkableArea 갱신됨).
        UpdateDpiScale();
        ApplyWindowSize();

        // 슬라임이 사라진 모니터 위에 있었다면 주 모니터 중앙으로 되돌린다.
        if (!_physics.IsCurrentPositionValid())
            ResetPositionToCenter();

        ApplyWindowPosition();
        EnsureRendering();
    }

    // ── 설정 변경 반영 ──────────────────────────────────────
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.AlwaysOnTop):
                Topmost = _settings.AlwaysOnTop;
                break;
            case nameof(AppSettings.Paused):
                if (!_settings.Paused) EnsureRendering();
                break;
            case nameof(AppSettings.SlimeVisible):
                if (_settings.SlimeVisible) Show();
                else Hide();
                break;
            case nameof(AppSettings.Skin):
                ApplySkin();
                break;
            case nameof(AppSettings.CueStickMode):
                UpdateSpinAimVisibility();
                break;
            case nameof(AppSettings.CatchHotkeyMod):
            case nameof(AppSettings.CatchHotkeyVk):
            case nameof(AppSettings.CatchHotkeyMouse):
                RegisterCatchHotkey();
                break;
            case nameof(AppSettings.SlimeSize):
                ApplyWindowSize();
                if (!_physics.IsCurrentPositionValid())
                    _physics.SetPositionClamped(_physics.Position);
                ApplyWindowPosition();
                break;
        }
    }

    /// <summary>선택된 스킨(UserControl)을 스킨 호스트에 넣는다. 스킨 추가는 여기만 확장.</summary>
    private void ApplySkin()
    {
        SkinHost.Content = _settings.Skin switch
        {
            // 당구공: 4구/3구 중이면 흰 수구, 아니면 검은 8번공
            SlimeSkinKind.Billiard => _extraBalls.Count > 0
                ? new BilliardSkin(BilliardSkin.Cue)
                : new BilliardSkin(),
            SlimeSkinKind.Pokeball or SlimeSkinKind.Ultra or SlimeSkinKind.Master
                => new BallSkin(_settings.Skin),
            _ => new JellySkin(),
        };
        _expression = SlimeExpression.Normal; // 새 스킨은 기본 표정으로 시작
        UpdateSkinBehavior();
        UpdateSpinAimVisibility();
    }

    /// <summary>표정 변경(스킨이 표정을 지원할 때만). 상태가 바뀔 때만 반영.</summary>
    private void SetExpression(SlimeExpression e)
    {
        if (e == _expression) return;
        _expression = e;
        if (SkinHost.Content is ISkinExpressions skin)
            skin.SetExpression(e);
    }

    // ── 외부(설정창)에서 호출하는 public API ─────────────────
    public void ResetPositionPublic()
    {
        ResetPositionToCenter();
        ApplyWindowPosition();
    }

    // ── 컨텍스트 메뉴 ───────────────────────────────────────
    private SettingsWindow? _settingsWindow;

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_settings, this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnResetPosition(object sender, RoutedEventArgs e) => ResetPositionPublic();

    /// <summary>트레이 등 외부에서 설정 창을 여는 진입점.</summary>
    public void OpenSettingsPublic() => OnOpenSettings(this, new RoutedEventArgs());

    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    // ── 당구 4구/3구 (추가 공 생성) ─────────────────────────
    private readonly List<ExtraBallWindow> _extraBalls = new();
    private readonly Random _ballRng = new();

    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        bool bil = _settings.Skin == SlimeSkinKind.Billiard;
        var vis = bil ? Visibility.Visible : Visibility.Collapsed;
        MenuBilliardSep.Visibility = vis;
        Menu4Ball.Visibility = vis;
        Menu3Ball.Visibility = vis;
        MenuClearBalls.Visibility = _extraBalls.Count > 0 && bil ? Visibility.Visible : Visibility.Collapsed;
    }

    private void On4Ball(object sender, RoutedEventArgs e) => SpawnBalls(2, 1);
    private void On3Ball(object sender, RoutedEventArgs e) => SpawnBalls(1, 1);
    private void OnClearBalls(object sender, RoutedEventArgs e) => ClearExtraBalls();

    private void SpawnBalls(int reds, int yellows)
    {
        ClearExtraBalls();
        for (int i = 0; i < reds; i++) SpawnBall(Skins.BilliardSkin.Red);
        for (int i = 0; i < yellows; i++) SpawnBall(Skins.BilliardSkin.Yellow);
        if (_settings.Skin == SlimeSkinKind.Billiard) ApplySkin(); // 수구(흰색)로 전환
        StartBilliardLoop();
    }

    private void SpawnBall(Color color)
    {
        var wa = _monitors.PrimaryWorkingArea;
        double size = _settings.SlimeSize;
        double x = wa.Left + _ballRng.NextDouble() * Math.Max(1, wa.Width - size);
        double y = wa.Top + _ballRng.NextDouble() * Math.Max(1, wa.Height - size);
        double ang = _ballRng.NextDouble() * Math.PI * 2;
        double spd = 1000 + _ballRng.NextDouble() * 1600;
        var vel = new Vector2(Math.Cos(ang) * spd, Math.Sin(ang) * spd);

        var ball = new ExtraBallWindow(color, _settings, _monitors, new Vector2(x, y), vel);
        _extraBalls.Add(ball);
        ball.Show();
    }

    private void ClearExtraBalls()
    {
        StopBilliardLoop();
        foreach (var b in _extraBalls) { try { b.Close(); } catch { } }
        _extraBalls.Clear();
        if (_settings.Skin == SlimeSkinKind.Billiard) ApplySkin(); // 8번공으로 복귀
    }

    // ── 당구 물리 루프(공-공 충돌 포함) ─────────────────────
    private bool _billiardActive;
    private double _billiardLastTime;

    private void StartBilliardLoop()
    {
        if (_billiardActive) return;
        _billiardActive = true;
        _billiardLastTime = Now;
        CompositionTarget.Rendering += OnBilliardTick;
    }

    private void StopBilliardLoop()
    {
        if (!_billiardActive) return;
        _billiardActive = false;
        CompositionTarget.Rendering -= OnBilliardTick;
    }

    private void OnBilliardTick(object? sender, EventArgs e)
    {
        double now = Now;
        double dt = now - _billiardLastTime;
        _billiardLastTime = now;
        if (dt <= 0) return;
        if (dt > _settings.MaxFrameDeltaSeconds) dt = _settings.MaxFrameDeltaSeconds;

        // 추가 공 물리 적분(수구는 SlimeWindow 자체 루프가 적분)
        foreach (var b in _extraBalls) b.Physics.Update(dt);

        ResolveBallCollisions();

        foreach (var b in _extraBalls) b.ApplyPosition();
    }

    /// <summary>수구+추가 공들 간 원-원 탄성 충돌(동일 질량, 캐롬 느낌).</summary>
    private void ResolveBallCollisions()
    {
        double windowHalf = _settings.SlimeSize / 2.0;      // 창 중심(=공 중심)
        double r = _settings.SlimeSize * 0.44;              // 실제 보이는 공 반경(스킨 여백 반영)
        double minDist = r * 2.0;                            // 표면이 맞닿는 중심거리

        // 인덱스 0 = 수구(SlimeWindow._physics), 1.. = 추가 공
        var engines = new List<SlimePhysicsEngine> { _physics };
        foreach (var b in _extraBalls) engines.Add(b.Physics);

        var half = new Vector2(windowHalf, windowHalf);
        bool cueChanged = false;

        for (int i = 0; i < engines.Count; i++)
        for (int j = i + 1; j < engines.Count; j++)
        {
            var a = engines[i]; var b2 = engines[j];
            Vector2 ca = a.Position + half, cb = b2.Position + half;
            Vector2 d = cb - ca;
            double dist = d.Length;
            if (dist <= 1e-4 || dist >= minDist) continue;

            Vector2 n = d / dist;
            double overlap = minDist - dist;
            // 겹침 분리(각각 절반씩) 후, 벽/작업표시줄 밖으로 나가지 않게 클램프
            a.Position -= n * (overlap * 0.5);
            b2.Position += n * (overlap * 0.5);
            a.SetPositionClamped(a.Position);
            b2.SetPositionClamped(b2.Position);

            Vector2 rv = b2.Velocity - a.Velocity;
            double vn = rv.X * n.X + rv.Y * n.Y;
            if (vn < 0) // 접근 중일 때만 반발
            {
                double e = 0.94; // 당구공 반발
                double jimp = -(1 + e) * vn / 2.0; // 동일 질량
                a.Velocity -= n * jimp;
                b2.Velocity += n * jimp;
                if (i == 0) cueChanged = true;
            }
        }

        if (cueChanged) EnsureRendering(); // 수구가 맞았으면 수구 루프 깨우기
    }

    // ── 정리 ────────────────────────────────────────────────
    public void ShutdownCleanup()
    {
        StopRendering();
        if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, CatchHotkeyId);
        RemoveMouseTrigger();
        _hwndSource?.RemoveHook(WndProc);
        _settings.PropertyChanged -= OnSettingsChanged;
        _monitors.LayoutChanged -= OnMonitorLayoutChanged;
        MouseLeftButtonDown -= OnMouseLeftButtonDown;
        MouseMove -= OnMouseMove;
        MouseLeftButtonUp -= OnMouseLeftButtonUp;

        ClearExtraBalls();
        try { _aimOverlay?.Close(); } catch { }
        _aimOverlay = null;
        _overlay?.ShutdownCleanup();
        _overlay?.Close();
        _overlay = null;
        _hitTextOverlay?.ShutdownCleanup();
        _hitTextOverlay?.Close();
        _hitTextOverlay = null;
        _audio.Dispose();
    }
}
