using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

/// <summary>
/// 네트워크 동기화를 지원하는 플레이어 컨트롤러 (Photon Fusion)
/// NetworkRigidbody를 사용하여 위치 및 속도를 동기화합니다.
/// </summary>
public class PlayerController : NetworkBehaviour, IPlayerLeft
{

    #region Networked Properties
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
    [Networked] public Vector3 MagicAnchorLocalPosition { get; set; } // 앵커의 로컬 위치 동기화
    [Networked] public bool MagicActive { get; set; }
    [Networked] public int MagicActivationTick { get; set; }

    // Teleporter
    [Networked] public TickTimer TeleportCooldownTimer { get; set; }
    [Networked] public NetworkBool DidTeleport { get; set; }
    
    // Barrier (자폭 보호막)
    [Networked] public TickTimer BarrierTimer { get; set; }
    [Networked] public bool HasBarrier { get; set; }
    private int _barrierMoveSpeedEffectId = -1; // 이동속도 효과 ID
    
    // Dash Skill (화염 돌진 스킬)
    [Networked] public TickTimer DashSkillTimer { get; set; }
    [Networked] public bool HasDashSkill { get; set; }
    [Networked] public int DashEnhancementCount { get; set; } // 강화 횟수 (0~3)
    [Networked] public bool IsDashFinalEnhancement { get; set; } // 최종 강화 상태
    [Networked] public TickTimer DashStunTimer { get; set; } // 정지/행동불능 타이머
    [Networked] public NetworkBool DashIsMoving { get; set; } // 이동 상태 (false = 정지, true = 이동)
    [Networked] public Vector2 DashVelocity { get; set; } // 돌진 속도
    [Networked] public Vector2 DashLastInputDirection { get; set; } // 마지막 입력 방향
    [Networked] public Vector2 DashPendingRecoilDirection { get; set; } // 튕겨나갈 방향 (플레이어 충돌 후)
    [Networked] public NetworkBool DashIsWaitingToRecoil { get; set; } // 반동 대기 중인지 여부
    public DashMagicObject DashMagicObject { get; set; } // 돌진 마법 오브젝트 참조
    
    // Threat Score (위협점수)
    [Networked] public float ThreatScore { get; set; }
    #endregion

    #region Private Fields - Components
    [SerializeField] private GameObject _magicViewObj;
    [SerializeField] private GameObject _magicAnchor;
    [SerializeField] private GameObject _magicIdleFirstFloor;
    [SerializeField] private GameObject _magicIdleSecondFloor;
    [SerializeField] private GameObject _magicActiveFloor;
    
    private GameDataManager _gameDataManager;
    private ChangeDetector _changeDetector;

    // Player Components
    private PlayerBehavior _behavior;
    private PlayerMagicController _magicController;
    private PlayerState _state;
    private PlayerRigidBodyMovement _movement;
    private PlayerAnimationController _animationController;
    private PlayerViewManager _viewManager;
    private PlayerDetectionManager _detectionManager;
    private PlayerEffectManager _effectManager;
    #endregion

    #region Private Fields - State
    private bool _isTestMode;
    #endregion

    #region Properties
    public GameDataManager GameDataManager => _gameDataManager;
    public GameObject ViewObj => _viewManager != null ? _viewManager.ViewObj : null;
    public PlayerBehavior Behavior => _behavior;
    public PlayerMagicController MagicController => _magicController;
    public PlayerState State => _state;
    public PlayerRigidBodyMovement Movement => _movement;
    public PlayerAnimationController AnimationController => _animationController;
    public PlayerViewManager ViewManager => _viewManager;
    public PlayerDetectionManager DetectionManager => _detectionManager;
    public PlayerEffectManager EffectManager => _effectManager;
    public float MoveSpeed => _movement != null ? _movement.GetMoveSpeed() : 0f;
    public ChangeDetector MagicChangeDetector => _changeDetector;

    // Health Properties
    public float HealthPercentage => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;
    public bool IsInvincible => !InvincibilityTimer.ExpiredOrNotRunning(Runner);

    // Test Mode Property
    public bool IsTestMode => _isTestMode;
    
