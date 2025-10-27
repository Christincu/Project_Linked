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
    #region Constants
    private const float MIN_MOVEMENT_SPEED = 0.1f; // Wall collision detection threshold
    #endregion

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
    [Networked] public Vector3 MagicAnchorLocalPosition { get; set; } // 앵커의 로컬 위치 동기화
    [Networked] public bool MagicActive { get; set; }
    [Networked] public TickTimer MagicCooldownTimer { get; set; }
    [Networked] public int MagicActivationTick { get; set; }

    // Teleporter
    [Networked] public TickTimer TeleportCooldownTimer { get; set; }
    [Networked] public NetworkBool DidTeleport { get; set; }
    #endregion

    #region Private Fields - Components
    [SerializeField] private GameObject _magicViewObj;
    [SerializeField] private GameObject _magicAnchor;
    [SerializeField] private GameObject _magicIdleFirstFloor;
    [SerializeField] private GameObject _magicIdleSecondFloor;
    [SerializeField] private GameObject _magicActiveFloor;

    private GameObject _viewObj;
    private Animator _animator;
    private ChangeDetector _changeDetector;

    // Player Components
    private PlayerBehavior _behavior;
    private PlayerMagicController _magicController;
    private PlayerState _state;
    private PlayerRigidBodyMovement _movement;
    #endregion

    #region Private Fields - State
    private Vector2 _previousPosition;
    private string _lastAnimationState = "";
    private bool _isTestMode;
    #endregion

    #region Properties
    public GameObject ViewObj => _viewObj;
    public PlayerBehavior Behavior => _behavior;
    public PlayerMagicController MagicController => _magicController;
    public PlayerState State => _state;
    public PlayerRigidBodyMovement Movement => _movement;
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
        _previousPosition = transform.position;

        InitializeComponents();
        InitializeNetworkState();

        TryCreateView();

        // 초기화 및 데이터 동기화 대기
        StartCoroutine(InitializeAllComponents());

        Debug.Log($"[PlayerController] Spawned - InputAuth: {Object.HasInputAuthority}, StateAuth: {Object.HasStateAuthority}, TestMode: {_isTestMode}");
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

        if (_magicController != null)
        {
            _magicController.Initialize(this);
            _magicController.SetMagicUIReferences(_magicViewObj, _magicAnchor, _magicIdleFirstFloor, _magicIdleSecondFloor, _magicActiveFloor);
        }

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
            _magicController?.ProcessInput(inputData, this, _isTestMode);
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
            UpdateAnimation();
        }

        if (Object.HasStateAuthority && DidTeleport)
        {
            DidTeleport = false;
        }

        _previousPosition = transform.position;
    }

    public override void Render()
    {
        DetectNetworkChanges();

        // ViewObj를 root 위치와 동기화
        if (_viewObj != null)
        {
            _viewObj.transform.localPosition = Vector3.zero;
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
        TryCreateView();
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

        _previousPosition = spawnPosition;
        AnimationState = "idle";

        _movement?.ResetVelocity();

        Debug.Log($"[PlayerController] Player respawned at {spawnPosition}");
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
    public void RPC_ActivateMagic(int magicSlot)
    {
        // 서버에서 MagicActive 변경 (자동으로 브로드캐스트됨)
        if (!MagicActive)
        {
            MagicActive = true;
            MagicActivationTick = Runner.Tick;

            ActiveMagicSlotNetworked = magicSlot;
            Debug.Log($"[PlayerController RPC] Magic activated - Slot: {magicSlot}, Tick: {MagicActivationTick}");
        }
    }

    /// <summary>
    /// 마법 UI를 비활성화합니다. (Input Authority → State Authority)
    /// 서버가 MagicActive를 변경하면 자동으로 모든 클라이언트에 동기화됨
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_DeactivateMagic()
    {
        // 서버에서 MagicActive 변경 (자동으로 브로드캐스트됨)
        if (MagicActive)
        {
            MagicActive = false;
            MagicActivationTick = 0;

            ActiveMagicSlotNetworked = 0;
            Debug.Log($"[PlayerController RPC] Magic deactivated");
        }
    }

    /// <summary>
    /// 특정 플레이어의 마법을 흡수합니다. (Input Authority → State Authority → Target Player)
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_AbsorbMagic(PlayerRef targetPlayer)
    {
        // 서버에서 타겟 플레이어에게 흡수당했다고 알림
        if (Runner.TryGetPlayerObject(targetPlayer, out NetworkObject targetObj))
        {
            if (targetObj.TryGetComponent(out PlayerController targetController))
            {
                targetController.RPC_NotifyAbsorbed();
                Debug.Log($"[PlayerController RPC] Player {Object.InputAuthority} absorbed magic from {targetPlayer}");
            }
        }
    }

    /// <summary>
    /// 마법이 흡수당했음을 알립니다. (State Authority → Target Player)
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NotifyAbsorbed()
    {
        // 흡수당한 플레이어의 마법 비활성화
        if (MagicActive)
        {
            MagicActive = false;
            MagicActivationTick = 0;
            ActiveMagicSlotNetworked = 0;
            
            // MagicController에 흡수당했다고 알림
            _magicController?.OnAbsorbed();
            
            Debug.Log($"[PlayerController RPC] Magic was absorbed by another player");
        }
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
            Debug.Log($"[PlayerController RPC] Loading panel shown for {duration}s");
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
        Debug.Log($"[PlayerController RPC] Loading panel shown for all");
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
        _previousPosition = (Vector2)targetPosition;
        DidTeleport = true;

        Debug.Log($"[PlayerController] Teleported to {targetPosition}");
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
            DidTeleport = false; // 초기화
        }
    }

    /// <summary>
    /// 캐릭터 뷰 오브젝트를 생성합니다. (CharacterIndex 동기화 후 호출)
    /// </summary>
    private void TryCreateView()
    {
        if (_viewObj != null || GameDataManager.Instance == null) return;

        var data = GameDataManager.Instance.CharacterService.GetCharacter(CharacterIndex);

        if (data != null && data.characterAnimator != null)
        {
            if (_viewObj != null) Destroy(_viewObj);

            GameObject instance = new GameObject("ViewObj");
            instance.transform.SetParent(transform, false);

            _viewObj = instance;

            _animator = _viewObj.AddComponent<Animator>();
            SpriteRenderer _spriteRenderer = _viewObj.AddComponent<SpriteRenderer>();

            _spriteRenderer.sprite = data.characterSprite;
            _spriteRenderer.material = GameDataManager.Instance.DefaltSpriteMat;

            _animator.runtimeAnimatorController = data.characterAnimator;
        }
    }
    #endregion

    #region Private Methods - Animation
    /// <summary>
    /// 실제 이동 거리를 기반으로 애니메이션 상태를 업데이트합니다. (서버 전용)
    /// </summary>
    private void UpdateAnimation()
    {
        if (_animator == null || IsDead) return;

        Vector2 currentPos = transform.position;
        Vector2 actualMovement = currentPos - _previousPosition;
        float actualSpeed = actualMovement.magnitude / Runner.DeltaTime;

        if (actualSpeed < MIN_MOVEMENT_SPEED)
        {
            AnimationState = "idle";
        }
        else
        {
            if (Mathf.Abs(actualMovement.y) > Mathf.Abs(actualMovement.x))
            {
                AnimationState = actualMovement.y > 0 ? "up" : "down";
            }
            else
            {
                AnimationState = "horizontal";
                ScaleX = actualMovement.x < 0 ? 1f : -1f;
            }
        }
    }

    /// <summary>
    /// 애니메이션을 재생합니다 (중복 재생 방지).
    /// </summary>
    private void PlayAnimation(string stateName)
    {
        if (_animator != null && !string.IsNullOrEmpty(stateName) && _lastAnimationState != stateName)
        {
            _animator.Play(stateName);
            _lastAnimationState = stateName;
        }
    }

    /// <summary>
    /// 뷰 오브젝트의 스케일을 업데이트합니다 (좌우 반전).
    /// </summary>
    private void UpdateScale()
    {
        if (_viewObj != null)
        {
            Vector3 scale = _viewObj.transform.localScale;
            scale.x = ScaleX;
            _viewObj.transform.localScale = scale;
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
                    PlayAnimation(AnimationState.ToString());
                    break;

                case nameof(ScaleX):
                    UpdateScale();
                    break;

                case nameof(CharacterIndex):
                    TryCreateView();
                    break;

                case nameof(MagicActive):
                    magicStateChanged = true;
                    break;

                case nameof(ActiveMagicSlotNetworked):
                    // MagicActive와 함께 변경될 수 있으므로 플래그만 설정
                    magicStateChanged = true;
                    break;

                case nameof(MagicAnchorLocalPosition):
                    _magicController?.UpdateAnchorPositionFromNetwork(MagicAnchorLocalPosition);
                    break;
            }
        }
        
        // 마법 상태가 변경되었을 때만 한 번 호출 (중복 방지)
        if (magicStateChanged)
        {
            _magicController?.UpdateMagicUIFromNetwork(MagicActive);
        }
    }
    #endregion
}
