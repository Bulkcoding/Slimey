using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Panel = System.Windows.Controls.Panel;
using Orientation = System.Windows.Controls.Orientation;

namespace ThrowMe.Services;

/// <summary>
/// 릴리스 노트를 화면에 그린다.
///
/// 업데이트 확인창(재시작 물어볼 때)과 업데이트 후 팝업이 같은 내용을 보여주므로
/// 렌더링을 한곳에 모았다. 서식 해석은 <see cref="ReleaseNotesFormatter"/> 담당.
/// </summary>
public static class NotesRenderer
{
    /// <summary>파싱된 노트를 패널에 채운다. 리소스(색/스타일)는 host 의 것을 쓴다.</summary>
    public static void Render(Panel target, string body, FrameworkElement host)
    {
        target.Children.Clear();

        var lines = ReleaseNotesFormatter.Parse(body);
        if (lines.Count == 0)
        {
            target.Children.Add(new TextBlock
            {
                Text = "자세한 변경 내용은 GitHub 릴리스 페이지에서 확인할 수 있습니다.",
                Style = (Style)host.FindResource("RowDesc"),
            });
            return;
        }

        var text = (Brush)host.FindResource("TextBrush");
        var muted = (Brush)host.FindResource("MutedBrush");
        var accent = (Brush)host.FindResource("Accent");
        var card = (Brush)host.FindResource("CardBg");

        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case NoteLineKind.Spacer:
                    target.Children.Add(new Border { Height = 8 });
                    break;

                case NoteLineKind.Heading:
                    target.Children.Add(MakeText(line, 14, bold: true, text, top: 12));
                    break;

                case NoteLineKind.Quote:
                    target.Children.Add(MakeQuote(line, muted, accent, card));
                    break;

                case NoteLineKind.Bullet:
                    target.Children.Add(MakeBullet(line, text, accent));
                    break;

                default:
                    target.Children.Add(MakeText(line, 13, bold: false, text, top: 2));
                    break;
            }
        }
    }

    private static TextBlock MakeText(NoteLine line, double size, bool bold, Brush brush, double top)
    {
        var tb = new TextBlock
        {
            FontSize = size,
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, top, 0, 0),
            LineHeight = size * 1.55,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        if (bold) tb.FontWeight = FontWeights.Bold;
        AddRuns(tb, line, forceBold: bold);
        return tb;
    }

    /// <summary>인용문: 왼쪽에 강조색 세로선을 둔 안내 블록.</summary>
    private static Border MakeQuote(NoteLine line, Brush fg, Brush accent, Brush bg)
    {
        var text = new TextBlock
        {
            FontSize = 12.5,
            Foreground = fg,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 12.5 * 1.55,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        AddRuns(text, line, forceBold: false);

        return new Border
        {
            Margin = new Thickness(0, 8, 0, 4),
            Padding = new Thickness(10, 7, 10, 7),
            CornerRadius = new CornerRadius(0, 6, 6, 0),
            Background = bg,
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = accent,
            Child = text,
        };
    }

    /// <summary>불릿: 들여쓰기 + 점 + 본문(줄바꿈 시 본문만 들여쓰기 유지).</summary>
    private static Grid MakeBullet(NoteLine line, Brush fg, Brush accent)
    {
        var grid = new Grid { Margin = new Thickness(line.Indent * 16, 3, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new TextBlock
        {
            Text = "•",
            FontSize = 13,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(dot, 0);

        var text = new TextBlock
        {
            FontSize = 13,
            Foreground = fg,
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
}
