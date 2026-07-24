namespace Slimey.Models;

/// <summary>슬라임 표정 상태. 속도/충돌에 따라 SlimeWindow 가 결정해 스킨에 전달한다.</summary>
public enum SlimeExpression
{
    /// <summary>평상시(정지·저속).</summary>
    Normal = 0,

    /// <summary>빠르게 날아가는 중(신남).</summary>
    Flying = 1,

    /// <summary>강하게 부딪힌 직후(어질~).</summary>
    Dizzy = 2,
}
