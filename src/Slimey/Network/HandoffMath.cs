using Slimey.Physics;

namespace Slimey.Network;

/// <summary>가상 데스크톱 바깥 경계 방향.</summary>
public enum Edge { Left, Right, Top, Bottom }

/// <summary>
/// 엣지 정규화용 경계(가상 데스크톱). System.Windows.Rect 대신 double 로만 표현해
/// 물리·수학 로직을 UI(WPF) 어셈블리에서 분리한다(단위 테스트 용이).
/// </summary>
public readonly struct Bounds
{
    public double Left { get; }
    public double Top { get; }
    public double Width { get; }
    public double Height { get; }
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public Bounds(double left, double top, double width, double height)
    {
        Left = left; Top = top; Width = width; Height = height;
    }
}

/// <summary>
/// 핸드오프 좌표/속도 변환(순수 함수). 절대 좌표를 보내지 않고 엣지 파라미터 t∈[0,1]
/// + 엣지 기준 속도(법선/접선)만 주고받아 해상도·DPI 무관하게 이어지도록 한다.
/// 설계문서 `Slimey_멀티PC_인터넷_릴레이_설계.md` 11절(= LAN 설계 5절) 구현.
/// </summary>
public static class HandoffMath
{
    /// <summary>엣지 바깥 방향 단위 법선.</summary>
    public static Vector2 OutwardNormal(Edge e) => e switch
    {
        Edge.Left => new Vector2(-1, 0),
        Edge.Right => new Vector2(1, 0),
        Edge.Top => new Vector2(0, -1),
        _ => new Vector2(0, 1), // Bottom
    };

    /// <summary>엣지 접선 단위 벡터(Left/Right=아래+, Top/Bottom=오른쪽+).</summary>
    public static Vector2 Tangent(Edge e) => e switch
    {
        Edge.Left or Edge.Right => new Vector2(0, 1),
        _ => new Vector2(1, 0), // Top/Bottom
    };

    private static double Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>엣지 위 중심점의 정규화 파라미터 t∈[0,1].</summary>
    public static double ParamAlong(Edge e, Bounds b, Vector2 center)
    {
        double t = (e is Edge.Left or Edge.Right)
            ? SafeDiv(center.Y - b.Top, b.Height)
            : SafeDiv(center.X - b.Left, b.Width);
        return Math.Clamp(t, 0.0, 1.0);
    }

    /// <summary>파라미터 t 에 해당하는 엣지 위 점(중심 기준).</summary>
    public static Vector2 CenterFromParam(Edge e, Bounds b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return e switch
        {
            Edge.Left => new Vector2(b.Left, b.Top + t * b.Height),
            Edge.Right => new Vector2(b.Right, b.Top + t * b.Height),
            Edge.Top => new Vector2(b.Left + t * b.Width, b.Top),
            _ => new Vector2(b.Left + t * b.Width, b.Bottom), // Bottom
        };
    }

    /// <summary>
    /// 공 중심이 경계를 넘어 밖으로 나가고 있는 엣지를 판정. 없으면 null.
    /// 여러 축을 넘으면 가장 많이 넘어간 쪽을 선택.
    /// </summary>
    public static Edge? DetectExitEdge(Bounds b, Vector2 center, Vector2 velocity)
    {
        Edge? best = null;
        double bestOver = 0;
        void Consider(bool outward, Edge e, double over)
        {
            if (outward && over > bestOver) { bestOver = over; best = e; }
        }
        Consider(center.X < b.Left && velocity.X < 0, Edge.Left, b.Left - center.X);
        Consider(center.X > b.Right && velocity.X > 0, Edge.Right, center.X - b.Right);
        Consider(center.Y < b.Top && velocity.Y < 0, Edge.Top, b.Top - center.Y);
        Consider(center.Y > b.Bottom && velocity.Y > 0, Edge.Bottom, center.Y - b.Bottom);
        return best;
    }

