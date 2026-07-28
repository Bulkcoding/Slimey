using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ThrowMe.Models;

namespace ThrowMe.Services;

/// <summary>
/// 시스템 트레이 아이콘과 메뉴. 표시/숨김·일시정지·항상 위·위치 초기화·설정·종료를 제공한다.
/// 아이콘은 에셋 없이 코드로 생성하며(디자인 트랙이 .ico 로 교체 가능),
/// 상태 체크는 AppSettings.PropertyChanged 로 양방향 동기화된다.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _visibleItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _topItem;
    private bool _disposed;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public TrayIconService(AppSettings settings, Action openSettings, Action resetPosition, Action exit)
    {
        _settings = settings;

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Visible = true,
            Text = "ThrowMe",
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("설정...", null, (_, _) => openSettings());
        menu.Items.Add("위치 초기화", null, (_, _) => resetPosition());
        menu.Items.Add(new ToolStripSeparator());

        _visibleItem = new ToolStripMenuItem("슬라임 표시") { CheckOnClick = true, Checked = settings.SlimeVisible };
        _visibleItem.Click += (_, _) => _settings.SlimeVisible = _visibleItem.Checked;
        menu.Items.Add(_visibleItem);

        _pauseItem = new ToolStripMenuItem("일시 정지") { CheckOnClick = true, Checked = settings.Paused };
        _pauseItem.Click += (_, _) => _settings.Paused = _pauseItem.Checked;
        menu.Items.Add(_pauseItem);

        _topItem = new ToolStripMenuItem("항상 위") { CheckOnClick = true, Checked = settings.AlwaysOnTop };
        _topItem.Click += (_, _) => _settings.AlwaysOnTop = _topItem.Checked;
        menu.Items.Add(_topItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => exit());

        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => _settings.SlimeVisible = !_settings.SlimeVisible;

        _settings.PropertyChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.SlimeVisible): _visibleItem.Checked = _settings.SlimeVisible; break;
            case nameof(AppSettings.Paused): _pauseItem.Checked = _settings.Paused; break;
            case nameof(AppSettings.AlwaysOnTop): _topItem.Checked = _settings.AlwaysOnTop; break;
        }
    }

    /// <summary>민트색 슬라임 원형 아이콘을 코드로 생성.</summary>
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(140, 245, 216)); // 민트
            g.FillEllipse(body, 3, 6, 26, 23);
            using var eye = new SolidBrush(Color.FromArgb(42, 42, 58));
            g.FillEllipse(eye, 11, 15, 4, 6);
            g.FillEllipse(eye, 18, 15, 4, 6);
        }

        IntPtr handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone(); // 핸들 소유하지 않는 복제본
        }
        finally
        {
            DestroyIcon(handle); // GetHicon 핸들 누수 방지
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.PropertyChanged -= OnSettingsChanged;
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}
