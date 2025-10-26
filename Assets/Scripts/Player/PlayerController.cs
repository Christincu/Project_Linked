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
    [Networked] public Vector3 MagicAnchorLocalPosition { get; set; } // 앵커의 로컬 위치 동기화
    [Networked] public bool MagicActive { get; set; }
    [Networked] public TickTimer MagicCooldownTimer { get; set; }
    [Networked] public int MagicActivationTick { get; set; }

    // Teleporter
    [Networked] public TickTimer TeleportCooldownTimer { get; set; }

    // ⭐ [추가됨] 순간이동 플래그 (보간 건너뛰기용)
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

    // ---

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
    }
    #endregion

    // ---

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

    /// <summary>
    /// 마법 활성화 틱을 설정합니다. (Input Authority에서 호출, RPC로 서버에 전달)
    /// </summary>
    public void SetMagicActivationTick(int tick)
    {
        if (Object.HasInputAuthority)
        {
            RPC_SetMagicActivationTick(tick);
        }
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
    /// 마법 활성화 틱을 설정합니다. (Input Authority → State Authority)
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetMagicActivationTick(int tick)
    {
        MagicActivationTick = tick;
    }

    /// <summary>
    /// 서버에서 호출되어 플레이어의 위치를 강제로 설정합니다. (MapTeleporter 사용)
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_TeleportPlayer(Vector3 targetPosition)
    {
        if (Object.HasInputAuthority)
        {
            StartCoroutine(TeleportWithLoadingScreen(targetPosition));
        }
        else
        {
            ExecuteTeleport(targetPosition);
        }
    }
    
    private IEnumerator TeleportWithLoadingScreen(Vector3 targetPosition)
    {
        LoadingPanel.Show();
        
        yield return new WaitForSeconds(0.3f);
        
        ExecuteTeleport(targetPosition);
        
        yield return new WaitForSeconds(0.3f);
        
        LoadingPanel.Hide();
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
                    _magicController?.UpdateMagicUIFromNetwork(MagicActive);
                    break;

                case nameof(MagicAnchorLocalPosition):
                    _magicController?.UpdateAnchorPositionFromNetwork(MagicAnchorLocalPosition);
                    break;
            }
        }
    }
    #endregion
}
