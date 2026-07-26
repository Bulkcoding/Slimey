namespace Slimey.Views.Skins;

/// <summary>
/// 굴러가는 동안 표면 무늬가 바뀌는 스킨(볼링공 등)이 구현하는 계약.
/// SlimeWindow 는 물리 이동량을 "공 지름 기준 회전수"로 환산해 매 프레임 전달하고,
/// 스킨은 누적 회전수에 맞춰 보이는 면(무늬)을 교체한다.
/// 이 인터페이스를 구현하지 않는 스킨에는 아무것도 전달하지 않는다.
/// </summary>
public interface ISkinRolling
{
    /// <summary>
    /// 이번 프레임에 공이 굴러간 회전수(부호 포함). 1.0 = 한 바퀴.
    /// 부호는 굴러가는 방향(양수=오른쪽 진행)이며, 스킨은 이 방향으로 면을 넘긴다.
    /// </summary>
    void OnRoll(double revolutions);
}
