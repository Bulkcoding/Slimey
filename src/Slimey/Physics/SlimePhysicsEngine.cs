using System.Windows;
using Slimey.Models;

namespace Slimey.Physics;

/// <summary>
/// 한 번의 Update 결과. 애니메이션·효과음 트리거에 사용한다.
/// </summary>
public readonly struct PhysicsStepResult
{
    /// <summary>이번 프레임에 벽에 부딪혔는가.</summary>
    public bool Collided { get; init; }

    /// <summary>충돌 시 반전 직전의 속도 크기(px/s). 충돌 세기 판정용.</summary>
    public double MaxImpactSpeed { get; init; }

    /// <summary>속도가 0 이 되어 정지(수면) 상태인가. 렌더 루프 유휴화 판단용.</summary>
    public bool Sleeping { get; init; }

    /// <summary>최대 세기 충돌의 벽 법선(플레이 영역 안쪽 방향). 방향성 이펙트용.</summary>
    public Vector2 CollisionNormal { get; init; }

    /// <summary>최대 세기 충돌이 일어난 순간의 슬라임 top-left 위치(물리 픽셀).
    /// 프레임 종료 위치와 다르다 — 빠를수록 한 프레임에 멀리 가므로,
    /// 이펙트(파티클·문구·스파크)는 반드시 이 값을 써야 벽에 붙어 나온다.</summary>
    public Vector2 CollisionPosition { get; init; }
}

/// <summary>
/// 위치·속도·마찰·충돌·반사를 계산하는 순수 물리 엔진(UI 비의존).
/// 좌표는 물리 스크린 픽셀(top-left 기준).
///
/// 충돌 판정은 <see cref="IWalkableArea"/> 에 위임한다.
/// - 다음 위치가 다른 모니터로 연결되면 통과(IsRectValid == true)
/// - 어떤 모니터에도 포함되지 않으면 벽(반사)
/// - X/Y 축을 분리해 각 축의 충돌 방향을 판정한다.
/// </summary>
public sealed class SlimePhysicsEngine
{
    private readonly AppSettings _settings;

    public IWalkableArea Area { get; set; }

    /// <summary>슬라임 top-left 위치(물리 픽셀).</summary>
    public Vector2 Position { get; set; }

    /// <summary>속도(px/s).</summary>
    public Vector2 Velocity { get; set; }

    /// <summary>각속도(deg/s). 양수=시계방향(화면 기준). 옆으로 휘는 사이드 스핀(마그누스)에 사용.</summary>
    public double AngularVelocity { get; set; }

    /// <summary>표면 스핀(px/s). 샷 축 방향 가감속용. 양수=밀어치기(전진), 음수=끌어치기(되돌아옴).</summary>
    public double SurfaceSpin { get; set; }

    /// <summary>표면 스핀이 작용하는 샷 축(단위 벡터). 발사 시점의 진행 방향.</summary>
    public Vector2 SpinShotDir { get; set; }

    /// <summary>누적 회전각(deg). 시각 회전에 사용.</summary>
    public double SpinAngle { get; set; }

    public SlimePhysicsEngine(AppSettings settings, IWalkableArea area)
    {
        _settings = settings;
        Area = area;
    }

    public bool IsAtRest => Velocity.LengthSquared < 1e-6;

    private Rect RectFor(double x, double y) =>
        new(x, y, _settings.SlimeSize, _settings.SlimeSize);

    public bool IsCurrentPositionValid() => Area.IsRectValid(RectFor(Position.X, Position.Y));