    // Threat Score Property
    public float CurrentThreatScore => ThreatScore;
    #endregion

    #region Fusion Callbacks
    /// <summary>
    /// 네트워크 오브젝트 생성 시 호출됩니다.
    /// </summary>
    public override void Spawned()
    {
        // MainGameManager의 테스트 모드 확인
        _isTestMode = MainGameManager.Instance != null && MainGameManager.Instance.IsTestMode;
        _gameDataManager = GameDataManager.Instance;

        InitializeComponents();
        InitializeNetworkState();

        // 초기화 및 데이터 동기화 대기
        StartCoroutine(InitializeAllComponents());
    }

    /// <summary>
    /// 모든 컴포넌트를 초기화하고 데이터 동기화를 처리합니다.
    /// </summary>
    private IEnumerator InitializeAllComponents()
    {
        yield return null;

        InitialPlayerData initialData = GameDataManager.Instance?.InitialPlayerData;

        if (initialData == null)
        {
            Debug.LogError("[PlayerController] GameDataManager or InitialPlayerData is null!");
            yield break;
        }

        // 1. 네트워크 변수 초기화 (서버만)
        if (Object.HasStateAuthority)
        {
            MaxHealth = initialData.MaxHealth;
            CurrentHealth = initialData.StartingHealth;
            IsDead = false;
        }

        // 2. 종속 컴포넌트 초기화
        _state?.Initialize(this, initialData);
        _movement?.Initialize(this, initialData);
        _behavior?.Initialize(this);
        _viewManager?.Initialize(this, _gameDataManager);
        _detectionManager?.Initialize(this, _gameDataManager);
        _animationController?.Initialize(this);
        _effectManager?.Initialize(this);

        if (_magicController != null)
        {
            _magicController.Initialize(this, GameDataManager);
            _magicController.SetMagicUIReferences(_magicViewObj, _magicAnchor, _magicIdleFirstFloor, _magicIdleSecondFloor, _magicActiveFloor);
        }

        // ViewObj 생성
        _viewManager?.TryCreateView();
        
        // Canvas 등록
        if (Object.HasStateAuthority)
        {
            RegisterToCanvas();
        }
        else
        {
            StartCoroutine(WaitForNetworkSyncAndRegister());
        }
    }

    private IEnumerator WaitForNetworkSyncAndRegister()
    {
        while (MaxHealth <= 0)
        {
            yield return null;
        }

        RegisterToCanvas();
    }

    private void RegisterToCanvas()
    {
        if (GameManager.Instance?.Canvas is MainCanvas canvas)
        {
            canvas.RegisterPlayer(this);
        }
        else
        {
            Debug.LogWarning($"[PlayerController] GameManager, Canvas, or MainCanvas type not found");
        }
    }

    /// <summary>
    /// Fusion 네트워크 입력 처리 (매 틱마다 호출)
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (_movement == null || _state == null) return;

        InputData? inputData = null;
        if (GetInput<InputData>(out var data))
        {
            inputData = data;
        }

        if (!_state.IsDead)
        {
            // 1. 이동 로직 처리
            HandleMovement(inputData);
            
            // 2. 마법 입력 처리 (MagicController로 위임)
            // 돌진 중에는 마법 조작 불가 (이미 DashMagicObject에서 처리)
            if (inputData.HasValue && !HasDashSkill)
            {
                _magicController?.ProcessInput(inputData.Value, this, _isTestMode);
            }
        }
        else
        {
            // 사망 시 즉시 이동 정지 (관성 제거)
            if (_movement != null && _movement.Rigidbody != null)
            {
                _movement.Rigidbody.velocity = Vector2.zero;
            }
        }

        if (Object.HasStateAuthority && _state.IsDead)
        {
            if (_state.RespawnTimer.Expired(Runner))
            {
                _state.Respawn();
            }
        }

        if (Object.HasStateAuthority)
        {
            _animationController?.UpdateAnimation();
        }

        if (Object.HasStateAuthority && DidTeleport)
        {
            DidTeleport = false;
        }

        // 자폭 베리어 타이머 체크 및 이동속도 효과 관리 (State Authority)
        if (Object.HasStateAuthority)
        {
            UpdateBarrierState();
        }
        
