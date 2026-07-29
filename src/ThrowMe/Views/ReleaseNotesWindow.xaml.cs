using System.Windows;
using System.Windows.Input;
using ThrowMe.Models;
using ThrowMe.Services;

namespace ThrowMe.Views;

/// <summary>
/// 업데이트가 적용된 뒤 "무엇이 달라졌는지" 한 번 보여주는 팝업.
///
/// 본문은 GitHub 릴리스 노트(마크다운)를 <see cref="ReleaseNotesFormatter"/> 로
/// 최소 서식만 해석해 <see cref="NotesRenderer"/> 가 그린다(업데이트 확인창과 공유).
/// 외부 마크다운 패키지는 쓰지 않는다.
/// </summary>
public partial class ReleaseNotesWindow : Window
{
    private readonly AppSettings? _settings;

    public ReleaseNotesWindow(UpdateService.ReleaseNotes notes, AppSettings? settings = null)
    {
        InitializeComponent();
        _settings = settings;

        VersionBadge.Text = "v" + (string.IsNullOrWhiteSpace(notes.Version) ? "?" : notes.Version);

        // 릴리스 제목이 있으면 헤드라인으로, 없으면 기본 문구.
        if (!string.IsNullOrWhiteSpace(notes.Title))
            HeadlineText.Text = notes.Title;

        NotesRenderer.Render(NotesPanel, notes.Body, this);

        // 토글 초기값 + 변경 시 설정 반영(설정이 주어진 경우에만).
        if (_settings != null)
        {
            MuteToggle.IsChecked = !_settings.ShowReleaseNotes;
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
        if (_settings != null)
            _settings.ShowReleaseNotes = MuteToggle.IsChecked != true;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); }
            catch { /* 드래그 중 창이 닫히는 등 드문 경우 무시 */ }
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