    /// <summary>deltaTime(초) 기반으로 한 프레임 진행.</summary>
    public PhysicsStepResult Update(double dt)
    {
        // 이동도 회전도 표면스핀도 없을 때만 완전 정지로 간주.
        if (dt <= 0 || (IsAtRest
                        && Math.Abs(AngularVelocity) < _settings.SpinStopThreshold
                        && Math.Abs(SurfaceSpin) < _settings.SurfaceSpinStopThreshold))
        {
            AngularVelocity = 0;
            SurfaceSpin = 0;
            return new PhysicsStepResult { Sleeping = true };
        }

        // 1) 마찰(프레임 독립 지수 감쇠)
        Velocity *= Math.Exp(-_settings.Friction * dt);

        // 1.5) 표면 스핀(끌어치기/밀어치기): 샷 축으로 가감속.
        //   음수(끌어치기)면 진행 반대로 힘 → 전진하다 감속·반전해 되돌아온다.
        //   양수(밀어치기)면 진행 방향으로 힘 → 더 끝까지 밀고 나간다.
        if (Math.Abs(SurfaceSpin) > 1e-3)
        {
            Velocity += SpinShotDir * (SurfaceSpin * _settings.DrawFollowStrength * dt);
            SurfaceSpin *= Math.Exp(-_settings.SurfaceSpinFriction * dt);
            if (Math.Abs(SurfaceSpin) < _settings.SurfaceSpinStopThreshold) SurfaceSpin = 0;
        }

        // 2) 최대 속도 제한
        Velocity = Velocity.ClampLength(_settings.MaxSpeed);

        // 3) 터널링 방지: 이동량이 크면 substep 분할
        double travel = Velocity.Length * dt;
        int steps = Math.Max(1, (int)Math.Ceiling(travel / Math.Max(1.0, _settings.SubstepMaxPx)));
        double subDt = dt / steps;

        bool collided = false;
        double maxImpact = 0;
        Vector2 normal = Vector2.Zero;
        Vector2 hitPos = Position;   // 최대 세기 충돌 순간의 위치(프레임 끝 위치와 다름)

        for (int i = 0; i < steps; i++)
        {
            // ── X 축 ────────────────────────────────
            double dx = Velocity.X * subDt;
            if (dx != 0)
            {
                double tryX = Position.X + dx;
                if (Area.IsRectValid(RectFor(tryX, Position.Y)))
                {
                    Position = Position.WithX(tryX);
                }
                else
                {
                    // 벽 접촉 지점까지 이분 근접 후 반사
                    double contactX = ResolveContact(dx, isXAxis: true);
                    Position = Position.WithX(contactX);
                    double impactX = Math.Abs(Velocity.X);
                    if (impactX > maxImpact)
                    {
                        maxImpact = impactX;
                        normal = new Vector2(-Math.Sign(dx), 0); // 진행 반대 = 안쪽
                        hitPos = Position;                        // 벽에 닿은 그 지점
                    }
                    Velocity = Velocity.WithX(-Velocity.X * _settings.Restitution);
                    // 스핀이 벽을 물어 접선(세로) 방향으로 튀고, 스핀은 소모된다.
                    if (Math.Abs(AngularVelocity) > 1e-3)
                    {
                        Velocity = Velocity.WithY(Velocity.Y + AngularVelocity * _settings.SpinWallKick);
                        AngularVelocity *= _settings.SpinWallRetain;
                    }
                    collided = true;
                }
            }

            // ── Y 축 ────────────────────────────────
            double dy = Velocity.Y * subDt;
            if (dy != 0)
            {
                double tryY = Position.Y + dy;
                if (Area.IsRectValid(RectFor(Position.X, tryY)))
                {
                    Position = Position.WithY(tryY);
                }
                else
                {
                    double contactY = ResolveContact(dy, isXAxis: false);
                    Position = Position.WithY(contactY);
                    double impactY = Math.Abs(Velocity.Y);
                    if (impactY > maxImpact)
                    {
                        maxImpact = impactY;
                        normal = new Vector2(0, -Math.Sign(dy)); // 진행 반대 = 안쪽
                        hitPos = Position;                        // 벽에 닿은 그 지점
                    }
                    Velocity = Velocity.WithY(-Velocity.Y * _settings.Restitution);
                    // 스핀이 벽을 물어 접선(가로) 방향으로 튀고, 스핀은 소모된다.
                    if (Math.Abs(AngularVelocity) > 1e-3)
                    {
                        Velocity = Velocity.WithX(Velocity.X + AngularVelocity * _settings.SpinWallKick);
                        AngularVelocity *= _settings.SpinWallRetain;
                    }
                    collided = true;
                }
            }
        }

        // 3.5) 스핀: 마그누스로 궤적을 휘게 + 각속도 감쇠 + 회전각 적분
        if (Math.Abs(AngularVelocity) > 1e-3 && Velocity.LengthSquared > 1.0)
        {
            double speed = Velocity.Length;
            // 속도에 수직인 방향으로 휘게(마그누스)
            Vector2 perp = new Vector2(-Velocity.Y, Velocity.X) / speed;
            double curveAccel = _settings.MagnusStrength * AngularVelocity * speed;
            Velocity += perp * (curveAccel * dt);
        }
        SpinAngle += AngularVelocity * dt;
        AngularVelocity *= Math.Exp(-_settings.SpinFriction * dt);
        if (Math.Abs(AngularVelocity) < _settings.SpinStopThreshold)
            AngularVelocity = 0;

        // 4) 저속 정지(진동 방지). 단, 표면 스핀이 남아 있으면(끌어치기 반전 지점 등)
        //    아직 가속할 힘이 있으므로 속도를 죽이지 않는다.
        bool sleeping = false;
        if (Velocity.Length < _settings.StopThreshold
            && Math.Abs(SurfaceSpin) < _settings.SurfaceSpinStopThreshold)
        {
            Velocity = Vector2.Zero;
            sleeping = true;
        }
        // 아직 회전 중이거나 표면 스핀이 남아 있으면 수면 아님
        if (Math.Abs(AngularVelocity) >= _settings.SpinStopThreshold
            || Math.Abs(SurfaceSpin) >= _settings.SurfaceSpinStopThreshold)
            sleeping = false;

        return new PhysicsStepResult
        {
            Collided = collided,
            MaxImpactSpeed = maxImpact,
            Sleeping = sleeping,
            CollisionNormal = normal,
            CollisionPosition = hitPos,
        };
    }

    /// <summary>
    /// 현재 위치(유효)에서 delta 만큼 이동하면 무효가 될 때,
    /// 벽에 최대한 근접한 이동량을 이분 탐색으로 구해 접촉 좌표를 반환한다.
    /// </summary>
    private double ResolveContact(double delta, bool isXAxis)
    {
        double lo = 0.0;   // 유효
        double hi = delta; // 무효
        for (int k = 0; k < 6; k++)
        {
            double mid = (lo + hi) * 0.5;
            Rect r = isXAxis
                ? RectFor(Position.X + mid, Position.Y)
                : RectFor(Position.X, Position.Y + mid);
            if (Area.IsRectValid(r)) lo = mid;
            else hi = mid;
        }
        return (isXAxis ? Position.X : Position.Y) + lo;
    }

    /// <summary>드래그 등 직접 배치 시 가상 데스크톱 밖으로 나가지 않게 클램프.</summary>
    public void SetPositionClamped(Vector2 pos)
    {
        Rect vb = Area.VirtualBounds;
        double x = Math.Clamp(pos.X, vb.Left, Math.Max(vb.Left, vb.Right - _settings.SlimeSize));
        double y = Math.Clamp(pos.Y, vb.Top, Math.Max(vb.Top, vb.Bottom - _settings.SlimeSize));
        Position = new Vector2(x, y);
    }
}
