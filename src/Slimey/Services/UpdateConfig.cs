namespace Slimey.Services;

/// <summary>
/// 자동 업데이트 설정.
///
/// 비공개(Private) 저장소라 릴리스 조회에 GitHub 토큰이 필요하다.
/// ⚠ 배포 exe 에서 토큰이 추출될 수 있으므로, 반드시
///   "Slimey 저장소 하나만, Contents 읽기 전용"으로 제한한 fine-grained PAT 를 넣을 것.
///   (그 토큰이 유출돼도 이미 exe 에 든 이 소스만 읽히므로 노출 피해가 최소.)
///
/// Token 이 비어 있으면 자동 업데이트는 조용히 비활성화된다(앱 동작에는 영향 없음).
/// 토큰은 로컬 파일에서 우선 읽고(개발 편의), 없으면 아래 상수를 사용한다.
/// </summary>
public static class UpdateConfig
{
    public const string Owner = "Bulkcoding";
    public const string Repo = "Slimey";

    /// <summary>배포 빌드 전 fine-grained 읽기 전용 PAT 를 여기에 넣는다. 비우면 업데이트 비활성.</summary>
    public const string EmbeddedToken = "";

    public static readonly bool Enabled = true;
}
