using System.Linq;

namespace Slimey.Network;

/// <summary>
/// 방 참여 시 엣지 매핑 병합 규칙(순수 함수, UI 비의존).
///
/// 규칙: <b>자기 노드에서 나가는 링크는 그 PC 가 권위</b>를 갖고, 다른 노드의 링크는 서버 것을 보존한다.
/// 이렇게 해야 나중에 접속한 PC 의 매핑이 먼저 접속한 PC 가 심어놓은 매핑에 덮이지 않는다.
/// (덮이면 그 PC 는 나갈 수 있는 엣지가 없어 공이 벽에 튕기기만 한다.)
/// </summary>
public static class LinkMerge
{
    public static List<EdgeLinkDto> Merge(
        string selfNodeId,
        IEnumerable<EdgeLinkDto> localLinks,
        IEnumerable<EdgeLinkDto> serverLinks)
    {
        var server = serverLinks.ToList();
        var mine = localLinks.Where(l => l.From == selfNodeId).ToList();

        // 로컬에 내 매핑이 없으면(설정 미입력 등) 서버에 있는 내 매핑을 사용.
        if (mine.Count == 0) mine = server.Where(l => l.From == selfNodeId).ToList();

        var others = server.Where(l => l.From != selfNodeId).ToList();
        return others.Concat(mine).ToList();
    }

    /// <summary>두 링크 집합이 (순서 무관) 동일한가. 불필요한 ROOM_CONFIG 재전송 방지용.</summary>
    public static bool Same(IEnumerable<EdgeLinkDto> a, IEnumerable<EdgeLinkDto> b)
    {
        static string Key(EdgeLinkDto l) => $"{l.From}.{l.FromEdge}->{l.To}.{l.ToEdge}:{l.Flip}";
        var ka = a.Select(Key).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var kb = b.Select(Key).OrderBy(s => s, StringComparer.Ordinal).ToList();
        return ka.SequenceEqual(kb);
    }
}
