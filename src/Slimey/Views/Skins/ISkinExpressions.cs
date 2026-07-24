using Slimey.Models;

namespace Slimey.Views.Skins;

/// <summary>
/// 표정 변경을 지원하는 스킨이 구현하는 계약.
/// 표정이 없는 스킨(예: 당구공)은 구현하지 않으며, SlimeWindow 는
/// 스킨이 이 인터페이스를 구현할 때만 표정을 전달한다.
/// </summary>
public interface ISkinExpressions
{
    void SetExpression(SlimeExpression expression);
}
