using System.Windows;

namespace ThrowMe.Physics;

/// <summary>
/// 슬라임이 존재할 수 있는 유효 영역을 판정하는 추상화.
/// 물리 엔진이 모니터 서비스(UI/WinForms)에 직접 의존하지 않게 분리해
/// 단위 테스트 시 가짜 배치를 주입할 수 있도록 한다.
/// 좌표는 물리 스크린 픽셀(가상 데스크톱 좌표계, 음수 가능).
/// </summary>
public interface IWalkableArea
{
    /// <summary>
    /// 주어진 사각형(슬라임 박스)이 유효한가.
    /// 여러 모니터에 걸쳐 있어도 전부 화면 위라면 true(인접 경계 통과),
    /// 일부라도 빈 좌표/외곽으로 벗어나면 false(벽).
    /// </summary>
    bool IsRectValid(Rect rect);

    /// <summary>모든 모니터를 감싸는 가상 데스크톱 경계(드래그 이탈 방지용).</summary>
    Rect VirtualBounds { get; }
}
