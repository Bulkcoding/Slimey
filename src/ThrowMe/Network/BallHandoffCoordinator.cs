using System.Linq;
using ThrowMe.Models;
using ThrowMe.Physics;

namespace ThrowMe.Network;

/// <summary>
/// 엣지 교차를 감지해 공을 넘기고, 받은 공을 물리 엔진에 주입하는 조정자.
/// 물리 엔진은 네트워크를 전혀 모른다(순수 유지) — 이 조정자가 밖에서 가로챈다.
///
/// UI 비의존: SlimePhysicsEngine·AppSettings·순수 수학만 참조하므로 단위 테스트 가능.
/// 실제 표시/숨김·렌더 재개는 결과값을 받은 SlimeWindow 가 처리한다.
/// </summary>
public sealed class BallHandoffCoordinator
{
    private readonly SlimePhysicsEngine _physics;
    private readonly AppSettings _settings;
    private readonly NetworkedWalkableArea _area;
    private readonly Func<Bounds> _bounds;
    private readonly Action<Envelope> _send;

    private List<EdgeLinkDto> _links = new();
    private string? _pendingOutId;
    private Edge _pendingExitEdge;
    private Vector2 _savedVelocity;
    private int _seq;

    public string SelfNodeId { get; set; } = "";

    public bool HasPendingOut => _pendingOutId != null;

    public BallHandoffCoordinator(
        SlimePhysicsEngine physics, AppSettings settings,
        NetworkedWalkableArea area, Func<Bounds> bounds, Action<Envelope> send)
    {
        _physics = physics;
        _settings = settings;
        _area = area;
        _bounds = bounds;
        _send = send;
    }

    /// <summary>엣지 매핑 갱신 + area 의 통과 허용 엣지 재계산.</summary>
    public void SetLinks(IEnumerable<EdgeLinkDto> links)
    {
        _links = links.ToList();
        _area.LinkedExitEdges.Clear();
        foreach (var l in _links)
        {
            if (l.From == SelfNodeId && HandoffMath.TryParseEdge(l.FromEdge, out var e))
                _area.LinkedExitEdges.Add(e);
        }
    }

    /// <summary>
    /// 물리 tick 뒤 호출. 공이 연결된 엣지를 넘었으면 핸드오프를 보내고 물리를 얼린 뒤 true.
    /// (SlimeWindow 는 true 면 공을 숨기고 유휴로 전환. 결과는 <see cref="OnResult"/> 로.)
    /// </summary>
    public bool CheckAndSendHandoff()
    {
        if (_pendingOutId != null) return false;

        Bounds b = _bounds();
        double size = _settings.SlimeSize;
        Vector2 center = _physics.Position + new Vector2(size / 2.0, size / 2.0);

        if (HandoffMath.DetectExitEdge(b, center, _physics.Velocity) is not Edge exit) return false;

        EdgeLinkDto? link = _links.FirstOrDefault(l =>
            l.From == SelfNodeId && HandoffMath.TryParseEdge(l.FromEdge, out var e) && e == exit);
        if (link == null) return false;

        string id = Guid.NewGuid().ToString("N");
        string via = $"{link.From}.{link.FromEdge}->{link.To}.{link.ToEdge}";
        HandoffData data = HandoffMath.Pack(
            id, via, exit, b, center, _physics.Velocity,
            _physics.AngularVelocity, _physics.SurfaceSpin, _physics.SpinShotDir, _physics.SpinAngle);

        _send(new Envelope
        {
            Type = MsgType.Handoff,
            From = SelfNodeId,
            To = link.To,
            Seq = ++_seq,
            Data = RelayJson.ToElement(data),
        });

        // ACK 전까지 롤백 대비 상태 저장 후 물리 정지(공이 다른 PC에 가 있는 동안).
        _pendingOutId = id;
        _pendingExitEdge = exit;
        _savedVelocity = _physics.Velocity;
        _physics.Velocity = Vector2.Zero;
        _physics.AngularVelocity = 0;
        _physics.SurfaceSpin = 0;
        return true;
    }

    public enum ResultKind { Ignored, Released, Reflected }

    /// <summary>서버의 HANDOFF_RESULT 처리. 성공=공 해제, 실패=반사로 회수.</summary>
    public ResultKind OnResult(HandoffResultData r)
    {
        if (_pendingOutId == null || r.HandoffId != _pendingOutId) return ResultKind.Ignored;
        _pendingOutId = null;

        if (r.Accepted) return ResultKind.Released;

        // 실패(오프라인/타임아웃/거부): 저장한 나가던 속도를 안쪽으로 반사해 되돌린다.
        Vector2 n = HandoffMath.OutwardNormal(_pendingExitEdge);
        double vn = _savedVelocity.X * n.X + _savedVelocity.Y * n.Y;
        _physics.Velocity = _savedVelocity - n * ((1 + _settings.Restitution) * vn);
        _physics.SetPositionClamped(_physics.Position); // 화면 안으로 클램프
        return ResultKind.Reflected;
    }

    /// <summary>받은 HANDOFF 를 물리 엔진에 주입하고 ACK 를 보낸다. 성공 시 true(공 표시).</summary>
    public bool ApplyIncoming(Envelope env)
    {
        HandoffData? d = env.DataAs<HandoffData>();
        if (d == null) return false;

        // viaLink 로 링크를 찾아 진입 엣지·flip 을 얻는다(없으면 viaLink 파싱으로 폴백).
        EdgeLinkDto? link = _links.FirstOrDefault(l =>
            $"{l.From}.{l.FromEdge}->{l.To}.{l.ToEdge}" == d.ViaLink);

        Edge entry;
        bool flip;
        if (link != null && HandoffMath.TryParseEdge(link.ToEdge, out entry))
        {
            flip = link.Flip;
        }
        else if (!HandoffMath.TryEntryEdgeFromViaLink(d.ViaLink, out entry))
        {
            return false;
        }
        else
        {
            flip = false;
        }

        Bounds bounds = _bounds();
        double size = _settings.SlimeSize;
        HandoffMath.IncomingState s = HandoffMath.Unpack(d, entry, flip, bounds, size);

        // 진입 지점이 실제로 공을 놓을 수 있는 곳인지 검증한다. 통과 허용 판정(_area)이 아니라
        // 엄격 판정(Strict)으로 확인해야 작업표시줄·모니터 사이 빈 공간에 공을 놓고 갇히지 않는다.
        if (!HandoffMath.TryFindValidEntry(
                s.Position, entry, bounds, size,
                (x, y, sz) => _area.Strict.IsRectValid(new System.Windows.Rect(x, y, sz, sz)),
                out Vector2 spawn))
        {
            // 놓을 자리가 전혀 없음(받는 쪽 화면 구성이 이상) → 거부해서 보낸 쪽이 반사하게 한다.
            _send(new Envelope
            {
                Type = MsgType.Ack,
                From = SelfNodeId,
                To = env.From,
                Data = RelayJson.ToElement(new AckData { HandoffId = d.HandoffId, Accepted = false }),
            });
            return false;
        }

        _physics.Position = spawn;
        _physics.Velocity = s.Velocity;
        _physics.AngularVelocity = s.AngularVelocity;
        _physics.SurfaceSpin = s.SurfaceSpin;
        _physics.SpinShotDir = s.SpinShotDir;
        _physics.SpinAngle = s.SpinAngle;

        _send(new Envelope
        {
            Type = MsgType.Ack,
            From = SelfNodeId,
            To = env.From,
            Data = RelayJson.ToElement(new AckData { HandoffId = d.HandoffId, Accepted = true }),
        });
        return true;
    }
}
