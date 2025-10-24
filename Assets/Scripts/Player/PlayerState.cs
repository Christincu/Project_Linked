using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어의 상태 로직을 관리합니다.
/// 네트워크 변수는 PlayerController에 있으며, 이 클래스는 로직만 담당합니다.
/// MonoBehaviour로 작동하며 네트워크 기능은 PlayerController를 통해 접근합니다.
/// </summary>
public class PlayerState : MonoBehaviour
{
    #region Private Fields
    private PlayerController _controller;
    private InitialPlayerData _initialData;
    private float _manaRegenTimer = 0f;
    #endregion

    #region Properties (PlayerController의 네트워크 변수 참조)
    /// <summary>
    /// 현재 체력 (PlayerController에서 참조)
    /// </summary>
    public float CurrentHealth
    {
        get => _controller != null ? _controller.CurrentHealth : 0;
        set { if (_controller != null) _controller.CurrentHealth = value; }
    }

    /// <summary>
    /// 최대 체력 (PlayerController에서 참조)
    /// </summary>
    public float MaxHealth
    {
        get => _controller != null ? _controller.MaxHealth : 0;
        set { if (_controller != null) _controller.MaxHealth = value; }
    }

    /// <summary>
    /// 사망 상태 (PlayerController에서 참조)
    /// </summary>
    public bool IsDead
    {
        get => _controller != null && _controller.IsDead;
        set { if (_controller != null) _controller.IsDead = value; }
    }

    /// <summary>
    /// 무적 타이머 (PlayerController에서 참조)
    /// </summary>
    public TickTimer InvincibilityTimer
    {
        get => _controller != null ? _controller.InvincibilityTimer : default;
        set { if (_controller != null) _controller.InvincibilityTimer = value; }
    }

    /// <summary>
    /// 현재 마나 (PlayerController에서 참조)
    /// </summary>
    public float CurrentMana
    {
        get => _controller != null ? _controller.CurrentMana : 0;
        set { if (_controller != null) _controller.CurrentMana = value; }
    }

    /// <summary>
    /// 최대 마나 (PlayerController에서 참조)
    /// </summary>
    public float MaxMana
    {
        get => _controller != null ? _controller.MaxMana : 0;
        set { if (_controller != null) _controller.MaxMana = value; }
    }

    /// <summary>
    /// 체력 비율 (0 ~ 1)
    /// </summary>
    public float HealthPercentage => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;

    /// <summary>
    /// 마나 비율 (0 ~ 1)
    /// </summary>
    public float ManaPercentage => MaxMana > 0 ? CurrentMana / MaxMana : 0;

    /// <summary>
    /// 무적 상태 확인
    /// </summary>
    public bool IsInvincible => _controller != null && _controller.IsInvincible;
    #endregion

    #region Events
    public System.Action<float, float> OnHealthChanged; // (current, max)
    public System.Action<float, float> OnManaChanged; // (current, max)
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

