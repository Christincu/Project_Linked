using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

/// <summary>
/// 네트워크 동기화를 지원하는 플레이어 컨트롤러 (Photon Fusion)
/// 캐릭터의 상태, 마법, 이동 등을 총괄하며 MainGameManager에 의해 관리됩니다.
/// </summary>
public class PlayerController : NetworkBehaviour, IPlayerLeft
{
    // Animation
    [Networked] public NetworkString<_16> AnimationState { get; set; }
    [Networked] public float ScaleX { get; set; }

    // Player Info
    [Networked] public int PlayerSlot { get; set; }
    [Networked] public int CharacterIndex { get; set; }

    // Health
    [Networked] public float CurrentHealth { get; set; }
    [Networked] public float MaxHealth { get; set; }
    [Networked] public NetworkBool IsDead { get; set; }
    [Networked] public TickTimer InvincibilityTimer { get; set; }
    [Networked] public TickTimer RespawnTimer { get; set; }

    // Magic
    [Networked] public int ActiveMagicSlotNetworked { get; set; }
    [Networked] public int ActivatedMagicCode { get; set; }
    [Networked] public int AbsorbedMagicCode { get; set; }
    [Networked] public Vector3 MagicAnchorLocalPosition { get; set; }

    // Teleporter & Barrier
    [Networked] public TickTimer TeleportCooldownTimer { get; set; }
    [Networked] public NetworkBool DidTeleport { get; set; }

    [SerializeField] private GameObject _magicViewObj;
    [SerializeField] private GameObject _magicAnchor;
    [SerializeField] private GameObject _magicIdleFirstFloor;
    [SerializeField] private GameObject _magicIdleSecondFloor;
    [SerializeField] private GameObject _magicActiveFloor;
    
    private GameDataManager _gameDataManager;
    private ChangeDetector _changeDetector;

    // Sub-Components
    private PlayerRigidBodyMovement _movement;
    private PlayerAnimationController _animationController;
    private PlayerDetectionTrigger _detectionTrigger;
    
    // Internal State
    private bool _isTestMode;
    private InitialPlayerData _initialData;

    public GameDataManager GameDataManager => _gameDataManager;
    public PlayerRigidBodyMovement Movement => _movement;
    public PlayerAnimationController AnimationController => _animationController;
    public PlayerDetectionTrigger DetectionTrigger => _detectionTrigger;
    public ChangeDetector MagicChangeDetector => _changeDetector;

    public float HealthPercentage => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;
    public bool IsInvincible => !InvincibilityTimer.ExpiredOrNotRunning(Runner);
    public bool IsTestMode => _isTestMode;

    /// <summary>
    /// UI 갱신용 이벤트
    /// </summary>
    public System.Action<float, float> OnHealthChanged; // (current, max)
    public System.Action<PlayerRef> OnDeath; // (killer)
    public System.Action OnRespawned;
    public System.Action<float> OnDamageTaken; // (damage)
    public System.Action<float> OnHealed; // (healAmount)

    #region Fusion Callbacks

    public override void Spawned()
    {
        base.Spawned();

        Runner.SetIsSimulated(Object, true);
        _isTestMode = MainGameManager.Instance != null && MainGameManager.Instance.IsTestMode;
        _gameDataManager = GameDataManager.Instance;

        InitializeComponents();
        InitializeNetworkState();
        InitializeSubSystems();
    }

    /// <summary>
    /// PlayerController의 내부 하위 시스템들을 초기화합니다.
    /// UI 등록 로직은 MainGameManager가 담당하므로 제거되었습니다.
    /// </summary>
    private void InitializeSubSystems()
    {
        InitialPlayerData initialData = GameDataManager.Instance?.InitialPlayerData;

        if (initialData == null)
        {
            Debug.LogError("[PlayerController] InitialPlayerData is null! Check GameDataManager.");
            return;
        }

        if (Object.HasStateAuthority)
        {
            if (MaxHealth <= 0)
            {
                MaxHealth = initialData.MaxHealth;
                CurrentHealth = initialData.StartingHealth;
                IsDead = false;
            }
        }
        _initialData = initialData;

        _movement?.Initialize(this, initialData);
        InitializeDetectionTrigger();
        _animationController?.Initialize(this);
    }

