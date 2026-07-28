using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ThrowMe.Services;

namespace ThrowMe.Network;

/// <summary>
/// 방 코드 기반 로그인·신원 보관(MVP). 계정 DB 없이 (방 코드 + 시크릿) 만으로 그룹핑한다.
///
/// - 릴레이 서버 주소는 <see cref="DefaultServerBaseUrl"/> 로 앱에 내장된다(사용자 입력 불필요).
/// - <see cref="RoomCode"/> + <see cref="Secret"/> : 같은 값을 입력한 PC 들이 한 방에 묶인다.
/// - <see cref="NodeId"/> : 이 PC의 표시 이름(기본 = 컴퓨터 이름). 방 안에서 고유해야 함.
///
/// 비밀값(시크릿)은 %LOCALAPPDATA%\ThrowMe\relay.json 에만 저장하고 리포지토리·배포 exe 에 넣지 않는다.
/// </summary>
public sealed class AuthService
{
    /// <summary>
    /// 내장 릴레이 서버 주소. 사용자가 입력할 필요 없이 앱이 항상 이 서버로 접속한다.
    /// 로컬 개발 서버로 바꾸려면 relay.json 의 serverBaseUrl 또는 --server= 인자로 덮어쓴다.
    /// </summary>
    public const string DefaultServerBaseUrl = "wss://slimey-relay.throwme.workers.dev";
    /// <summary>
    /// 설정 프로필 이름. 비우면 기본(<c>relay.json</c>), 지정하면 <c>relay.&lt;profile&gt;.json</c> 을 사용한다.
    /// 한 PC에서 여러 인스턴스를 서로 다른 노드로 띄워 테스트할 때 사용(<c>--profile=A</c>).
    /// </summary>
    public static string Profile { get; set; } = "";

    private static string ConfigDir => Services.AppPaths.Local;

    private static string ConfigPath => Path.Combine(ConfigDir,
        string.IsNullOrWhiteSpace(Profile) ? "relay.json" : $"relay.{Profile}.json");

    public bool Enabled { get; set; }
    /// <summary>릴레이 서버 주소. 기본은 내장값이며, 개발용으로만 파일/인자로 덮어쓴다.</summary>
    public string ServerBaseUrl { get; set; } = DefaultServerBaseUrl;
    public string RoomCode { get; set; } = "";
    public string Secret { get; set; } = "";
    public string NodeId { get; set; } = "";
    /// <summary>마지막으로 알려진 엣지 매핑(연결 전 표시용 캐시). 서버가 권위.</summary>
    public List<EdgeLinkDto> Links { get; set; } = new();

    /// <summary>참여에 필요한 값이 모두 채워졌는가. 서버 주소는 내장 기본값이 있어 사용자 입력이 아니다.</summary>
    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(EffectiveServerBaseUrl)
        && !string.IsNullOrWhiteSpace(RoomCode)
        && !string.IsNullOrWhiteSpace(Secret)
        && !string.IsNullOrWhiteSpace(NodeId);

    /// <summary>실제 사용할 서버 주소(비어 있으면 내장 기본값).</summary>
    public string EffectiveServerBaseUrl =>
        string.IsNullOrWhiteSpace(ServerBaseUrl) ? DefaultServerBaseUrl : ServerBaseUrl;

