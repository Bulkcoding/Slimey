using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Slimey.Services;

/// <summary>
/// GitHub 릴리스 기반 자동 업데이트(조용히 자동 적용).
///
/// 흐름:
///  1) 앱 실행 중 백그라운드로 최신 릴리스를 확인해, 더 높은 버전이면 exe 를 staging 폴더에 받아 둔다.
///  2) 다음 실행의 맨 처음(창 생성 전)에 staging 된 최신 exe 가 있으면, 현재 프로세스 종료를 기다렸다
///     원본 exe 를 교체하고 재실행하는 스크립트를 띄운 뒤 스스로 종료한다.
///
/// 실행 중인 exe 는 덮어쓸 수 없으므로, 교체는 항상 "다음 실행 시작 시점"에 수행한다.
/// </summary>
public static class UpdateService
{
    private static string StageDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slimey", "update");
    private static string PendingExe => Path.Combine(StageDir, "Slimey.pending.exe");
    private static string PendingVer => Path.Combine(StageDir, "pending.txt");

    /// <summary>받아 둔 버전의 릴리스 노트(교체 전). apply.cmd 는 이 파일을 지우지 않는다.</summary>
    private static string PendingNotes => Path.Combine(StageDir, "pending_notes.json");

    /// <summary>교체가 실제로 일어난 뒤, 새 버전이 처음 뜰 때 보여줄 노트.</summary>
    private static string AppliedNotes => Path.Combine(StageDir, "applied_notes.json");
    private static string TokenFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slimey", "update_token.txt");

    /// <summary>현재 실행 파일 버전(4자리 정규화).</summary>
    public static Version Current => Normalize(Assembly.GetEntryAssembly()?.GetName().Version);

    /// <summary>릴리스 노트 1건(버전 + GitHub 릴리스 본문).</summary>
    public sealed class ReleaseNotes
    {
        public string Version { get; set; } = "";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
    }

    private static string? Token
    {
        get
        {
            try { if (File.Exists(TokenFile)) { var t = File.ReadAllText(TokenFile).Trim(); if (t.Length > 0) return t; } }
            catch { }
            return string.IsNullOrWhiteSpace(UpdateConfig.EmbeddedToken) ? null : UpdateConfig.EmbeddedToken;
        }
    }

