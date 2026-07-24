using Slimey.Models;
using Slimey.Physics;

namespace Slimey.Effects;

/// <summary>단일 파티클 상태(물리 픽셀 좌표).</summary>
public struct Particle
{
    public Vector2 Position;
    public Vector2 Velocity;
    public double Age;       // 경과 시간(초)
    public double LifeSpan;  // 총 수명(초)
    public double Size;      // px
    public ImpactTier Tier;  // 색/스타일 결정용(디자인 트랙)
    public bool Spark;       // true 면 당구 쿠션 스파크(밝은 색·작음)

    /// <summary>남은 수명 비율 1→0.</summary>
    public readonly double LifeFraction => LifeSpan <= 0 ? 0 : Math.Clamp(1.0 - Age / LifeSpan, 0, 1);
}

/// <summary>
/// 충돌·펀치 시 튀는 파티클을 시뮬레이션하는 순수 로직(렌더 비의존).
/// 좌표는 물리 픽셀. 렌더는 ParticleOverlayWindow(디자인 교체 가능)가 담당.
/// </summary>
public sealed class ParticleSystem
{
    private readonly AppSettings _settings;
    private readonly List<Particle> _particles = new(64);

    // 결정적 재현이 필요 없는 시각 효과이므로 런타임 Random 사용.
    private readonly Random _rng = new();

    public ParticleSystem(AppSettings settings) => _settings = settings;

    public IReadOnlyList<Particle> Active => _particles;
    public bool HasActive => _particles.Count > 0;

    /// <summary>충돌/펀치 지점에서 파티클 방출.</summary>
    /// <param name="origin">방출 중심(물리 픽셀).</param>
    /// <param name="intensity01">0~1 세기(개수·속도 스케일).</param>
    public void Emit(Vector2 origin, double intensity01, ImpactTier tier)
    {
        if (!_settings.ParticlesEnabled) return;

        intensity01 = Math.Clamp(intensity01, 0, 1);
        int count = (int)Math.Round(
            _settings.ParticleBaseCount +
            (_settings.ParticleMaxCount - _settings.ParticleBaseCount) * intensity01);
        if (count <= 0) return;

        double speedMin = _settings.ParticleSpeedMin;
        double speedMax = _settings.ParticleSpeedMax;

        for (int i = 0; i < count; i++)
        {
            double angle = _rng.NextDouble() * Math.PI * 2.0;
            // 세게 부딪힐수록 빠르게. 위쪽으로 살짝 편향(중력에 저항하며 튀는 느낌).
            double speed = (speedMin + _rng.NextDouble() * (speedMax - speedMin)) * (0.5 + 0.5 * intensity01);
            var vel = new Vector2(Math.Cos(angle) * speed, Math.Sin(angle) * speed - speed * 0.35);

            _particles.Add(new Particle
            {
                Position = origin,
                Velocity = vel,
                Age = 0,
                LifeSpan = _settings.ParticleLifeSeconds * (0.7 + _rng.NextDouble() * 0.6),
                Size = _settings.ParticleSize * (0.7 + _rng.NextDouble() * 0.6),
                Tier = tier,
            });
        }
    }

    /// <summary>
    /// 당구 쿠션 충돌 스파크. 벽면(법선의 접선)을 따라 양방향으로 튀며 살짝 안쪽으로 향한다.
    /// 짧고 빠르고 작은 밝은 입자 → "탁!" 하고 쿠션에 부딪히는 느낌.
    /// </summary>
    public void EmitCushion(Vector2 origin, Vector2 normal, double intensity01, ImpactTier tier)
    {
        if (!_settings.ParticlesEnabled) return;

        intensity01 = Math.Clamp(intensity01, 0, 1);
        if (normal.LengthSquared < 1e-6) normal = new Vector2(0, -1);
        normal = normal.Normalized();
        var tangent = new Vector2(-normal.Y, normal.X); // 벽면 방향

        int count = (int)Math.Round(
            _settings.ParticleBaseCount +
            (_settings.ParticleMaxCount - _settings.ParticleBaseCount) * intensity01);
        if (count <= 0) return;

        for (int i = 0; i < count; i++)
        {
            double along = _rng.NextDouble() * 2.0 - 1.0;      // 접선 -1..1(양방향)
            double outward = 0.12 + _rng.NextDouble() * 0.55;   // 벽에서 살짝 안쪽으로
            double speed = (_settings.ParticleSpeedMin + _rng.NextDouble() *
                            (_settings.ParticleSpeedMax - _settings.ParticleSpeedMin)) *
                           (0.7 + 0.6 * intensity01);

            Vector2 dir = (tangent * along + normal * outward).Normalized();
            _particles.Add(new Particle
            {
                Position = origin,
                Velocity = dir * speed,
                Age = 0,
                LifeSpan = _settings.ParticleLifeSeconds * 0.5 * (0.6 + _rng.NextDouble() * 0.5),
                Size = _settings.ParticleSize * 0.65 * (0.7 + _rng.NextDouble() * 0.5),
                Tier = tier,
                Spark = true,
            });
        }
    }

    /// <summary>
    /// 몬스터볼이 열리는 순간의 방사형 빛 입자. 사방으로 밝게 터지며 위로 살짝 편향.
    /// </summary>
    public void EmitOpen(Vector2 origin, double intensity01)
    {
        if (!_settings.ParticlesEnabled) return;

        intensity01 = Math.Clamp(intensity01, 0, 1);
        int count = (int)Math.Round(
            _settings.ParticleBaseCount +
            (_settings.ParticleMaxCount - _settings.ParticleBaseCount) * intensity01);
        if (count <= 0) return;

        for (int i = 0; i < count; i++)
        {
            double angle = _rng.NextDouble() * Math.PI * 2.0;
            double speed = (_settings.ParticleSpeedMin + _rng.NextDouble() *
                            (_settings.ParticleSpeedMax - _settings.ParticleSpeedMin)) *
                           (0.6 + 0.7 * intensity01);
            var vel = new Vector2(Math.Cos(angle) * speed, Math.Sin(angle) * speed - speed * 0.25);

            _particles.Add(new Particle
            {
                Position = origin,
                Velocity = vel,
                Age = 0,
                LifeSpan = _settings.ParticleLifeSeconds * 0.8 * (0.6 + _rng.NextDouble() * 0.6),
                Size = _settings.ParticleSize * (0.8 + _rng.NextDouble() * 0.7),
                Tier = ImpactTier.Bonk,
                Spark = true, // 밝게 렌더
            });
        }
    }

    /// <summary>deltaTime 진행. 수명이 다한 파티클 제거. 살아있는 게 있으면 true.</summary>
    public bool Update(double dt)
    {
        if (_particles.Count == 0) return false;

        double gravity = _settings.ParticleGravity;

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            Particle p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.LifeSpan)
            {
                // 스왑 후 말단 제거(O(1))
                _particles[i] = _particles[^1];
                _particles.RemoveAt(_particles.Count - 1);
                continue;
            }
            p.Velocity = new Vector2(p.Velocity.X, p.Velocity.Y + gravity * dt);
            p.Position = p.Position + p.Velocity * dt;
            _particles[i] = p;
        }

        return _particles.Count > 0;
    }

    public void Clear() => _particles.Clear();
}
