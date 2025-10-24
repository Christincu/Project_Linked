using System;
using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어의 체력 정보를 저장하는 네트워크 동기화 구조체
/// </summary>
[System.Serializable]
public struct PlayerHealthData : INetworkStruct
{
    [Networked] public float CurrentHealth { get; set; }
    [Networked] public float MaxHealth { get; set; }
    [Networked] public NetworkBool IsDead { get; set; }
    [Networked] public TickTimer InvincibilityTimer { get; set; }

    /// <summary>
    /// 체력 비율 (0 ~ 1)
    /// </summary>
    public float HealthPercentage => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;

    /// <summary>
    /// 무적 상태 확인
    /// </summary>
    public bool IsInvincible(NetworkRunner runner) => !InvincibilityTimer.ExpiredOrNotRunning(runner);

    /// <summary>
    /// 초기화
    /// </summary>
    public static PlayerHealthData Create(float maxHealth, float startingHealth)
    {
        return new PlayerHealthData
        {
            MaxHealth = maxHealth,
            CurrentHealth = startingHealth,
            IsDead = false
        };
    }
}