    /// <summary>앱 시작 즉시(창 생성 전) 호출. staged 최신 exe 가 있으면 교체 스크립트 실행 후 true(→ 즉시 종료).</summary>
    public static bool TryApplyStagedUpdate()
    {
        try
        {
            if (!File.Exists(PendingExe) || !File.Exists(PendingVer)) return false;
            if (!Version.TryParse(File.ReadAllText(PendingVer).Trim(), out var pv)) { Cleanup(); return false; }
            if (Normalize(pv) <= Current) { Cleanup(); return false; }

            string? target = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(target)) return false;
            int pid = Environment.ProcessId;

            Directory.CreateDirectory(StageDir);

            // 교체가 확정된 시점에 노트를 applied 로 승격한다.
            // (pending_* 는 apply.cmd 가 지우므로, 살아남을 이름으로 옮겨 둔다.)
            PromoteNotes(pv);
            string script = Path.Combine(StageDir, "apply.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                ":wait\r\n" +
                $"tasklist /fi \"PID eq {pid}\" | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                $"copy /y \"{PendingExe}\" \"{target}\" >nul\r\n" +
                $"del /q \"{PendingExe}\" >nul 2>&1\r\n" +
                $"del /q \"{PendingVer}\" >nul 2>&1\r\n" +
                $"start \"\" \"{target}\"\r\n" +
                "del /q \"%~f0\" >nul 2>&1\r\n");

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            return true;
        }
        catch { return false; }
    }

    /// <summary>백그라운드에서 최신 릴리스 확인 → 더 높으면 staging 에 exe 다운로드(다음 실행 때 적용).</summary>
    public static async Task CheckAndStageAsync()
    {
        if (!UpdateConfig.Enabled) return;
        string? token = Token; // 공개 저장소면 토큰 없이 동작. 토큰이 있으면(비공개 대비) 인증에 사용.

        try
        {
            using var http = CreateClient(token);

            string api = $"https://api.github.com/repos/{UpdateConfig.Owner}/{UpdateConfig.Repo}/releases/latest";
            string json = await http.GetStringAsync(api);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Version? latest = ParseTag(root.GetProperty("tag_name").GetString());
            if (latest == null || latest <= Current) return;

            // 이미 같은(또는 더 높은) 버전을 받아 뒀으면 재다운로드 생략
            if (File.Exists(PendingVer) && Version.TryParse(File.ReadAllText(PendingVer).Trim(), out var staged)
                && Normalize(staged) >= latest) return;

            string? apiUrl = null, browserUrl = null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                string name = a.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    apiUrl = a.GetProperty("url").GetString();                                  // asset API URL(인증 필요)
                    browserUrl = a.TryGetProperty("browser_download_url", out var b) ? b.GetString() : null; // 공개 직링크
                    break;
                }
            }

            // 공개 저장소: 인증 없는 직링크(browser_download_url)로 받는다.
            // 토큰이 있으면(비공개 대비) asset API URL + octet-stream 으로 받는다.
            bool useApi = !string.IsNullOrEmpty(token) && apiUrl != null;
            string? downloadUrl = useApi ? apiUrl : browserUrl;
            if (downloadUrl == null) return;

            using var req = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            req.Headers.Accept.Clear();
            if (useApi) req.Headers.Accept.ParseAdd("application/octet-stream");
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            Directory.CreateDirectory(StageDir);
            string part = PendingExe + ".part";
            await using (var fs = File.Create(part))
                await resp.Content.CopyToAsync(fs);

            if (File.Exists(PendingExe)) File.Delete(PendingExe);
            File.Move(part, PendingExe);
            File.WriteAllText(PendingVer, latest.ToString());

            // 릴리스 노트도 같이 저장 — 이미 받아 둔 응답에 들어 있어 추가 요청이 필요 없다.
            SavePendingNotes(root, latest);
        }
        catch { /* 네트워크/권한/파싱 문제는 조용히 무시(앱 사용에 지장 없음) */ }
    }

    /// <summary>
    /// 최신 릴리스의 노트를 즉시 조회한다(설정창의 "최근 변경 내용 보기"용).
    /// 네트워크 실패 시 null.
    /// </summary>
    public static async Task<ReleaseNotes?> FetchLatestNotesAsync()
    {
        if (!UpdateConfig.Enabled) return null;
        try
        {
            using var http = CreateClient(Token);
            string api = $"https://api.github.com/repos/{UpdateConfig.Owner}/{UpdateConfig.Repo}/releases/latest";
            string json = await http.GetStringAsync(api);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Version? v = ParseTag(root.GetProperty("tag_name").GetString());
            return new ReleaseNotes
            {
                Version = (v ?? Current).ToString(3),
                Title = root.TryGetProperty("name", out var n) ? (n.GetString() ?? "").Trim() : "",
                Body = root.TryGetProperty("body", out var b)
                    ? (b.GetString() ?? "").Replace("\r\n", "\n").Trim()
                    : "",
            };
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to fetch latest release notes.", ex);
            return null;
        }
    }

    private static HttpClient CreateClient(string? token)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Slimey-Updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrEmpty(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    /// <summary>릴리스 응답에서 노트를 뽑아 pending_notes.json 으로 저장.</summary>
    private static void SavePendingNotes(JsonElement root, Version version)
    {
        try
        {
            string body = root.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "";
            string title = root.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";

            var notes = new ReleaseNotes
            {
                Version = version.ToString(3),
                Title = title.Trim(),
                Body = body.Replace("\r\n", "\n").Trim(),
            };
            File.WriteAllText(PendingNotes, JsonSerializer.Serialize(notes));
        }
        catch (Exception ex) { Logger.Error("Failed to save pending release notes.", ex); }
    }

    /// <summary>exe 교체가 확정되면 pending_notes → applied_notes 로 옮긴다(버전 확정 기록).</summary>
    private static void PromoteNotes(Version applying)
    {
        try
        {
            ReleaseNotes notes;
            if (File.Exists(PendingNotes))
            {
                notes = JsonSerializer.Deserialize<ReleaseNotes>(File.ReadAllText(PendingNotes))
                        ?? new ReleaseNotes();
            }
            else
            {
                notes = new ReleaseNotes(); // 노트 없이 받은 경우에도 "업데이트됨"은 알린다.
            }

            notes.Version = Normalize(applying).ToString(3);
            File.WriteAllText(AppliedNotes, JsonSerializer.Serialize(notes));

            try { if (File.Exists(PendingNotes)) File.Delete(PendingNotes); } catch { }
        }
        catch (Exception ex) { Logger.Error("Failed to promote release notes.", ex); }
    }

    /// <summary>
    /// 방금 업데이트가 적용되어 보여줄 노트가 있으면 반환하고 파일을 지운다(1회만 표시).
    /// 기록된 버전이 지금 실행 중인 버전과 다르면 무시한다(교체 실패/롤백 대비).
    /// </summary>
    public static ReleaseNotes? TryConsumeAppliedNotes()
    {
        try
        {
            if (!File.Exists(AppliedNotes)) return null;

            var notes = JsonSerializer.Deserialize<ReleaseNotes>(File.ReadAllText(AppliedNotes));
            try { File.Delete(AppliedNotes); } catch { } // 성공/실패와 무관하게 1회성

            if (notes == null) return null;
            if (!Version.TryParse(notes.Version, out var nv)) return null;
            if (Normalize(nv) != Current) return null; // 실제로 이 버전이 떠 있을 때만

            return notes;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to read applied release notes.", ex);
            return null;
        }
    }

    private static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        tag = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(tag, out var v) ? Normalize(v) : null;
    }

    private static Version Normalize(Version? v) =>
        v == null ? new Version(0, 0, 0, 0)
                  : new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build), 0);

    private static void Cleanup()
    {
        try { if (File.Exists(PendingExe)) File.Delete(PendingExe); } catch { }
        try { if (File.Exists(PendingVer)) File.Delete(PendingVer); } catch { }
        try { if (File.Exists(PendingNotes)) File.Delete(PendingNotes); } catch { }
    }
}
