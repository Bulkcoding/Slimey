namespace ThrowMe.Views.Skins;

/// <summary>
/// 벽/골대에 부딪힐 때 알림을 받고 싶은 스킨이 구현하는 계약.
/// 농구공은 이 알림마다 씸(seam) 무늬를 바꿔 "튈 때마다 모양이 달라지는" 느낌을 준다.
/// 표정/열림과 달리, 상태 없는 순간 이벤트다.
/// </summary>
public interface ISkinBounce
{
    /// <summary>공이 무언가에 부딪힌 순간 호출.</summary>
    void OnBounce();
}
