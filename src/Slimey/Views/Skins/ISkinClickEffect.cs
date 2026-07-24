namespace Slimey.Views.Skins;

/// <summary>
/// 열림/닫힘 상태를 갖는 스킨(포켓몬 볼 계열)이 구현하는 계약.
/// 여닫는 판단은 SlimeWindow 가 상호작용(클릭/드래그)에 따라 결정한다.
/// </summary>
public interface ISkinClickEffect
{
    bool IsOpen { get; }
    void SetOpen(bool open);
}
