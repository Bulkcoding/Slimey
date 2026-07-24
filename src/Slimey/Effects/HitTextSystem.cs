using Slimey.Physics;

namespace Slimey.Effects;

/// <summary>떠오르는 타격 문구 하나(물리 픽셀 좌표). 메이플스토리 데미지 문구 느낌.</summary>
public struct HitText
{
    public string Text;
    public Vector2 Origin;   // 시작 지점(물리 픽셀)
    public double Age;       // 경과(초)
    public double LifeSpan;  // 총 수명(초)
    public double Power01;   // 0~1 세기(글자 크기·색)
    public double DriftX;    // 가로 흘림(px/s)
    public int Style;        // 색 스타일 인덱스

    /// <summary>팝인: 0.0~0.12s 사이 작게→살짝 오버슈트→1.0.</summary>
    public readonly double Scale
    {
        get
        {
            double t = Age / 0.14;
            if (t >= 1) return 1.0;
            // easeOutBack 비슷하게 살짝 튀어오름
            double u = t - 1;
            return 1 + (u * u * ((1.70158 + 1) * u + 1.70158)); // 0→1 오버슈트
        }
    }

    /// <summary>수명 후반 60%부터 서서히 사라짐.</summary>
    public readonly double Opacity
    {
        get
        {
            double f = LifeSpan <= 0 ? 0 : Age / LifeSpan;
            if (f < 0.6) return 1.0;
            return Math.Clamp(1.0 - (f - 0.6) / 0.4, 0, 1);
        }
    }

    /// <summary>시작점 대비 위로 떠오른 거리(px, 양수=위로). 처음 빠르게 튀어오르고 점점 느려짐.</summary>
    public readonly double RiseY
    {
        get
        {
            double f = LifeSpan <= 0 ? 0 : Math.Clamp(Age / LifeSpan, 0, 1);
            double eased = 1 - Math.Pow(1 - f, 2); // easeOutQuad
            return 46.0 * eased;
        }
    }

    public readonly double CurrentX => Origin.X + DriftX * Age;
    public readonly double CurrentY => Origin.Y - RiseY;
}

/// <summary>타격 문구를 관리하는 순수 로직. 렌더는 HitTextOverlayWindow 담당.</summary>
public sealed class HitTextSystem
{
    private readonly List<HitText> _items = new(16);
    private readonly Random _rng = new();

    private static readonly string[] Words =
        { "Hit!", "Bonk!", "Pow!", "Bam!", "Smack!", "Whack!", "Boing!", "Ouch!", "Pang!" };

    public IReadOnlyList<HitText> Active => _items;
    public bool HasActive => _items.Count > 0;

    /// <summary>타격 지점에서 랜덤 문구 하나 띄우기.</summary>
    /// <param name="origin">중심(물리 픽셀).</param>
    /// <param name="power01">0~1 세기(글자 크기·색).</param>
    public void Spawn(Vector2 origin, double power01)
    {
        power01 = Math.Clamp(power01, 0, 1);
        _items.Add(new HitText
        {
            Text = Words[_rng.Next(Words.Length)],
            Origin = origin,
            Age = 0,
            LifeSpan = 0.75 + _rng.NextDouble() * 0.25,
            Power01 = power01,
            DriftX = (_rng.NextDouble() * 2 - 1) * 28.0,
            Style = _rng.Next(3),
        });

        // 화면 폭주 방지: 오래된 것부터 제거
        if (_items.Count > 12) _items.RemoveAt(0);
    }

    /// <summary>deltaTime 진행. 수명 다한 문구 제거. 살아있으면 true.</summary>
    public bool Update(double dt)
    {
        if (_items.Count == 0) return false;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            HitText h = _items[i];
            h.Age += dt;
            if (h.Age >= h.LifeSpan)
            {
                _items[i] = _items[^1];
                _items.RemoveAt(_items.Count - 1);
                continue;
            }
            _items[i] = h;
        }
        return _items.Count > 0;
    }

    public void Clear() => _items.Clear();
}
