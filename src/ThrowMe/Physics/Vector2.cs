namespace ThrowMe.Physics;

/// <summary>
/// 물리 계산 전용 2D 벡터(double 정밀도).
/// WPF 의 System.Windows.Vector 를 직접 쓰지 않고 분리해
/// 물리 엔진이 UI 어셈블리에 의존하지 않도록 한다.
/// </summary>
public readonly struct Vector2
{
    public double X { get; }
    public double Y { get; }

    public Vector2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public static Vector2 Zero => new(0, 0);

    public double LengthSquared => X * X + Y * Y;
    public double Length => Math.Sqrt(LengthSquared);

    public Vector2 WithX(double x) => new(x, Y);
    public Vector2 WithY(double y) => new(X, y);

    /// <summary>길이 1 로 정규화. 길이가 0 이면 Zero 반환.</summary>
    public Vector2 Normalized()
    {
        double len = Length;
        return len > 1e-9 ? new Vector2(X / len, Y / len) : Zero;
    }

    /// <summary>최대 길이로 제한(방향 유지).</summary>
    public Vector2 ClampLength(double maxLength)
    {
        double lenSq = LengthSquared;
        if (lenSq <= maxLength * maxLength || lenSq < 1e-12)
            return this;
        double scale = maxLength / Math.Sqrt(lenSq);
        return new Vector2(X * scale, Y * scale);
    }

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator *(Vector2 a, double s) => new(a.X * s, a.Y * s);
    public static Vector2 operator *(double s, Vector2 a) => new(a.X * s, a.Y * s);
    public static Vector2 operator /(Vector2 a, double s) => new(a.X / s, a.Y / s);

    public override string ToString() => $"({X:0.##}, {Y:0.##})";
}
