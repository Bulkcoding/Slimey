using System.Windows;
using ThrowMe.Physics;

namespace ThrowMe.Network;

/// <summary>
/// 로컬 모니터 판정(<see cref="IWalkableArea"/>)을 감싸, <b>연결된 엣지 방향으로는 밖으로 나가도 유효</b>
/// 하게 만든다. 그래야 물리 엔진이 그 엣지에서 반사하지 않고 공이 속도를 유지한 채 경계를 넘고,
/// <see cref="BallHandoffCoordinator"/> 가 그 교차를 감지해 핸드오프한다.
///
/// - 연결 없는 엣지/모니터 사이 빈 공간 = 기존대로 무효(반사).
/// - <see cref="LinkedExitEdges"/> 에 있는 바깥 경계 방향만 나가도록 허용.
/// 설계문서 10절의 "Coordinator 가 로컬 area 를 감싸는 방식".
/// </summary>
public sealed class NetworkedWalkableArea : IWalkableArea
{
    private readonly IWalkableArea _inner;

    /// <summary>이 노드에서 나갈 수 있는(연결된) 바깥 경계 방향.</summary>
    public HashSet<Edge> LinkedExitEdges { get; } = new();

    /// <summary>
    /// 통과 허용을 적용하지 않은 <b>엄격한</b> 로컬 판정(모니터 작업영역 union).
    /// 공을 새로 놓을 위치(핸드오프 진입 지점)는 반드시 이쪽으로 검증해야 한다 —
    /// 통과 허용 판정으로 검사하면 작업표시줄/화면 밖에 공을 놓고 갇히게 된다.
    /// </summary>
    public IWalkableArea Strict => _inner;

    public NetworkedWalkableArea(IWalkableArea inner) => _inner = inner;

    public Rect VirtualBounds => _inner.VirtualBounds;

    public bool IsRectValid(Rect rect)
    {
        if (_inner.IsRectValid(rect)) return true;

        // 로컬 무효. 바깥 경계를 넘어서 무효가 된 것인지(엣지), 모니터 사이 빈 공간인지 구분.
        Rect vb = _inner.VirtualBounds;
        const double eps = 0.5;
        bool exceedsLeft = rect.Left < vb.Left - eps;
        bool exceedsRight = rect.Right > vb.Right + eps;
        bool exceedsTop = rect.Top < vb.Top - eps;
        bool exceedsBottom = rect.Bottom > vb.Bottom + eps;

        // 바깥으로 넘지 않았는데 무효 = 모니터 사이 빈 공간 → 반사.
        if (!(exceedsLeft || exceedsRight || exceedsTop || exceedsBottom)) return false;

        // 넘어간 방향 중 하나라도 연결 안 된 엣지면 반사.
        if (exceedsLeft && !LinkedExitEdges.Contains(Edge.Left)) return false;
        if (exceedsRight && !LinkedExitEdges.Contains(Edge.Right)) return false;
        if (exceedsTop && !LinkedExitEdges.Contains(Edge.Top)) return false;
        if (exceedsBottom && !LinkedExitEdges.Contains(Edge.Bottom)) return false;

        // 넘어간 방향이 모두 연결된 엣지 → 통과(나가도록 허용). Coordinator 가 잡는다.
        return true;
    }
}
