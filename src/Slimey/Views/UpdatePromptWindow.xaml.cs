using System.Windows;
using System.Windows.Input;
using Slimey.Models;

namespace Slimey.Views;

/// <summary>
/// 새 버전을 받아 둔 직후 "지금 재시작할까요?" 를 묻는 팝업.
///
/// 실행 중인 exe 는 스스로를 덮어쓸 수 없어 업데이트에는 재시작이 반드시 필요하다.
/// 예전에는 아무것도 묻지 않고 다음 실행을 기다려서, 사용자가 앱을 두 번 켜야 새 버전이 됐다.
/// (기본 MessageBox 대신 앱 테마에 맞춘 창 — <see cref="ReleaseNotesWindow"/> 와 같은 형식.)
/// </summary>
public partial class UpdatePromptWindow : Window
{
    private readonly AppSettings? _settings;

    /// <summary>사용자가 "지금 재시작" 을 골랐는가.</summary>
    public bool RestartNow { get; private set; }

    public UpdatePromptWindow(System.Version version, AppSettings? settings = null)
    {
        InitializeComponent();
        _settings = settings;

        // Version.ToString() 은 "1.5.0.0" 처럼 4자리라, 릴리스 태그와 같은 3자리로 맞춘다.
        VersionBadge.Text = "v" + (version.Revision <= 0 && version.Build >= 0
            ? version.ToString(3)
            : version.ToString());

        if (_settings != null)
        {
            MuteToggle.IsChecked = !_settings.AutoRestartOnUpdate;
            MuteToggle.Checked += OnMuteChanged;
            MuteToggle.Unchecked += OnMuteChanged;
        }
        else
        {
            MuteToggle.Visibility = Visibility.Collapsed;
        }
    }

    private void OnMuteChanged(object sender, RoutedEventArgs e)
    {
        // 체크 = "다음부터 묻지 않기" → 자동 재시작 제안 끄기.
        if (_settings != null) _settings.AutoRestartOnUpdate = MuteToggle.IsChecked != true;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnRestartNow(object sender, RoutedEventArgs e)
    {
        RestartNow = true;
        DialogResult = true;
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        RestartNow = false;
        DialogResult = false;
    }
}
