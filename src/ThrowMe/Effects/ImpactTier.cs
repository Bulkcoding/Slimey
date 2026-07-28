using ThrowMe.Models;

namespace ThrowMe.Effects;

/// <summary>충돌 세기 단계. 소리·파티클·찌그러짐 강도를 결정한다.</summary>
public enum ImpactTier
{
    /// <summary>효과를 낼 만큼 세지 않음.</summary>
    None,
    /// <summary>약한 튕김 — BOING.</summary>
    Boing,
    /// <summary>중간 충돌 — SPLAT.</summary>
    Splat,
    /// <summary>강한 충돌 — BONK.</summary>
    Bonk,
}

/// <summary>충돌 속도를 설정 임계값과 비교해 단계로 분류한다.</summary>
public static class ImpactClassifier
{
    /// <param name="impactSpeed">반전 직전 속도 크기(px/s).</param>
    public static ImpactTier Classify(double impactSpeed, AppSettings s)
    {
        double maxSpeed = s.ImpactReferenceSpeed <= 0 ? 1.0 : s.ImpactReferenceSpeed;
        double ratio = impactSpeed / maxSpeed;

        if (ratio < s.ImpactSoftFraction) return ImpactTier.None;
        if (ratio < s.ImpactMediumFraction) return ImpactTier.Boing;
        if (ratio < s.ImpactHardFraction) return ImpactTier.Splat;
        return ImpactTier.Bonk;
    }

    /// <summary>0~1 정규화 세기(파티클 수·볼륨 스케일용).</summary>
    public static double Intensity01(double impactSpeed, AppSettings s)
    {
        double maxSpeed = s.ImpactReferenceSpeed <= 0 ? 1.0 : s.ImpactReferenceSpeed;
        return Math.Clamp(impactSpeed / maxSpeed, 0.0, 1.0);
    }
}