    /// <summary>나가는 공 상태를 엣지 정규화 페이로드로 패킹.</summary>
    public static HandoffData Pack(
        string handoffId, string viaLink, Edge exitEdge, Bounds b,
        Vector2 center, Vector2 velocity, double angularVelocity,
        double surfaceSpin, Vector2 spinShotDir, double spinAngle)
    {
        Vector2 n = OutwardNormal(exitEdge);
        Vector2 tau = Tangent(exitEdge);
        return new HandoffData
        {
            HandoffId = handoffId,
            ViaLink = viaLink,
            EdgeParam = ParamAlong(exitEdge, b, center),
            NormalSpeed = Dot(velocity, n),       // 양수 = 밖으로(나가는 중)
            TangentSpeed = Dot(velocity, tau),
            AngularVelocity = angularVelocity,
            SurfaceSpin = surfaceSpin,
            // MVP: 표면 스핀 축은 진입 측에서 진행 방향으로 재설정하므로 각도는 참고값(0).
            SurfaceSpinAxisDeg = 0,
            SpinAngle = spinAngle,
        };
    }

    public struct IncomingState
    {
        public Vector2 Position;      // 슬라임 top-left(물리 픽셀)
        public Vector2 Velocity;
        public double AngularVelocity;
        public double SurfaceSpin;
        public Vector2 SpinShotDir;
        public double SpinAngle;
    }

    /// <summary>받는 쪽: 페이로드를 자기 해상도의 진입 엣지 기준으로 역변환.</summary>
    public static IncomingState Unpack(HandoffData d, Edge entryEdge, bool flip, Bounds b, double slimeSize)
    {
        double t = flip ? 1.0 - d.EdgeParam : d.EdgeParam;
        Vector2 edgePoint = CenterFromParam(entryEdge, b, t);
        Vector2 inward = OutwardNormal(entryEdge) * -1.0;
        Vector2 tau = Tangent(entryEdge);

        // 진입 위치: 엣지에서 안쪽으로 slimeSize 만큼(=화면 안 + 지연 흡수) 밀어 넣는다.
        Vector2 centerIn = edgePoint + inward * slimeSize;
        double tangent = flip ? -d.TangentSpeed : d.TangentSpeed;
        Vector2 vel = inward * d.NormalSpeed + tau * tangent;

        return new IncomingState
        {
            Position = centerIn - new Vector2(slimeSize / 2.0, slimeSize / 2.0),
            Velocity = vel,
            AngularVelocity = flip ? -d.AngularVelocity : d.AngularVelocity,
            SurfaceSpin = d.SurfaceSpin,
            SpinShotDir = vel.Normalized(),   // 표면 스핀 축 = 진행 방향으로 재설정
            SpinAngle = d.SpinAngle,
        };
    }

    // ── 엣지 문자열 ↔ enum ──────────────────────────────────
    public static bool TryParseEdge(string? s, out Edge edge)
    {
        switch ((s ?? "").Trim().ToLowerInvariant())
        {
            case "left": edge = Edge.Left; return true;
            case "right": edge = Edge.Right; return true;
            case "top": edge = Edge.Top; return true;
            case "bottom": edge = Edge.Bottom; return true;
            default: edge = Edge.Left; return false;
        }
    }

    /// <summary>"A.Right->B.Left" 의 진입(도착) 엣지(두 번째 항목의 엣지)를 파싱.</summary>
    public static bool TryEntryEdgeFromViaLink(string? viaLink, out Edge entry)
    {
        entry = Edge.Left;
        if (string.IsNullOrEmpty(viaLink)) return false;
        int arrow = viaLink.IndexOf("->", StringComparison.Ordinal);
        if (arrow < 0) return false;
        string toPart = viaLink[(arrow + 2)..];
        int dot = toPart.LastIndexOf('.');
        if (dot < 0) return false;
        return TryParseEdge(toPart[(dot + 1)..], out entry);
    }

    private static double SafeDiv(double a, double b) => Math.Abs(b) < 1e-9 ? 0.0 : a / b;
}
