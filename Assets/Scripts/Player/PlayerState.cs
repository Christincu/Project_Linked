using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어의 상태 로직을 관리합니다.
/// 네트워크 변수는 PlayerController에 있으며, 이 클래스는 로직만 담당합니다.
/// </summary>
public class PlayerState : MonoBehaviour
{
    #region Private Fields
    private PlayerController _controller;
    private InitialPlayerData _initialData;
    #endregion

    #region Properties (PlayerController의 네트워크 변수 참조)
    public float CurrentHealth
    {
        get => _controller != null ? _controller.CurrentHealth : 0;
        // set 로직은 PlayerController에서만 실행되도록 보호
        set { if (_controller != null) _controller.CurrentHealth = value; }
    }

    public float MaxHealth
    {
        get => _controller != null ? _controller.MaxHealth : 0;
        set { if (_controller != null) _controller.MaxHealth = value; }
    }

    public bool IsDead
    {
        get => _controller != null && _controller.IsDead;
        set { if (_controller != null) _controller.IsDead = value; }
    }

    public TickTimer InvincibilityTimer
    {
        get => _controller != null ? _controller.InvincibilityTimer : default;
        set { if (_controller != null) _controller.InvincibilityTimer = value; }
    }

    public TickTimer RespawnTimer
    {
        // ⭐ [전제] PlayerController에 RespawnTimer가 있다고 가정하고 참조
        get => _controller != null ? _controller.RespawnTimer : default;
        set { if (_controller != null) _controller.RespawnTimer = value; }
    }

    public float HealthPercentage => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;

    public bool IsInvincible => _controller != null && _controller.IsInvincible;
    #endregion

    #region Events
    public System.Action<float, float> OnHealthChanged; // (current, max)
    public System.Action<PlayerRef> OnDeath; // (killer)
    public System.Action OnRespawned;
    public System.Action<float> OnDamageTaken; // (damage)
    public System.Action<float> OnHealed; // (healAmount)
    #endregion

    #region Initialization
    /// <summary>
    /// PlayerController에서 호출하여 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController controller, InitialPlayerData initialData)
    {
        _controller = controller;
        _initialData = initialData;
    }

    // FixedUpdate는 결정론적이지 않으므로 제거합니다.
    // FixedUpdateNetwork에서 타이머를 확인하는 것이 안전합니다.
    #endregion

    #region Health Management
    /// <summary>
    /// 플레이어에게 데미지를 입힙니다 (서버/State Authority에서만 실행).
    /// </summary>
    public void TakeDamage(float damage, PlayerRef attacker = default)
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority) return;
        if (IsDead || IsInvincible) return;

        // 데미지 적용
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        
        // 무적 타이머 시작
        if (_initialData != null && _controller.Runner != null)
        {
            InvincibilityTimer = TickTimer.CreateFromSeconds(_controller.Runner, _initialData.InvincibilityDuration);
        }

        // 이벤트 발생
        OnDamageTaken?.Invoke(damage);
        // 네트워크 변수가 변경되면 OnChanged 콜백을 통해 UI가 업데이트되므로, OnHealthChanged 이벤트는 로컬 UI에만 필요할 수 있습니다.
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth); 

        // 체력이 0 이하면 사망 처리
        if (CurrentHealth <= 0)
        {
            Die(attacker);
        }
    }

    /// <summary>
    /// 플레이어를 치료합니다 (서버/State Authority에서만 실행).
    /// </summary>
    public void Heal(float healAmount)
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority) return;
        if (IsDead) return;

        // 치료 적용
        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + healAmount);
        
        // 이벤트 발생
        OnHealed?.Invoke(healAmount);
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }

    /// <summary>
    /// 플레이어를 완전 회복합니다 (서버/State Authority에서만 실행).
    /// </summary>
    public void FullHeal()
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority) return;
        
        CurrentHealth = MaxHealth;
        
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }

    /// <summary>
    /// 플레이어를 사망 처리합니다 (서버/State Authority에서만 실행).
    /// </summary>
    private void Die(PlayerRef killer = default)
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority) return;
        if (IsDead) return;

        IsDead = true;
        CurrentHealth = 0;
        
        // 이벤트 발생
        OnDeath?.Invoke(killer);
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);

        // ⭐ [개선] 리스폰 타이머 설정 (결정론적 TickTimer 사용)
        if (_initialData != null && _initialData.CanRespawn)
        {
            if (_controller.Runner != null)
            {
                // RespawnTimer 설정, 실제 Respawn은 PlayerController의 FixedUpdateNetwork에서 처리
                RespawnTimer = TickTimer.CreateFromSeconds(_controller.Runner, _initialData.RespawnDelay);
            }
        }
    }
    
    /// <summary>
    /// 플레이어를 리스폰합니다 (서버/State Authority에서만 실행).
    /// </summary>
    /// <param name="spawnPosition">리스폰 위치 (PlayerController가 처리할 수 있음)</param>
    public void Respawn(Vector3? spawnPosition = null)
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority) return;
        
        // 상태 초기화
        IsDead = false;
        CurrentHealth = _initialData != null ? _initialData.StartingHealth : MaxHealth;

        // ⭐ 리스폰 타이머 리셋
        RespawnTimer = TickTimer.None; 

        // PlayerController에 리스폰 위치 업데이트를 위임
        if (spawnPosition.HasValue)
        {
            _controller.OnPlayerRespawned(spawnPosition.Value);
        }
        else
        {
            // 리스폰 위치를 PlayerController의 기본 로직에 맡김
            _controller.OnPlayerRespawned(_controller.transform.position); 
        }

        // 이벤트 발생
        OnRespawned?.Invoke();
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }

    /// <summary>
    /// 최대 체력을 변경합니다 (서버/State Authority에서만 실행).
    /// </summary>
    public void SetMaxHealth(float newMaxHealth)
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority) return;

        MaxHealth = newMaxHealth;
        // 체력 감소 시 현재 체력 조정
        CurrentHealth = Mathf.Min(CurrentHealth, MaxHealth);
        
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }
    #endregion
}