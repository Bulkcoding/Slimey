namespace ThrowMe.Services;

/// <summary>
/// 자동 업데이트 설정.
///
/// 저장소가 공개(Public)이므로 릴리스 조회·다운로드에 토큰이 필요 없다(권장 상태).
/// 만약 다시 비공개로 돌린다면, "ThrowMe 저장소 Contents 읽기 전용"으로 제한한
/// fine-grained PAT 를 EmbeddedToken 에 넣거나 로컬 update_token.txt 에 두면 된다.
/// </summary>
public static class UpdateConfig
{
    public const string Owner = "Bulkcoding";
    public const string Repo = "ThrowMe";

    /// <summary>공개 저장소면 비워 둔다. (비공개 전환 시에만 읽기전용 PAT 사용)</summary>
    public const string EmbeddedToken = "";

    public static readonly bool Enabled = true;
}
