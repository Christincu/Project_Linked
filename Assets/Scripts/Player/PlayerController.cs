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
    
    // Barrier (보호막)
    [Networked] public TickTimer BarrierTimer { get; set; }
    [Networked] public bool HasBarrier { get; set; }
    #endregion

    #region Private Fields - Components
    [SerializeField] private GameObject _magicViewObj;
    [SerializeField] private GameObject _magicAnchor;
    [SerializeField] private GameObject _magicIdleFirstFloor;
    [SerializeField] private GameObject _magicIdleSecondFloor;
    [SerializeField] private GameObject _magicActiveFloor;
    
    [Header("Magic Prefabs")]
    [SerializeField] private GameObject _magicProjectilePrefab;
    [SerializeField] private NetworkPrefabRef _barrierMagicObjectPrefab; // 보호막 마법 오브젝트 프리팹

    private GameDataManager _gameDataManager;
    private ChangeDetector _changeDetector;

    // Player Components
    private PlayerBehavior _behavior;
    private PlayerMagicController _magicController;
    private PlayerState _state;
    private PlayerRigidBodyMovement _movement;
    private PlayerAnimationController _animationController;
    private PlayerBarrierVisual _barrierVisual;
    private PlayerViewManager _viewManager;
    private PlayerDetectionManager _detectionManager;
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
    public PlayerBarrierVisual BarrierVisual => _barrierVisual;
    public PlayerViewManager ViewManager => _viewManager;
    public PlayerDetectionManager DetectionManager => _detectionManager;
    public float MoveSpeed => _movement != null ? _movement.GetMoveSpeed() : 0f;

    // Health Properties
    public float HealthPercentage => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;
    public bool IsInvincible => !InvincibilityTimer.ExpiredOrNotRunning(Runner);

    // Test Mode Property
    public bool IsTestMode => _isTestMode;
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
        _barrierVisual?.Initialize(this);

        if (_magicController != null)
        {
            _magicController.Initialize(this, GameDataManager, _magicProjectilePrefab, _barrierMagicObjectPrefab);
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
            _movement.ProcessInput(inputData, _isTestMode, PlayerSlot);
            if (inputData.HasValue)
            {
                _magicController?.ProcessInput(inputData.Value, this, _isTestMode);
            }
            _movement.Move();
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

        // 보호막 타이머 체크
        if (Object.HasStateAuthority && HasBarrier)
        {
            // 타이머가 실행 중이고 만료되었는지 확인
            if (BarrierTimer.IsRunning && BarrierTimer.Expired(Runner))
            {
                // 보호막 만료: 모든 플레이어의 HP를 3으로 회복
                RestoreAllPlayersHealthOnBarrierExpire();
                
                HasBarrier = false;
                BarrierTimer = TickTimer.None;
                Debug.Log($"[BarrierMagic] {name} barrier expired - all players HP restored to 3");
            }
            else if (!BarrierTimer.IsRunning)
            {
                // 타이머가 실행되지 않으면 보호막 제거 (초기화 문제)
                HasBarrier = false;
                Debug.LogWarning($"[BarrierMagic] {name} barrier timer not running, removing barrier");
            }
        }
    }

    public override void Render()
    {
        DetectNetworkChanges();
        _viewManager?.SyncViewObjPosition();
        _barrierVisual?.CheckBarrierState();
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
                RPC_DeactivateMagic();
            }
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
        _barrierVisual = GetComponent<PlayerBarrierVisual>() ?? gameObject.AddComponent<PlayerBarrierVisual>();
        _viewManager = GetComponent<PlayerViewManager>() ?? gameObject.AddComponent<PlayerViewManager>();
        _detectionManager = GetComponent<PlayerDetectionManager>() ?? gameObject.AddComponent<PlayerDetectionManager>();

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
            
            // 보호막 상태 제거
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
                    _barrierVisual?.UpdateBarrierVisual();
                    break;
            }
        }

        // 마법 상태가 변경되었을 때만 한 번 호출 (중복 방지)
        if (magicStateChanged)
        {
            MagicController.UpdateMagicUIState(MagicActive);
        }
    }
    #endregion
}

