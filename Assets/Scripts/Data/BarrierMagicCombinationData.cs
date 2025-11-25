using UnityEngine;
using Fusion;

/// <summary>
/// 베리어 마법 조합 데이터 (Air + Soil)
/// 베리어 관련 모든 상수를 포함합니다.
/// </summary>
[CreateAssetMenu(fileName = "New Barrier Magic Combination", menuName = "Game/Barrier Magic Combination Data")]
public class BarrierMagicCombinationData : MagicCombinationData
{
    [Header("Barrier Settings")]
    [Tooltip("베리어 지속 시간 (초)")]
    public float barrierDuration = 10f;
    
    [Tooltip("베리어 적용 시 위협점수")]
    public float threatScore = 200f;
    
    [Tooltip("베리어 적용 시 이동속도 배율 (1.5 = 150%)")]
    public float moveSpeedMultiplier = 1.5f;
    
    [Tooltip("이동속도 효과가 적용되는 시간 구간 (초) - 예: 10초~3초까지")]
    public float moveSpeedEffectDuration = 7f; // 10초 - 3초 = 7초
    
    [Header("Explosion Settings - Phase 1")]
    [Tooltip("1단계 폭발 반지름 (미터)")]
    public float explosionRadiusPhase1 = 2.5f;
    
    [Tooltip("1단계 폭발 데미지")]
    public float explosionDamagePhase1 = 100f;
    
    [Header("Explosion Settings - Phase 2")]
    [Tooltip("2단계 폭발 반지름 (미터)")]
    public float explosionRadiusPhase2 = 4f;
    
    [Tooltip("2단계 폭발 데미지")]
    public float explosionDamagePhase2 = 200f;
    
    [Header("Explosion Settings - Phase 3")]
    [Tooltip("3단계 폭발 반지름 (미터)")]
    public float explosionRadiusPhase3 = 9f;
    
    [Tooltip("3단계 폭발 데미지")]
    public float explosionDamagePhase3 = 1000f;
    
    [Header("Health Settings")]
    [Tooltip("베리어를 받은 플레이어의 체력")]
    public float barrierReceiverHealth = 3f;
    
    [Tooltip("베리어를 받지 못한 플레이어의 체력")]
    public float nonReceiverHealth = 1f;
    
    [Header("Prefabs")]
    [Tooltip("베리어 마법 오브젝트 프리팹 (NetworkPrefabRef)")]
    public NetworkPrefabRef barrierMagicObjectPrefab;
    
    /// <summary>
    /// 남은 시간에 따라 폭발 반지름과 데미지를 반환합니다.
    /// </summary>
    public void GetExplosionData(float remainingTime, out float radius, out float damage)
    {
        if (remainingTime > 7f)
        {
            // Phase 1: 10초~7초
            radius = explosionRadiusPhase1;
            damage = explosionDamagePhase1;
        }
        else if (remainingTime > 3f)
        {
            // Phase 2: 7초~3초
            radius = explosionRadiusPhase2;
            damage = explosionDamagePhase2;
        }
        else
        {
            // Phase 3: 3초~0초
            radius = explosionRadiusPhase3;
            damage = explosionDamagePhase3;
        }
    }
    
    /// <summary>
    /// 현재 단계에 따라 폭발 반지름을 반환합니다 (시각화용).
    /// </summary>
    public float GetExplosionRadius(float remainingTime)
    {
        if (remainingTime > 7f) return explosionRadiusPhase1;
        if (remainingTime > 3f) return explosionRadiusPhase2;
        return explosionRadiusPhase3;
    }
}

