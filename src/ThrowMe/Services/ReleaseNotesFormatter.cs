using System.Text.RegularExpressions;

namespace ThrowMe.Services;

/// <summary>렌더링할 한 줄의 종류.</summary>
public enum NoteLineKind
{
    /// <summary>일반 문단.</summary>
    Text,
    /// <summary>`#`/`##` 제목.</summary>
    Heading,
    /// <summary>`-`/`*`/`+` 불릿.</summary>
    Bullet,
    /// <summary>`&gt;` 인용문(주의·안내 콜아웃).</summary>
    Quote,
    /// <summary>빈 줄(간격).</summary>
    Spacer,
}

/// <summary>렌더링용 한 줄. Inline 서식은 굵게 여부만 구간으로 표현한다.</summary>
public sealed class NoteLine
{
    public NoteLineKind Kind { get; init; }
    /// <summary>불릿 들여쓰기 깊이(0부터).</summary>
    public int Indent { get; init; }
    /// <summary>(텍스트, 굵게) 구간 목록.</summary>
    public List<(string Text, bool Bold)> Runs { get; init; } = new();
}

/// <summary>
/// GitHub 릴리스 본문(마크다운)을 팝업에 뿌릴 수 있는 최소 형태로 변환한다.
///
/// 외부 패키지 없이 실제로 쓰이는 문법만 처리한다:
///   `#`~`###` 제목 / `-`,`*`,`+` 불릿(들여쓰기) / `&gt;` 인용문 / `**굵게**` / 인라인 `코드`.
/// 그 외 마크다운(표·링크·이미지 등)은 원문 텍스트 그대로 남긴다 — 읽는 데 지장이 없고,
/// 완전한 렌더러는 이 용도에 과하다.
/// </summary>
public static class ReleaseNotesFormatter
{
    private static readonly Regex HeadingRx = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex BulletRx = new(@"^(\s*)[-*+]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex QuoteRx = new(@"^\s*>+\s?(.*)$", RegexOptions.Compiled);
    private static readonly Regex BoldRx = new(@"\*\*(.+?)\*\*|__(.+?)__", RegexOptions.Compiled);

    /// <summary>마크다운 본문을 줄 목록으로 변환. 빈 입력이면 빈 목록.</summary>
    public static List<NoteLine> Parse(string? markdown)
    {
        var result = new List<NoteLine>();
        if (string.IsNullOrWhiteSpace(markdown)) return result;

        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        bool inCodeFence = false;

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();

            // ``` 코드펜스: 안쪽은 서식 해석 없이 그대로.
            if (line.TrimStart().StartsWith("```"))
            {
                inCodeFence = !inCodeFence;
                continue;
            }
            if (inCodeFence)
            {
                result.Add(new NoteLine { Kind = NoteLineKind.Text, Runs = { (line, false) } });
                continue;
            }

            if (line.Trim().Length == 0)
            {
                // 연속 빈 줄은 하나로 합친다.
                if (result.Count > 0 && result[^1].Kind != NoteLineKind.Spacer)
                    result.Add(new NoteLine { Kind = NoteLineKind.Spacer });
                continue;
            }

            var h = HeadingRx.Match(line);
            if (h.Success)
            {
                result.Add(new NoteLine
                {
                    Kind = NoteLineKind.Heading,
                    Runs = SplitBold(Clean(h.Groups[2].Value)),
                });
                continue;
            }

            // 인용문은 불릿보다 먼저 판정한다("> - 항목" 같은 줄을 불릿으로 오인하지 않도록).
            var q = QuoteRx.Match(line);
            if (q.Success)
            {
                string inner = q.Groups[1].Value.Trim();
                if (inner.Length == 0) continue; // "> " 만 있는 빈 인용 줄
                result.Add(new NoteLine
                {
                    Kind = NoteLineKind.Quote,
                    Runs = SplitBold(Clean(inner)),
                });
                continue;
            }

            var b = BulletRx.Match(line);
            if (b.Success)
            {
                // 공백 2칸당 한 단계(탭은 2칸으로 계산). 과도한 들여쓰기는 2단계로 제한.
                int spaces = b.Groups[1].Value.Replace("\t", "  ").Length;
                result.Add(new NoteLine
                {
                    Kind = NoteLineKind.Bullet,
                    Indent = Math.Min(2, spaces / 2),
                    Runs = SplitBold(Clean(b.Groups[2].Value)),
                });
                continue;
            }

            // 들여쓴 이어짐 줄은 바로 앞 불릿의 본문에 이어 붙인다.
            // (릴리스 노트에서 한 항목을 여러 줄로 접어 쓰는 스타일이 흔하다.)
            bool indented = raw.Length > 0 && char.IsWhiteSpace(raw[0]);
            if (indented && result.Count > 0 && result[^1].Kind == NoteLineKind.Bullet)
            {
                var prev = result[^1];
                prev.Runs.Add((" ", false));
                prev.Runs.AddRange(SplitBold(Clean(line.Trim())));
                continue;
            }

            result.Add(new NoteLine
            {
                Kind = NoteLineKind.Text,
                Runs = SplitBold(Clean(line.Trim())),
            });
        }

        // 끝의 빈 줄 제거
        while (result.Count > 0 && result[^1].Kind == NoteLineKind.Spacer)
            result.RemoveAt(result.Count - 1);

        return result;
    }

    /// <summary>표시에 불필요한 기호만 제거(인라인 코드 백틱 등).</summary>
    private static string Clean(string s) => s.Replace("`", "");

    /// <summary>`**굵게**` 기준으로 (텍스트, 굵게) 구간 분해.</summary>
    private static List<(string Text, bool Bold)> SplitBold(string s)
    {
        var runs = new List<(string, bool)>();
        int pos = 0;

        foreach (Match m in BoldRx.Matches(s))
        {
            if (m.Index > pos)
                runs.Add((s[pos..m.Index], false));

            string inner = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            runs.Add((inner, true));
            pos = m.Index + m.Length;
        }

        if (pos < s.Length)
            runs.Add((s[pos..], false));

        if (runs.Count == 0)
            runs.Add((s, false));

        return runs;
    }
}
