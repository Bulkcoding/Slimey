using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Slimey.Models;
using Slimey.Services;
using Brush = System.Windows.Media.Brush;

namespace Slimey.Views;

/// <summary>
/// 업데이트가 적용된 뒤 "무엇이 달라졌는지" 한 번 보여주는 팝업.
///
/// 본문은 GitHub 릴리스 노트(마크다운)를 <see cref="ReleaseNotesFormatter"/> 로
/// 최소 서식만 해석해 그린다. 외부 마크다운 패키지는 쓰지 않는다.
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

        BuildBody(notes.Body);

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

    /// <summary>파싱된 줄들을 TextBlock 으로 쌓는다.</summary>
    private void BuildBody(string body)
    {
        var lines = ReleaseNotesFormatter.Parse(body);

        if (lines.Count == 0)
        {
            NotesPanel.Children.Add(new TextBlock
            {
                Text = "자세한 변경 내용은 GitHub 릴리스 페이지에서 확인할 수 있습니다.",
                Style = (Style)FindResource("RowDesc"),
            });
            return;
        }

        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case NoteLineKind.Spacer:
                    NotesPanel.Children.Add(new Border { Height = 8 });
                    break;

                case NoteLineKind.Heading:
                    NotesPanel.Children.Add(MakeText(line, fontSize: 14, bold: true,
                        brush: (Brush)FindResource("TextBrush"), topMargin: 12));
                    break;

                case NoteLineKind.Bullet:
                    NotesPanel.Children.Add(MakeBullet(line));
                    break;

                default:
                    NotesPanel.Children.Add(MakeText(line, fontSize: 13, bold: false,
                        brush: (Brush)FindResource("TextBrush"), topMargin: 2));
                    break;
            }
        }
    }

    private TextBlock MakeText(NoteLine line, double fontSize, bool bold, Brush brush, double topMargin)
    {
        var tb = new TextBlock
        {
            FontSize = fontSize,
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, topMargin, 0, 0),
            LineHeight = fontSize * 1.55,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        if (bold) tb.FontWeight = FontWeights.Bold;
        AddRuns(tb, line, forceBold: bold);
        return tb;
    }

    /// <summary>불릿: 들여쓰기 + 점 + 본문(줄바꿈 시 본문만 들여쓰기 유지).</summary>
    private Grid MakeBullet(NoteLine line)
    {
        var grid = new Grid { Margin = new Thickness(line.Indent * 16, 3, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new TextBlock
        {
            Text = "•",
            FontSize = 13,
            Foreground = (Brush)FindResource("Accent"),
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(dot, 0);

        var text = new TextBlock
        {
            FontSize = 13,
            Foreground = (Brush)FindResource("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 13 * 1.55,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        AddRuns(text, line, forceBold: false);
        Grid.SetColumn(text, 1);

        grid.Children.Add(dot);
        grid.Children.Add(text);
        return grid;
    }

    private static void AddRuns(TextBlock target, NoteLine line, bool forceBold)
    {
        foreach (var (text, boldRun) in line.Runs)
        {
            if (text.Length == 0) continue;
            var run = new Run(text);
            if (boldRun && !forceBold) run.FontWeight = FontWeights.Bold;
            target.Inlines.Add(run);
        }
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
