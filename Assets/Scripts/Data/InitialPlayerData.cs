using System;
using UnityEngine;

/// <summary>
/// 플레이어의 초기 데이터를 정의합니다.
/// 이동 속도, 체력 등 게임 시작 시 필요한 기본 값들을 포함합니다.
/// </summary>
[Serializable]
public class InitialPlayerData
{
    [Header("Movement Settings")]
    [Tooltip("기본 이동 속도")]
    [SerializeField] private float moveSpeed = 5f;
    
    [Tooltip("최대 이동 속도")]
    [SerializeField] private float maxVelocity = 8f;
    
    [Tooltip("가속도 (1이면 즉시 반응, 0에 가까울수록 부드러운 가속)")]
    [SerializeField, Range(0.1f, 1f)] private float acceleration = 1f;

    [Header("Health Settings")]
    [Tooltip("최대 체력 (하트 하나당 1HP)")]
    [SerializeField] private float maxHealth = 3f;
    
    [Tooltip("시작 체력 (최대 체력 이하여야 함)")]
    [SerializeField] private float startingHealth = 3f;
    
    [Tooltip("체력 자동 회복 여부")]
    [SerializeField] private bool enableHealthRegeneration = false;
    
    [Tooltip("초당 회복량")]
    [SerializeField] private float healthRegenerationRate = 5f;
    
    [Tooltip("회복 시작 전 대기 시간 (초)")]
    [SerializeField] private float healthRegenerationDelay = 3f;

    [Header("Combat Settings")]
    [Tooltip("무적 시간 (피격 후 무적 시간, 초)")]
    [SerializeField] private float invincibilityDuration = 1f;
    
    [Tooltip("사망 시 리스폰 가능 여부")]
    [SerializeField] private bool canRespawn = true;
    
    [Tooltip("리스폰 대기 시간 (초)")]
    [SerializeField] private float respawnDelay = 3f;

    #region Properties
    public float MoveSpeed => moveSpeed;
    public float MaxVelocity => maxVelocity;
    public float Acceleration => acceleration;
    public float MaxHealth => maxHealth;
    public float StartingHealth => Mathf.Min(startingHealth, maxHealth);
    public bool EnableHealthRegeneration => enableHealthRegeneration;
    public float HealthRegenerationRate => healthRegenerationRate;
    public float HealthRegenerationDelay => healthRegenerationDelay;
    public float InvincibilityDuration => invincibilityDuration;
    public bool CanRespawn => canRespawn;
    public float RespawnDelay => respawnDelay;
    #endregion

    /// <summary>
    /// 데이터 유효성을 검증합니다.
    /// </summary>
    public void Validate()
    {
        if (moveSpeed <= 0)
        {
            Debug.LogWarning("InitialPlayerData: moveSpeed는 0보다 커야 합니다. 기본값 5로 설정됩니다.");
            moveSpeed = 5f;
        }

        if (maxVelocity <= 0)
        {
            Debug.LogWarning("InitialPlayerData: maxVelocity는 0보다 커야 합니다. 기본값 8로 설정됩니다.");
            maxVelocity = 8f;
        }

        if (maxHealth <= 0)
        {
            Debug.LogWarning("InitialPlayerData: maxHealth는 0보다 커야 합니다. 기본값 3으로 설정됩니다.");
            maxHealth = 3f;
        }

        if (startingHealth > maxHealth)
        {
            Debug.LogWarning($"InitialPlayerData: startingHealth({startingHealth})가 maxHealth({maxHealth})보다 큽니다. maxHealth로 조정됩니다.");
            startingHealth = maxHealth;
        }
        
        if (startingHealth <= 0)
        {
            Debug.LogWarning("InitialPlayerData: startingHealth는 0보다 커야 합니다. maxHealth로 설정됩니다.");
            startingHealth = maxHealth;
        }
    }

    /// <summary>
    /// 기본값으로 초기화합니다.
    /// </summary>
    public void SetDefaults()
    {
        moveSpeed = 5f;
        maxVelocity = 8f;
        acceleration = 1f;
        maxHealth = 3f;
        startingHealth = 3f;
        enableHealthRegeneration = false;
        healthRegenerationRate = 1f;
        healthRegenerationDelay = 3f;
        invincibilityDuration = 1f;
        canRespawn = true;
        respawnDelay = 3f;
    }
}