    /// <summary>WSS 접속 URI: &lt;base&gt;/room/&lt;CODE&gt;.</summary>
    public Uri BuildUri()
    {
        string baseUrl = EffectiveServerBaseUrl.TrimEnd('/');
        // http(s):// 로 들어오면 ws(s):// 로 보정.
        if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            baseUrl = "wss://" + baseUrl["https://".Length..];
        else if (baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            baseUrl = "ws://" + baseUrl["http://".Length..];
        string code = Uri.EscapeDataString(RoomCode.Trim().ToUpperInvariant());
        return new Uri($"{baseUrl}/room/{code}");
    }

    /// <summary>서버에 보낼 HELLO 봉투.</summary>
    public Envelope BuildHello() => new()
    {
        Type = MsgType.Hello,
        RoomId = RoomCode.Trim().ToUpperInvariant(),
        From = NodeId,
        Data = RelayJson.ToElement(new HelloData
        {
            Secret = Secret,
            Version = UpdateService.Current.ToString(),
        }),
    };

    // ── 영속화 ───────────────────────────────────────────────
    private sealed class Persisted
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("serverBaseUrl")] public string ServerBaseUrl { get; set; } = "";
        [JsonPropertyName("roomCode")] public string RoomCode { get; set; } = "";
        [JsonPropertyName("secret")] public string Secret { get; set; } = "";
        [JsonPropertyName("nodeId")] public string NodeId { get; set; } = "";
        [JsonPropertyName("links")] public List<EdgeLinkDto> Links { get; set; } = new();
    }

    public static AuthService Load()
    {
        var svc = new AuthService();
        try
        {
            if (File.Exists(ConfigPath))
            {
                var p = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(ConfigPath));
                if (p != null)
                {
                    svc.Enabled = p.Enabled;
                    svc.ServerBaseUrl = p.ServerBaseUrl;
                    svc.RoomCode = p.RoomCode;
                    svc.Secret = p.Secret;
                    svc.NodeId = p.NodeId;
                    svc.Links = p.Links ?? new();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load relay config.", ex);
        }

        if (string.IsNullOrWhiteSpace(svc.NodeId))
            svc.NodeId = DefaultNodeId();

        // 저장값이 비어 있으면(구버전 설정·서버 주소 미입력) 내장 기본 서버로.
        if (string.IsNullOrWhiteSpace(svc.ServerBaseUrl))
            svc.ServerBaseUrl = DefaultServerBaseUrl;

        svc.ApplyCommandLineOverrides();
        return svc;
    }

    /// <summary>
    /// 실행 인자로 설정을 덮어쓴다(한 PC에서 여러 노드 테스트용).
    ///   ThrowMe.exe --profile=A --node=PC-A --server=wss://... --room=TEST-1 --secret=pw --link=Right:PC-B:Left
    /// <c>--link</c> 는 여러 번 지정 가능(형식: 내엣지:상대노드:상대엣지[:flip]).
    /// </summary>
    private void ApplyCommandLineOverrides()
    {
        string[] args;
        try { args = Environment.GetCommandLineArgs(); }
        catch { return; }

        var links = new List<EdgeLinkDto>();
        bool anyLink = false;

        foreach (string raw in args.Skip(1))
        {
            string arg = raw.Trim();
            int eq = arg.IndexOf('=');
            if (!arg.StartsWith("--", StringComparison.Ordinal) || eq < 0) continue;
            string key = arg[2..eq].ToLowerInvariant();
            string val = arg[(eq + 1)..].Trim().Trim('"');

            switch (key)
            {
                case "server": ServerBaseUrl = val; Enabled = true; break;
                case "room": RoomCode = val; Enabled = true; break;
                case "secret": Secret = val; break;
                case "node": if (val.Length > 0) NodeId = val; break;
                case "link":
                    // Right:PC-B:Left[:flip]
                    string[] parts = val.Split(':');
                    if (parts.Length >= 3
                        && HandoffMath.TryParseEdge(parts[0], out var fromEdge)
                        && HandoffMath.TryParseEdge(parts[2], out var toEdge))
                    {
                        anyLink = true;
                        links.Add(new EdgeLinkDto
                        {
                            From = NodeId, FromEdge = fromEdge.ToString(),
                            To = parts[1], ToEdge = toEdge.ToString(),
                            Flip = parts.Length >= 4 && parts[3].Equals("flip", StringComparison.OrdinalIgnoreCase),
                        });
                    }
                    break;
            }
        }

        if (anyLink)
        {
            // --node 가 --link 뒤에 올 수도 있으니 From 을 최종 NodeId 로 확정.
            foreach (var l in links) l.From = NodeId;
            Links = links;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var p = new Persisted
            {
                Enabled = Enabled,
                ServerBaseUrl = ServerBaseUrl,
                RoomCode = RoomCode,
                Secret = Secret,
                NodeId = NodeId,
                Links = Links,
            };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(p,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save relay config.", ex);
        }
    }

    private static string DefaultNodeId()
    {
        try { return Environment.MachineName; }
        catch { return "PC-" + Guid.NewGuid().ToString("N")[..6]; }
    }
}
