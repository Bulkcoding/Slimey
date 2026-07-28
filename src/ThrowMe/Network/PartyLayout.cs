namespace ThrowMe.Network;

/// <summary>
/// 파티 순서(좌 → 우)를 엣지 매핑으로 변환하는 순수 로직.
///
/// 배치는 <b>좌우 일렬</b>만 사용한다(위/아래 없음). 순서가 [A, B, C] 라면
/// 화면이 A | B | C 로 나란히 붙어 있는 것처럼 동작한다:
///   A.Right ↔ B.Left,  B.Right ↔ C.Left
/// 양 끝(A의 왼쪽, C의 오른쪽)은 연결이 없으므로 평소처럼 벽에 튕긴다.
/// </summary>
public static class PartyLayout
{
    /// <summary>순서 목록에서 좌우 체인 링크(양방향)를 만든다.</summary>
    public static List<EdgeLinkDto> BuildChainLinks(IReadOnlyList<string> order)
    {
        var links = new List<EdgeLinkDto>();
        if (order == null) return links;

        // 빈 값·중복 제거(서버가 걸러주지만 방어).
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in order)
        {
            string id = (raw ?? "").Trim();
            if (id.Length > 0 && seen.Add(id)) ids.Add(id);
        }

        for (int i = 0; i + 1 < ids.Count; i++)
        {
            string left = ids[i], right = ids[i + 1];
            links.Add(new EdgeLinkDto
            {
                From = left, FromEdge = "Right", To = right, ToEdge = "Left", Flip = false,
            });
            links.Add(new EdgeLinkDto
            {
                From = right, FromEdge = "Left", To = left, ToEdge = "Right", Flip = false,
            });
        }
        return links;
    }

    /// <summary>두 링크 집합이 같은 배치인가(순서 무관 비교).</summary>
    public static bool SameLinks(IReadOnlyList<EdgeLinkDto> a, IReadOnlyList<EdgeLinkDto> b)
    {
        if (a.Count != b.Count) return false;
        var setA = a.Select(Key).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var setB = b.Select(Key).OrderBy(s => s, StringComparer.Ordinal).ToList();
        for (int i = 0; i < setA.Count; i++)
            if (!string.Equals(setA[i], setB[i], StringComparison.Ordinal)) return false;
        return true;

        static string Key(EdgeLinkDto l) => $"{l.From}.{l.FromEdge}->{l.To}.{l.ToEdge}:{l.Flip}";
    }
}