        Debug.Log($"[PlayerState] Initialized - Controller: {_controller != null}");
    }

    void FixedUpdate()
    {
        if (_controller != null && _controller.Object != null && _controller.Object.HasStateAuthority)
        {
            // 마나 자동 회복
            RegenerateMana();
        }
    }
    #endregion

    #region Health Management
    /// <summary>
    /// 플레이어에게 데미지를 입힙니다 (서버에서만 실행).
    /// </summary>
    public void TakeDamage(float damage, PlayerRef attacker = default)
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasStateAuthority) return;
        if (IsDead || IsInvincible) return;

        // 데미지 적용
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        
        Debug.Log($"[PlayerState] {_controller.Object.InputAuthority} took {damage} damage. HP: {CurrentHealth}/{MaxHealth}");

        // 무적 타이머 시작
        if (_initialData != null && _controller.Runner != null)
        {
            InvincibilityTimer = TickTimer.CreateFromSeconds(_controller.Runner, _initialData.InvincibilityDuration);
        }

        // 이벤트 발생
        OnDamageTaken?.Invoke(damage);
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);

        // 체력이 0 이하면 사망 처리
        if (CurrentHealth <= 0)
        {
            Die(attacker);
        }
    }

    /// <summary>
    /// 플레이어를 치료합니다 (서버에서만 실행).
    /// </summary>
    public void Heal(float healAmount)
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasStateAuthority) return;
        if (IsDead) return;

        // 치료 적용
        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + healAmount);
        
        Debug.Log($"[PlayerState] {_controller.Object.InputAuthority} healed {healAmount}. HP: {CurrentHealth}/{MaxHealth}");

        // 이벤트 발생
        OnHealed?.Invoke(healAmount);
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }

    /// <summary>
    /// 플레이어를 완전 회복합니다 (서버에서만 실행).
    /// </summary>
    public void FullHeal()
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasStateAuthority) return;
        
        CurrentHealth = MaxHealth;
        
        Debug.Log($"[PlayerState] {_controller.Object.InputAuthority} fully healed. HP: {CurrentHealth}/{MaxHealth}");

        // 이벤트 발생
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }

    /// <summary>
    /// 플레이어를 사망 처리합니다 (서버에서만 실행).
    /// </summary>
    private void Die(PlayerRef killer = default)
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasStateAuthority) return;
        if (IsDead) return;

        IsDead = true;
        CurrentHealth = 0;
        
        Debug.Log($"[PlayerState] {_controller.Object.InputAuthority} died. Killer: {killer}");

        // 이벤트 발생
        OnDeath?.Invoke(killer);
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);

        // PlayerController에 알림
        if (_controller != null)
        {
            _controller.OnPlayerDied();
        }

        // 리스폰 처리
        if (_initialData != null && _initialData.CanRespawn)
        {
            StartCoroutine(RespawnAfterDelay(_initialData.RespawnDelay));
        }
    }

    /// <summary>
    /// 일정 시간 후 리스폰합니다.
    /// </summary>
    private IEnumerator RespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Respawn();
    }

    /// <summary>
    /// 플레이어를 리스폰합니다 (서버에서만 실행).
    /// </summary>
    public void Respawn(Vector3? spawnPosition = null)
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasStateAuthority) return;

        // 상태 초기화
        IsDead = false;
        CurrentHealth = _initialData != null ? _initialData.StartingHealth : MaxHealth;
        CurrentMana = MaxMana;

        Debug.Log($"[PlayerState] {_controller.Object.InputAuthority} respawned. HP: {CurrentHealth}/{MaxHealth}");

        // 이벤트 발생
        OnRespawned?.Invoke();
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        OnManaChanged?.Invoke(CurrentMana, MaxMana);

        // PlayerController에 알림
        if (_controller != null)
        {
            Vector3 respawnPos = spawnPosition ?? transform.position;
            _controller.OnPlayerRespawned(respawnPos);
        }
    }

    /// <summary>
    /// 최대 체력을 변경합니다 (서버에서만 실행).
    /// </summary>
    public void SetMaxHealth(float newMaxHealth)
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasStateAuthority) return;

        MaxHealth = newMaxHealth;
        CurrentHealth = Mathf.Min(CurrentHealth, MaxHealth);
        
        Debug.Log($"[PlayerState] Max health set to {MaxHealth}");

        // 이벤트 발생
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }
    #endregion

    #region Mana Management
    /// <summary>
    /// 마나를 자동으로 회복합니다.
    /// </summary>
    private void RegenerateMana()
    {
        if (IsDead) return;
        if (_controller == null || _controller.Runner == null) return;
        
        if (CurrentMana < MaxMana)
        {
            float regenRate = 10f; // 초당 10 마나 회복
            CurrentMana = Mathf.Min(MaxMana, CurrentMana + regenRate * _controller.Runner.DeltaTime);
            
            // 변경 감지용 타이머
            _manaRegenTimer += _controller.Runner.DeltaTime;
            if (_manaRegenTimer >= 0.5f) // 0.5초마다 이벤트 발생
            {
                _manaRegenTimer = 0f;
                OnManaChanged?.Invoke(CurrentMana, MaxMana);
            }
        }
    }

    /// <summary>
    /// 마나를 소모합니다 (서버에서만 실행).
    /// </summary>
    public bool ConsumeMana(float amount)
    {
        if (_controller == null || _controller.Object == null) return false;
        if (!_controller.Object.HasStateAuthority) return false;
        if (IsDead) return false;
        if (CurrentMana < amount) return false;

        CurrentMana = Mathf.Max(0, CurrentMana - amount);
        OnManaChanged?.Invoke(CurrentMana, MaxMana);
        return true;
    }

    /// <summary>
    /// 마나를 회복합니다 (서버에서만 실행).
    /// </summary>
    public void RestoreMana(float amount)
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasStateAuthority) return;

        CurrentMana = Mathf.Min(MaxMana, CurrentMana + amount);
        OnManaChanged?.Invoke(CurrentMana, MaxMana);
    }

    /// <summary>
    /// 마나를 완전히 회복합니다 (서버에서만 실행).
    /// </summary>
    public void FullRestoreMana()
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasStateAuthority) return;

        CurrentMana = MaxMana;
        OnManaChanged?.Invoke(CurrentMana, MaxMana);
    }

    /// <summary>
    /// 최대 마나를 변경합니다 (서버에서만 실행).
    /// </summary>
    public void SetMaxMana(float newMaxMana)
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasStateAuthority) return;

        MaxMana = newMaxMana;
        CurrentMana = Mathf.Min(CurrentMana, MaxMana);
        OnManaChanged?.Invoke(CurrentMana, MaxMana);
    }
    #endregion
}
