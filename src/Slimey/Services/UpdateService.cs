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
    private static string TokenFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slimey", "update_token.txt");

    /// <summary>현재 실행 파일 버전(4자리 정규화).</summary>
    public static Version Current => Normalize(Assembly.GetEntryAssembly()?.GetName().Version);

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
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Slimey-Updater");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            if (!string.IsNullOrEmpty(token))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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
        }
        catch { /* 네트워크/권한/파싱 문제는 조용히 무시(앱 사용에 지장 없음) */ }
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
    }
}
