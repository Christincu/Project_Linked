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

    #region Properties
    /// <summary>
    /// State Authority 확인 헬퍼 프로퍼티 (중복 체크 제거)
    /// </summary>
    private bool IsServer => _controller != null && _controller.Object != null && _controller.Object.HasStateAuthority;

    public float CurrentHealth
    {
        get => _controller?.CurrentHealth ?? 0;
        private set { if (_controller != null) _controller.CurrentHealth = value; }
    }

    public float MaxHealth
    {
        get => _controller?.MaxHealth ?? 0;
        set { if (_controller != null) _controller.MaxHealth = value; }
    }

    public bool IsDead
    {
        get => _controller != null && _controller.IsDead;
        private set { if (_controller != null) _controller.IsDead = value; }
    }

    public TickTimer InvincibilityTimer
    {
        get => _controller?.InvincibilityTimer ?? default;
        set { if (_controller != null) _controller.InvincibilityTimer = value; }
    }

    public TickTimer RespawnTimer
    {
        get => _controller?.RespawnTimer ?? default;
        set { if (_controller != null) _controller.RespawnTimer = value; }
    }

    public float HealthPercentage => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;

    /// <summary>
    /// 무적 상태인지 확인합니다.
    /// 일반 무적 타이머 또는 Dash Magic 최종 강화 상태를 확인합니다.
    /// </summary>
    public bool IsInvincible
    {
        get
        {
            if (_controller == null) return false;
            
            // 1. 일반 무적 타이머 체크
            if (_controller.IsInvincible) return true;
            
            // 2. Dash Magic 최종 강화 상태 체크
            if (_controller.IsDashFinalEnhancement) return true;
            
            return false;
        }
    }
    #endregion

    #region Events
    /// <summary>
    /// UI 갱신용 (Network Change Detector에서 호출됨)
    /// </summary>
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
    #endregion

    #region Health Management
    /// <summary>
    /// 플레이어에게 데미지를 입힙니다 (서버/State Authority에서만 실행).
    /// </summary>
    public void TakeDamage(float damage, PlayerRef attacker = default)
    {
        if (!IsServer || IsDead) return;
        
        // 무적 체크
        if (IsInvincible)
        {
            Debug.Log($"[PlayerState] {_controller.name} took no damage (Invincible)");
            return;
        }

        // 데미지 적용
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        
        // 무적 타이머 시작 (피격 후 잠시 무적)
        if (_initialData != null && _controller.Runner != null)
        {
            InvincibilityTimer = TickTimer.CreateFromSeconds(_controller.Runner, _initialData.InvincibilityDuration);
        }

        // 이벤트 (서버 로직용)
        OnDamageTaken?.Invoke(damage);

        // 사망 처리
        if (CurrentHealth <= 0)
        {
            HandleDeath(attacker);
        }
    }

    /// <summary>
    /// 플레이어를 치료합니다 (서버/State Authority에서만 실행).
    /// </summary>
    public void Heal(float healAmount)
    {
        if (!IsServer || IsDead) return;

        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + healAmount);
        OnHealed?.Invoke(healAmount);
    }

    /// <summary>
    /// 최대 체력을 변경합니다 (서버/State Authority에서만 실행).
    /// </summary>
    public void SetMaxHealth(float newMaxHealth)
    {
        if (!IsServer) return;

        MaxHealth = newMaxHealth;
        CurrentHealth = Mathf.Min(CurrentHealth, MaxHealth);
    }
    
    /// <summary>
    /// 현재 체력을 직접 설정합니다 (서버/State Authority에서만 실행).
    /// 보호막 등 특수 상황에서 사용됩니다.
    /// </summary>
    public void SetHealth(float newHealth)
    {
        if (!IsServer) return;
        
        CurrentHealth = Mathf.Clamp(newHealth, 0, MaxHealth);
    }
    #endregion

    #region Death & Respawn
    /// <summary>
    /// 플레이어를 사망 처리합니다 (서버/State Authority에서만 실행).
    /// </summary>
    private void HandleDeath(PlayerRef killer = default)
    {
        IsDead = true;
        CurrentHealth = 0;
        
        // [중요] 자폭 베리어 로직은 PlayerController나 Handler가 이벤트(OnDeath)를 구독해서 처리하는 것이 좋지만,
        // 기존 로직 유지를 위해 여기서 호출합니다. (단, null 체크 간소화)
        if (_controller.HasBarrier && _controller.BarrierTimer.IsRunning)
        {
            // 자폭!
            var barrierHandler = _controller.MagicController?.GetBarrierMagicHandler();
            barrierHandler?.HandleBarrierExplosion(_controller);
            
            // 베리어 해제
            _controller.HasBarrier = false;
            _controller.BarrierTimer = TickTimer.None;
        }
        
        // 이벤트 발생
        OnDeath?.Invoke(killer);

        // 리스폰 타이머 설정
        if (_initialData != null && _initialData.CanRespawn && _controller.Runner != null)
        {
            RespawnTimer = TickTimer.CreateFromSeconds(_controller.Runner, _initialData.RespawnDelay);
        }
    }
    
    /// <summary>
    /// 플레이어를 리스폰합니다 (서버/State Authority에서만 실행).
    /// </summary>
    /// <param name="spawnPosition">리스폰 위치 (PlayerController가 처리할 수 있음)</param>
    public void Respawn(Vector3? spawnPosition = null)
    {
        if (!IsServer) return;
        
        // 상태 초기화
        IsDead = false;
        CurrentHealth = _initialData != null ? _initialData.StartingHealth : MaxHealth;
        RespawnTimer = TickTimer.None;

        // 위치 재설정
        Vector3 pos = spawnPosition ?? _controller.transform.position;
        _controller.OnPlayerRespawned(pos);

        // 이벤트 발생
        OnRespawned?.Invoke();
    }
    #endregion
}