        // 위협점수 업데이트 (매 프레임)
        if (Object.HasStateAuthority)
        {
            UpdateThreatScore();
        }
        
        // 효과 업데이트 (만료된 효과 제거)
        if (Object.HasStateAuthority)
        {
            _effectManager?.UpdateEffects();
        }
    }

    public override void Render()
    {
        DetectNetworkChanges();
        _viewManager?.SyncViewObjPosition();
        
        // 마법 컨트롤러의 시각적 업데이트 (렌더링 틱에서 처리)
        _magicController?.OnRender();
    }
    /// <summary>
    /// 이동 로직을 처리합니다.
    /// </summary>
    private void HandleMovement(InputData? inputData)
    {
        // 대쉬 스킬 사용 중이면 대쉬 오브젝트가 이동 제어
        if (HasDashSkill && DashMagicObject != null)
        {
            DashMagicObject.FixedUpdateNetwork();
        }
        else
        {
            _movement.ProcessInput(inputData, _isTestMode, PlayerSlot);
            _movement.Move();
        }
    }
    #endregion

    #region Public Methods - Network
    /// <summary>
    /// 플레이어가 나갔을 때 호출됩니다.
    /// </summary>
    public void PlayerLeft(PlayerRef player)
    {
        if (player == Object.InputAuthority)
        {
            Runner.Despawn(Object);
        }
    }

    /// <summary>
    /// MapTeleporter가 순간이동을 요청합니다. (State Authority에서 호출)
    /// </summary>
    public void RequestTeleport(Vector3 targetPosition)
    {
        // PlayerController는 NetworkBehaviour이므로, RPC를 호출하여 서버에 요청합니다.
        if (Object.HasStateAuthority)
        {
            RPC_TeleportPlayer(targetPosition);
        }
    }
    #endregion

    #region Public Methods - Character & State Callbacks
    /// <summary>
    /// 캐릭터 인덱스를 설정하고 뷰 오브젝트를 생성합니다.
    /// </summary>
    public void SetCharacterIndex(int characterIndex)
    {
        CharacterIndex = characterIndex;
        _viewManager?.TryCreateView();
    }

    /// <summary>
    /// PlayerState에서 리스폰 시 호출됩니다. 위치를 업데이트하고 움직임을 리셋합니다.
    /// </summary>
    public void OnPlayerRespawned(Vector3 spawnPosition)
    {
        // Rigidbody 위치를 설정하여 동기화를 보장합니다.
        if (Movement?.Rigidbody != null)
        {
            Movement.Rigidbody.position = spawnPosition;
        }
        else
        {
            transform.position = spawnPosition;
        }

        AnimationState = "idle";

        _movement?.ResetVelocity();
        _animationController?.Initialize(this); // 위치 초기화를 위해 재초기화

    }

    #endregion

    #region RPC Methods
    /// <summary>
    /// 마법 앵커 로컬 위치를 업데이트합니다. (Input Authority → State Authority)
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_UpdateMagicAnchorPosition(Vector3 localPosition)
    {
        MagicAnchorLocalPosition = localPosition;
    }

    /// <summary>
    /// 마법 UI를 활성화합니다. (Input Authority → State Authority)
    /// 서버가 MagicActive를 변경하면 자동으로 모든 클라이언트에 동기화됨
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_ActivateMagic(int magicSlot, int activatedMagicCode)
    {
        MagicActive = true;
        ActivatedMagicCode = activatedMagicCode;
        AbsorbedMagicCode = -1;
        MagicActivationTick = Runner.Tick;
        ActiveMagicSlotNetworked = magicSlot;
        // [수정] 마법 활성화 시 앵커 위치를 초기화하여 이전 위치가 남아있지 않도록 함
        MagicAnchorLocalPosition = Vector3.zero;
    }

    /// <summary>
    /// 마법 UI를 비활성화합니다. (Input Authority → State Authority)
    /// 서버가 MagicActive를 변경하면 자동으로 모든 클라이언트에 동기화됨
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_DeactivateMagic()
    {
        MagicActive = false;
        MagicActivationTick = 0;
        ActivatedMagicCode = -1;
        AbsorbedMagicCode = -1;
        ActiveMagicSlotNetworked = 0;
    }

    /// <summary>
    /// 마법을 시전합니다. (Input Authority → State Authority)
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_CastMagic(Vector3 targetPosition)
    {
        if (_magicController != null)
        {
            // CastMagic에서 마법 코드를 결정하므로 여기서는 바로 호출
            _magicController.CastMagic(targetPosition);
            
            // CastMagic에서 결정된 마법 코드를 확인하여 보호막 마법이 아닌 경우에만 비활성화
            // 보호막 마법은 선택 후에 비활성화됨
            int magicCodeToCast = GetCurrentMagicCodeToCast();
            if (magicCodeToCast != 10)
            {
                // RPC를 직접 호출하는 대신 네트워크 변수를 직접 설정
                // (State Authority에서 실행 중이므로 직접 설정 가능)
                DeactivateMagicInternal();
            }
        }
    }
    
    /// <summary>
    /// 마법을 비활성화합니다. (내부 메서드, State Authority에서 직접 호출)
    /// 다른 NetworkBehaviour에서 호출할 때 사용합니다.
    /// </summary>
    public void DeactivateMagicInternal()
    {
        MagicActive = false;
        MagicActivationTick = 0;
        ActivatedMagicCode = -1;
        AbsorbedMagicCode = -1;
        ActiveMagicSlotNetworked = 0;
    }
    
    /// <summary>
    /// 보호막을 플레이어에게 적용합니다. (Input Authority → State Authority)
    /// BarrierMagicHandler에서 호출됩니다.
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_ApplyBarrier(NetworkId targetPlayerId)
    {
        if (_magicController == null) return;
        
        // 타겟 플레이어 찾기
        PlayerController targetPlayer = null;
        if (Runner != null)
        {
            var targetObj = Runner.FindObject(targetPlayerId);
            if (targetObj != null)
            {
                targetPlayer = targetObj.GetComponent<PlayerController>();
            }
        }
        
        if (targetPlayer == null || targetPlayer.IsDead) return;
        
        // BarrierMagicHandler를 통해 보호막 적용
        var barrierHandler = _magicController.GetBarrierMagicHandler();
        if (barrierHandler != null)
        {
            barrierHandler.ApplyBarrierToPlayer(targetPlayer);
        }
    }
    
    /// <summary>
    /// 현재 시전할 마법 코드를 가져옵니다.
    /// </summary>
    private int GetCurrentMagicCodeToCast()
    {
        if (AbsorbedMagicCode != -1 && ActivatedMagicCode != -1)
        {
            int combinedMagicCode = _gameDataManager.MagicService.GetCombinedMagic(
                ActivatedMagicCode, 
                AbsorbedMagicCode
            );
            if (combinedMagicCode != -1)
            {
                return combinedMagicCode;
            }
        }
        
        if (ActivatedMagicCode != -1)
        {
            return ActivatedMagicCode;
        }
        
        return -1;
    }

    /// <summary>
    /// 서버가 모든 클라이언트에게 폭발 이펙트를 재생하라고 명령합니다.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_TriggerExplosionVfx(Vector3 position, float radius)
    {
        // BarrierMagicCombinationData에서 폭발 이펙트 프리팹 가져오기
        BarrierMagicCombinationData barrierData = GetBarrierData();
        if (barrierData == null || barrierData.explosionVfxPrefab == null)
        {
            Debug.LogWarning($"[RPC_TriggerExplosionVfx] Explosion VFX prefab is not assigned in BarrierMagicCombinationData! Position: {position}, Radius: {radius}");
            return;
        }
        
        // 1. 이펙트 프리팹 생성 (로컬 GameObject)
        GameObject vfx = Instantiate(barrierData.explosionVfxPrefab, position, Quaternion.identity);
        
        // 2. 크기 조정 (필요 시)
        vfx.transform.localScale = Vector3.one * radius;
        
        // 3. 파티클 재생 후 파괴 (파티클 설정에 Auto Destruct가 있다면 생략 가능)
        Destroy(vfx, 2.0f);
    }

    /// <summary>
    /// 텔레포트하는 플레이어에게만 로딩 패널을 표시합니다.
    /// (MapTeleporter에서 호출)
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ShowLoadingPanel(float duration)
    {
        // 해당 플레이어만 로딩 패널 표시
        if (Object.HasInputAuthority)
        {
            LoadingPanel.ShowForSeconds(duration);
        }
    }

    /// <summary>
    /// 모든 플레이어에게 로딩 패널을 표시합니다.
    /// (ScenDespawner에서 호출)
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ShowLoadingPanelToAll()
    {
        LoadingPanel.Show();
    }

    /// <summary>
    /// 서버에서 호출되어 플레이어의 위치를 강제로 설정합니다. (MapTeleporter 사용)
    /// 로딩 화면은 MapTeleporter에서 관리합니다.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_TeleportPlayer(Vector3 targetPosition)
    {
        ExecuteTeleport(targetPosition);
    }

    private void ExecuteTeleport(Vector3 targetPosition)
    {
        var networkRb = GetComponent<NetworkRigidbody2D>();
        if (networkRb != null && networkRb.Rigidbody != null)
        {
            networkRb.Teleport(targetPosition);
            networkRb.Rigidbody.velocity = Vector2.zero;
        }
        else
        {
            transform.position = targetPosition;
        }

        Movement?.ResetVelocity();
        DidTeleport = true;
        _animationController?.Initialize(this); // 위치 초기화를 위해 재초기화

    }
    #endregion

    #region Private Methods - Initialization
    /// <summary>
    /// 컴포넌트를 초기화합니다 (없으면 추가).
    /// </summary>
    private void InitializeComponents()
    {
        _state = GetComponent<PlayerState>() ?? gameObject.AddComponent<PlayerState>();
        _behavior = GetComponent<PlayerBehavior>() ?? gameObject.AddComponent<PlayerBehavior>();
        _magicController = GetComponent<PlayerMagicController>() ?? gameObject.AddComponent<PlayerMagicController>();
        _movement = GetComponent<PlayerRigidBodyMovement>() ?? gameObject.AddComponent<PlayerRigidBodyMovement>();
        _animationController = GetComponent<PlayerAnimationController>() ?? gameObject.AddComponent<PlayerAnimationController>();
        _viewManager = GetComponent<PlayerViewManager>() ?? gameObject.AddComponent<PlayerViewManager>();
        _detectionManager = GetComponent<PlayerDetectionManager>() ?? gameObject.AddComponent<PlayerDetectionManager>();
        _effectManager = GetComponent<PlayerEffectManager>() ?? gameObject.AddComponent<PlayerEffectManager>();


        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
    }
    
    /// <summary>
    /// 네트워크 상태 초기값을 설정합니다. (State Authority 전용)
    /// </summary>
    private void InitializeNetworkState()
    {
        if (Object.HasStateAuthority)
        {
            ScaleX = 1f;
            AnimationState = "idle";
            IsDead = false;
            TeleportCooldownTimer = TickTimer.None;
            DidTeleport = false;
            BarrierTimer = TickTimer.None;
            HasBarrier = false;
            DashSkillTimer = TickTimer.None;
            HasDashSkill = false;
            DashEnhancementCount = 0;
            IsDashFinalEnhancement = false;
            DashStunTimer = TickTimer.None;
            DashIsMoving = false;
            DashVelocity = Vector2.zero;
            DashLastInputDirection = Vector2.zero;
            DashPendingRecoilDirection = Vector2.zero;
            DashIsWaitingToRecoil = false;
            ThreatScore = 0f;
        }
    }
    
    /// <summary>
    /// 특정 적이 근처에 있는지 확인합니다.
    /// </summary>
    public bool IsEnemyNearby(EnemyDetector enemy)
    {
        return _detectionManager != null && _detectionManager.IsEnemyNearby(enemy);
    }
    
    /// <summary>
    /// 보호막 만료 시 모든 플레이어의 HP를 3으로 회복합니다.
    /// </summary>
    private void RestoreAllPlayersHealthOnBarrierExpire()
    {
        if (Runner == null) return;
        
        // 모든 플레이어 가져오기
        List<PlayerController> allPlayers = new List<PlayerController>();
        
        if (MainGameManager.Instance != null)
        {
            allPlayers = MainGameManager.Instance.GetAllPlayers();
        }
        
        if (allPlayers == null || allPlayers.Count == 0)
        {
            allPlayers = new List<PlayerController>(FindObjectsOfType<PlayerController>());
        }
        
        // 모든 플레이어의 HP를 3으로 회복
        foreach (var player in allPlayers)
        {
            if (player == null || player.IsDead) continue;
            
            // PlayerState의 SetHealth를 사용하여 이벤트 발생
            if (player.State != null)
            {
                player.State.SetHealth(3f);
            }
            else
            {
                player.CurrentHealth = 3f;
            }
            
            // 보호막 상태 제거 (FixedUpdateNetwork에서 이동속도 효과도 자동 제거됨)
            player.HasBarrier = false;
            player.BarrierTimer = TickTimer.None;
            
            Debug.Log($"[BarrierMagic] {player.name} HP restored to 3 (barrier expired)");
        }
    }
    #endregion

    #region Private Methods - Network Synchronization
    /// <summary>
    /// 네트워크 상태 변경을 감지하고 처리합니다. (렌더링 틱)
    /// </summary>
    private void DetectNetworkChanges()
    {
        bool magicStateChanged = false;

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
                    _viewManager?.TryCreateView();
                    break;

                case nameof(CurrentHealth):
                case nameof(MaxHealth):
                    // 체력 변경 시 UI 업데이트를 위한 이벤트 발생 (모든 클라이언트에서)
                    if (_state != null)
                    {
                        _state.OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
                    }
                    break;

                case nameof(MagicActive):
                    MagicController.UpdateMagicUIState(MagicActive);
                    magicStateChanged = true;
                    break;

                case nameof(ActiveMagicSlotNetworked):
                    MagicController.UpdateMagicUIState(MagicActive);
                    magicStateChanged = true;
                    break;

                case nameof(MagicAnchorLocalPosition):
                    _magicController.UpdateMagicUIState(MagicActive);
                    _magicController.UpdateAnchorPosition(MagicAnchorLocalPosition);
                    break;

                case nameof(AbsorbedMagicCode):
                    _magicController.UpdateMagicUIState(MagicActive);
                    break;

                case nameof(HasBarrier):
                    UpdateThreatScore(); // 위협점수 업데이트
                    break;
            }
        }

        // 마법 상태가 변경되었을 때만 한 번 호출 (중복 방지)
        if (magicStateChanged)
        {
            MagicController.UpdateMagicUIState(MagicActive);
        }
    }
    
    /// <summary>
    /// 베리어 상태를 업데이트합니다. (PlayerController를 깔끔하게 유지하기 위해 메서드로 분리)
    /// </summary>
    private void UpdateBarrierState()
    {
        if (!HasBarrier || IsDead) return;
        
        if (BarrierTimer.IsRunning)
        {
            float remainingTime = BarrierTimer.RemainingTime(Runner) ?? 0f;
            
            // 베리어 조합 데이터 가져오기
            BarrierMagicCombinationData barrierData = GetBarrierData();
            if (barrierData != null)
            {
                // 이동속도 효과 적용 구간 계산
                float moveSpeedEffectEndTime = barrierData.barrierDuration - barrierData.moveSpeedEffectDuration;
                
                // 이동속도 효과 적용 구간 (예: 10초~3초)
                if (remainingTime > moveSpeedEffectEndTime)
                {
                    // 이동속도 효과가 없으면 추가
                    if (_barrierMoveSpeedEffectId == -1 && _effectManager != null)
                    {
                        float effectDuration = remainingTime - moveSpeedEffectEndTime;
                        _barrierMoveSpeedEffectId = _effectManager.AddEffect(EffectType.MoveSpeed, barrierData.moveSpeedMultiplier, effectDuration);
                        Debug.Log($"[BarrierMagic] {name} barrier move speed effect applied ({barrierData.moveSpeedMultiplier * 100f}%)");
                    }
                }
                // 이동속도 정상화 구간
                else
                {
                    // 이동속도 효과 제거
                    if (_barrierMoveSpeedEffectId != -1 && _effectManager != null)
                    {
                        _effectManager.RemoveEffect(_barrierMoveSpeedEffectId);
                        _barrierMoveSpeedEffectId = -1;
                        Debug.Log($"[BarrierMagic] {name} barrier move speed effect removed (normalized)");
                    }
                }
            }
            else
            {
                // 데이터가 없으면 기본값 사용 (하위 호환성)
                if (remainingTime > 3f)
                {
                    if (_barrierMoveSpeedEffectId == -1 && _effectManager != null)
                    {
                        _barrierMoveSpeedEffectId = _effectManager.AddEffect(EffectType.MoveSpeed, 1.5f, remainingTime - 3f);
                    }
                }
                else
                {
                    if (_barrierMoveSpeedEffectId != -1 && _effectManager != null)
                    {
                        _effectManager.RemoveEffect(_barrierMoveSpeedEffectId);
                        _barrierMoveSpeedEffectId = -1;
                    }
                }
            }
            
            // 타이머 만료 확인
            if (BarrierTimer.Expired(Runner))
            {
                // 보호막 만료 처리
                HasBarrier = false;
                BarrierTimer = TickTimer.None;
                
                // 이동속도 효과 제거
                if (_barrierMoveSpeedEffectId != -1 && _effectManager != null)
                {
                    _effectManager.RemoveEffect(_barrierMoveSpeedEffectId);
                    _barrierMoveSpeedEffectId = -1;
                }
                
                UpdateThreatScore();
                Debug.Log($"[BarrierMagic] {name} barrier expired");
            }
        }
        else if (!BarrierTimer.IsRunning)
        {
            // 타이머가 실행되지 않으면 보호막 제거
            HasBarrier = false;
            
            // 이동속도 효과 제거
            if (_barrierMoveSpeedEffectId != -1 && _effectManager != null)
            {
                _effectManager.RemoveEffect(_barrierMoveSpeedEffectId);
                _barrierMoveSpeedEffectId = -1;
            }
            
            UpdateThreatScore();
            Debug.LogWarning($"[BarrierMagic] {name} barrier timer not running, removing barrier");
        }
        
        // 보호막이 없는데 효과가 남아있으면 제거
        if (!HasBarrier && _barrierMoveSpeedEffectId != -1)
        {
            if (_effectManager != null)
            {
                _effectManager.RemoveEffect(_barrierMoveSpeedEffectId);
                _barrierMoveSpeedEffectId = -1;
            }
        }
    }
    
    /// <summary>
    /// 위협점수를 업데이트합니다.
    /// 자폭 베리어에 따라 위협점수가 결정됩니다.
    /// </summary>
    private void UpdateThreatScore()
    {
        if (!Object.HasStateAuthority) return;
        
        float threatScore = 0f;
        
        // 1. 자폭 베리어 보너스
        if (HasBarrier)
        {
            // 베리어 조합 데이터에서 위협점수 가져오기
            BarrierMagicCombinationData barrierData = GetBarrierData();
            if (barrierData != null)
            {
                threatScore = barrierData.threatScore;
            }
            else
            {
                // 데이터가 없으면 기본값 사용 (하위 호환성)
                threatScore = 200f;
            }
        }
        
        // 2. 사망 상태는 위협점수 0
        if (IsDead)
        {
            threatScore = 0f;
        }
        
        ThreatScore = threatScore;
    }
    
    /// <summary>
    /// 베리어 조합 데이터를 가져옵니다.
    /// </summary>
    private BarrierMagicCombinationData GetBarrierData()
    {
        if (_gameDataManager == null || _gameDataManager.MagicService == null) return null;
        
        // 베리어 마법 코드는 10 (Air + Soil 조합)
        // MagicService에서 조합 데이터 찾기
        MagicCombinationData combinationData = _gameDataManager.MagicService.GetCombinationDataByResult(10);
        
        // BarrierMagicCombinationData로 캐스팅
        if (combinationData is BarrierMagicCombinationData barrierData)
        {
            return barrierData;
        }
        
        return null;
    }
    #endregion
}

