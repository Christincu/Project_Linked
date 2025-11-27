using UnityEngine;
using Fusion;

/// <summary>
/// 화염 돌진 마법 조합 데이터
/// 두 플레이어의 마법 합체 발동 시 적용되는 특수 돌진 스킬 데이터
/// </summary>
[CreateAssetMenu(fileName = "New Dash Magic Combination", menuName = "Game/Dash Magic Combination Data")]
public class DashMagicCombinationData : MagicCombinationData
{
    [Header("Initial Settings")]
    [Tooltip("스킬 발동 시 정지 시간 (초)")]
    public float initialStunDuration = 0.5f;
    
    [Tooltip("카메라 줌 아웃 배율")]
    public float cameraZoomOutMultiplier = 3f;
    
    [Header("Movement Settings")]
    [Tooltip("최대 속도 (m/s)")]
    public float maxSpeed = 20f;
    
    [Tooltip("이동 상태에서 추가 가속도 (m/s²)")]
    public float movementAcceleration = 2f;
    
    [Tooltip("자연 감속도 (m/s²)")]
    public float deceleration = 4f;
    
    [Header("Skill Stats")]
    [Tooltip("기본 지속 시간 (초)")]
    public float baseDuration = 10f;
    
    [Tooltip("최대 지속 시간 (초)")]
    public float maxDuration = 15f;
    
    [Tooltip("기본 충돌 피해")]
    public float baseDamage = 30f;
    
    [Tooltip("충돌 시 지속 시간 감소량 (초)")]
    public float durationReductionOnHit = 1.5f;
    
    [Header("Enhancement Settings")]
    [Tooltip("강화 1회 당 지속 시간 증가량 (초)")]
    public float durationIncreasePerEnhancement = 7.5f;
    
    [Tooltip("강화 1회 당 피해 증가량")]
    public float damageIncreasePerEnhancement = 10f;
    
    [Tooltip("최종 강화에 필요한 강화 횟수")]
    public int finalEnhancementCount = 3;
    
    [Header("Final Enhancement Settings")]
    [Tooltip("최종 강화 지속 시간 (초) - 잔여 시간과 관계없이 고정")]
    public float finalEnhancementDuration = 8f;
    
    [Tooltip("최종 강화 피해 증가량")]
    public float finalEnhancementDamageBonus = 50f;
    
    [Tooltip("최종 강화 이동 속도 배율 (기본 속도 * 이 값)")]
    public float finalEnhancementSpeedMultiplier = 2f;
    
    [Header("Collision Settings")]
    [Tooltip("플레이어 충돌 시 정면 범위 (도)")]
    public float playerCollisionFrontAngle = 120f;
    
    [Tooltip("적 충돌 후 재충돌 방지 시간 (초)")]
    public float enemyCollisionCooldown = 0.5f;
    
    [Tooltip("적 넉백 각도 범위 (±도)")]
    public float enemyKnockbackAngleRange = 45f;
    
    [Tooltip("적 넉백 힘")]
    public float enemyKnockbackForce = 15f;
    
    [Tooltip("적 넉백 지속 시간 (초) - NavMeshAgent 무시 시간")]
    public float enemyKnockbackDuration = 0.3f;
    
    [Header("Player Collision Settings")]
    [Tooltip("플레이어 충돌 시 멈추는 시간 (초)")]
    public float playerCollisionFreezeDuration = 0.5f;
    
    [Tooltip("플레이어 충돌 후 튕겨나가는 힘")]
    public float playerCollisionRecoilForce = 20f;
    
    [Tooltip("플레이어 충돌 후 튕겨나갈 때 스킬 지속시간 연장량 (초)")]
    public float playerCollisionRecoilTimeExtension = 0.5f;
    
    [Header("Penalty Settings")]
    [Tooltip("잘못된 충돌 시 행동 불능 시간 (초)")]
    public float wrongCollisionStunDuration = 2f;
    
    [Header("Prefabs")]
    [Tooltip("화염 돌진 베리어 오브젝트 프리팹")]
    public GameObject dashBarrierPrefab;
    
    [Header("Barrier Sprites")]
    [Tooltip("기본 베리어 스프라이트 (강화 0회)")]
    public Sprite baseBarrierSprite;
    
    [Tooltip("강화 베리어 스프라이트 (강화 1회)")]
    public Sprite enhancedBarrierSprite;
    
    [Tooltip("최종 강화 베리어 스프라이트 (강화 2회)")]
    public Sprite finalEnhancementBarrierSprite;
}