    /// <summary>
    /// 적 감시 범위 트리거를 초기화합니다.
    /// </summary>
    private void InitializeDetectionTrigger()
    {
        if (_detectionTrigger == null)
        {
            GameObject detectionTriggerObj = new GameObject("DetectionTrigger");
            detectionTriggerObj.transform.SetParent(transform);
            detectionTriggerObj.transform.localPosition = Vector3.zero;
            detectionTriggerObj.transform.localRotation = Quaternion.identity;
            detectionTriggerObj.transform.localScale = Vector3.one;
            detectionTriggerObj.layer = 15;

            _detectionTrigger = detectionTriggerObj.AddComponent<PlayerDetectionTrigger>();
            _detectionTrigger.Initialize(this, _gameDataManager);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (_movement == null) return;

        InputData? inputData = null;
        if (GetInput<InputData>(out var data))
        {
            inputData = data;
        }

        // 1. 사망 상태가 아닐 때만 동작
        if (!IsDead)
        {
            HandleMovement(inputData);
        }
        else
        {
            // 사망 시 이동 정지
            if (_movement?.Rigidbody != null)
            {
                _movement.Rigidbody.velocity = Vector2.zero;
            }
        }

        // 2. 서버 권한 로직 (리스폰, 애니메이션, 상태 업데이트)
        if (Object.HasStateAuthority)
        {
            if (IsDead && RespawnTimer.Expired(Runner))
            {
                Respawn();
            }

            _animationController?.UpdateAnimation();

            if (DidTeleport) DidTeleport = false;
        }
    }

    public override void Render()
    {
        DetectNetworkChanges();
    }

    private void HandleMovement(InputData? inputData)
    {
        _movement.ProcessInput(inputData, _isTestMode, PlayerSlot);
        _movement.Move();
    }

    #endregion

    #region Public Methods - Network & Game Logic

    public void PlayerLeft(PlayerRef player)
    {
        if (player == Object.InputAuthority)
        {
            Runner.Despawn(Object);
        }
    }

    public void RequestTeleport(Vector3 targetPosition)
    {
        if (Object.HasStateAuthority)
        {
            RPC_TeleportPlayer(targetPosition);
        }
    }

    public void SetCharacterIndex(int characterIndex)
    {
        CharacterIndex = characterIndex;
    }

    public void OnPlayerRespawned(Vector3 spawnPosition)
    {
        if (Movement?.Rigidbody != null)
        {
            Movement.Rigidbody.position = spawnPosition;
        }
        else
        {
            transform.position = spawnPosition;
        }

        AnimationState = "idle";
        _animationController?.Initialize(this);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_UpdateMagicAnchorPosition(Vector3 localPosition)
    {
        MagicAnchorLocalPosition = localPosition;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_CastMagic(Vector3 targetPosition)
    {

    }

    public void DeactivateMagicInternal()
    {
        ActivatedMagicCode = -1;
        AbsorbedMagicCode = -1;
        ActiveMagicSlotNetworked = 0;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ShowLoadingPanel(float duration)
    {
        if (Object.HasInputAuthority)
        {
            LoadingPanel.ShowForSeconds(duration);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ShowLoadingPanelToAll()
    {
        LoadingPanel.Show();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_TeleportPlayer(Vector3 targetPosition)
    {
        ExecuteTeleport(targetPosition);
    }

    private void ExecuteTeleport(Vector3 targetPosition)
    {
        var networkRb = GetComponent<NetworkRigidbody2D>();
        if (networkRb?.Rigidbody != null)
        {
            networkRb.Teleport(targetPosition);
            networkRb.Rigidbody.velocity = Vector2.zero;
        }
        else
        {
            transform.position = targetPosition;
        }

        DidTeleport = true;
        _animationController?.Initialize(this);
    }

    /// <summary>
    /// 플레이어의 모든 행동 상태(마법, 대시, 배리어 등)를 완벽하게 초기화합니다.
    /// 마법 종료, 리스폰, 피격 등으로 인한 상태 초기화 시 사용합니다.
    /// [Server Only] 이 메서드는 State Authority에서만 호출해야 합니다.
    /// </summary>
    public void ResetPlayerActionState()
    {
        if (!Object.HasStateAuthority) return;

        ActivatedMagicCode = -1;
        AbsorbedMagicCode = -1;
        ActiveMagicSlotNetworked = 0;
    }

    private void InitializeComponents()
    {
        _movement = GetComponent<PlayerRigidBodyMovement>() ?? gameObject.AddComponent<PlayerRigidBodyMovement>();
        _animationController = GetComponent<PlayerAnimationController>() ?? gameObject.AddComponent<PlayerAnimationController>();
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
    }

    private void InitializeNetworkState()
    {
        if (!Object.HasStateAuthority) return;

        ScaleX = 1f;
        AnimationState = "idle";
        IsDead = false;

        ActivatedMagicCode = -1;
        AbsorbedMagicCode = -1;
        ActiveMagicSlotNetworked = 0;
    }

    public bool IsEnemyNearby(EnemyDetector enemy)
    {
        return _detectionTrigger != null && _detectionTrigger.IsEnemyNearby(enemy);
    }

    /// <summary>
    /// 플레이어에게 데미지를 입힙니다 (서버/State Authority에서만 실행).
    /// </summary>
    public void TakeDamage(float damage, PlayerRef attacker = default)
    {
        if (!Object.HasStateAuthority || IsDead) return;
        
        // 무적 체크
        if (IsInvincible)
        {
            Debug.Log($"[PlayerController] {name} took no damage (Invincible)");
            return;
        }

        // 데미지 적용
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        
        // 무적 타이머 시작 (피격 후 잠시 무적)
        if (_initialData != null && Runner != null)
        {
            InvincibilityTimer = TickTimer.CreateFromSeconds(Runner, _initialData.InvincibilityDuration);
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
        if (!Object.HasStateAuthority || IsDead) return;

        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + healAmount);
        OnHealed?.Invoke(healAmount);
    }

    /// <summary>
    /// 최대 체력을 변경합니다 (서버/State Authority에서만 실행).
    /// </summary>
    public void SetMaxHealth(float newMaxHealth)
    {
        if (!Object.HasStateAuthority) return;

        MaxHealth = newMaxHealth;
        CurrentHealth = Mathf.Min(CurrentHealth, MaxHealth);
    }
    
    /// <summary>
    /// 현재 체력을 직접 설정합니다 (서버/State Authority에서만 실행).
    /// 보호막 등 특수 상황에서 사용됩니다.
    /// </summary>
    public void SetHealth(float newHealth)
    {
        if (!Object.HasStateAuthority) return;
        
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
        OnDeath?.Invoke(killer);
        if (_initialData != null && _initialData.CanRespawn && Runner != null)
        {
            RespawnTimer = TickTimer.CreateFromSeconds(Runner, _initialData.RespawnDelay);
        }
    }
    
    /// <summary>
    /// 플레이어를 리스폰합니다 (서버/State Authority에서만 실행).
    /// </summary>
    /// <param name="spawnPosition">리스폰 위치 (선택적)</param>
    public void Respawn(Vector3? spawnPosition = null)
    {
        if (!Object.HasStateAuthority) return;
        IsDead = false;
        CurrentHealth = _initialData != null ? _initialData.StartingHealth : MaxHealth;
        RespawnTimer = TickTimer.None;
        Vector3 pos = spawnPosition ?? transform.position;
        OnPlayerRespawned(pos);
        OnRespawned?.Invoke();
    }

    #endregion

    private void DetectNetworkChanges()
    {
        foreach (var change in _changeDetector.DetectChanges(this))
        {
            switch (change)
            {
                case nameof(AnimationState):
                    _animationController?.PlayAnimation(AnimationState.ToString());
                    break;
                case nameof(ScaleX):
                    _animationController?.UpdateScale();
                    break;
                case nameof(CharacterIndex):
                    break;
                case nameof(CurrentHealth):
                case nameof(MaxHealth):
                    OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
                    break;
                default:
                    break;
            }
        }

    }
}