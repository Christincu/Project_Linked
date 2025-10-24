using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

/// <summary>
/// 네트워크 동기화를 지원하는 플레이어 컨트롤러
/// Photon Fusion을 사용하여 플레이어를 총괄 관리합니다.
/// </summary>
public class PlayerController : NetworkBehaviour, IPlayerLeft
{
    #region Constants
    private const float MIN_MOVEMENT_SPEED = 0.1f; // Wall collision detection threshold
    #endregion

    #region Networked Properties
    // Position & Animation
    [Networked] public Vector3 NetworkedPosition { get; set; }
    [Networked] public Vector2 NetworkedVelocity { get; set; }
    [Networked] public NetworkString<_16> AnimationState { get; set; }
    [Networked] public float ScaleX { get; set; }
    
    // Player Info
    [Networked] public int PlayerSlot { get; set; } // Test mode: which slot's input to receive
    [Networked] public int CharacterIndex { get; set; } // Character index (network synced)
    
    // Health
    [Networked] public float CurrentHealth { get; set; }
    [Networked] public float MaxHealth { get; set; }
    [Networked] public NetworkBool IsDead { get; set; }
    [Networked] public TickTimer InvincibilityTimer { get; set; }

    // Magic
    [Networked] public bool MagicActive { get; set; }
    [Networked] public TickTimer MagicCooldownTimer { get; set; }
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
    private int _characterIndex = 0;
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
    #endregion

    #region Fusion Callbacks
    /// <summary>
    /// 네트워크 오브젝트 생성 시 호출됩니다.
    /// </summary>
    public override void Spawned()
    {
        _isTestMode = FindObjectOfType<TestGameManager>() != null;
        _previousPosition = transform.position;

        // 컴포넌트 초기화
        InitializeComponents();
        
        // 네트워크 상태 초기화
        InitializeNetworkState();
        
        // 뷰 생성
        TryCreateView();

        // 모든 초기화를 1프레임 후에 처리
        StartCoroutine(InitializeAllComponents());

        Debug.Log($"[PlayerController] Spawned - InputAuth: {Object.HasInputAuthority}, StateAuth: {Object.HasStateAuthority}");
    }

    /// <summary>
    /// 모든 컴포넌트를 초기화합니다.
    /// </summary>
    private IEnumerator InitializeAllComponents()
    {
        // 1프레임 대기 (모든 컴포넌트의 Spawned() 호출 보장)
        yield return null;

        // GameDataManager에서 InitialPlayerData 가져오기
        InitialPlayerData initialData = null;
        if (GameDataManager.Instance != null)
        {
            initialData = GameDataManager.Instance.InitialPlayerData;
        }
        else
        {
            Debug.LogError("[PlayerController] GameDataManager.Instance is null!");
            yield break;
        }

        // 1. 네트워크 변수 초기화 (서버만)
        if (Object.HasStateAuthority && initialData != null)
        {
            // Health 초기화
            MaxHealth = initialData.MaxHealth;
            CurrentHealth = initialData.StartingHealth;
            IsDead = false;
            
            Debug.Log($"[PlayerController] Network state initialized - HP: {CurrentHealth}/{MaxHealth}");
        }
        
        // 2. PlayerState 초기화
        if (_state != null)
        {
            _state.Initialize(this, initialData);
        }

        // 3. PlayerRigidBodyMovement 초기화
        if (_movement != null)
        {
            _movement.Initialize(this, initialData);
        }

        // 4. PlayerBehavior 초기화
        if (_behavior != null)
        {
            _behavior.Initialize(this);
        }

        // 5. PlayerMagicController 초기화
        if (_magicController != null)
        {
            _magicController.Initialize(this);
            _magicController.SetMagicUIReferences(_magicViewObj, _magicAnchor, _magicIdleFirstFloor, _magicIdleSecondFloor, _magicActiveFloor);
        }

        Debug.Log($"[PlayerController] All components initialized");

        // Canvas 등록 (클라이언트는 네트워크 동기화 대기)
        if (Object.HasStateAuthority)
        {
            // 서버는 즉시 등록
            RegisterToCanvas();
        }
        else
        {
            // 클라이언트는 MaxHealth 동기화 대기 후 등록
            StartCoroutine(WaitForNetworkSyncAndRegister());
        }
    }

    /// <summary>
    /// 클라이언트에서 네트워크 동기화를 대기하고 Canvas에 등록합니다.
    /// </summary>
    private IEnumerator WaitForNetworkSyncAndRegister()
    {
        // MaxHealth가 동기화될 때까지 대기
        while (MaxHealth <= 0)
        {
            yield return null;
        }
        
        Debug.Log($"[PlayerController] Network sync complete - HP: {CurrentHealth}/{MaxHealth}");
        
        // MainCanvas에 플레이어 등록
        RegisterToCanvas();
    }

    /// <summary>
    /// GameManager를 통해 MainCanvas에 자신을 등록합니다.
    /// </summary>
    private void RegisterToCanvas()
    {
        if (GameManager.Instance != null && GameManager.Instance.Canvas != null)
        {
            var canvas = GameManager.Instance.Canvas as MainCanvas;
            if (canvas != null)
            {
                canvas.RegisterPlayer(this);
                Debug.Log($"[PlayerController] Registered to MainCanvas");
            }
            else
            {
                Debug.LogWarning($"[PlayerController] Canvas is not MainCanvas type");
            }
        }
        else
        {
            Debug.LogWarning($"[PlayerController] GameManager or Canvas not found");
        }
    }

    /// <summary>
    /// Fusion 네트워크 입력 처리 (매 틱마다 호출)
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        // 초기화 전이면 실행 안 함
        if (_movement == null) return;

        // 입력 처리
        InputData? inputData = null;
        if (GetInput<InputData>(out var data))
        {
            inputData = data;
        }
        
        _movement.ProcessInput(inputData, _isTestMode, PlayerSlot);

        // 마법 컨트롤러 입력 처리
        if (_magicController != null && inputData.HasValue)
        {
            _magicController.ProcessInput(inputData.Value, this, _isTestMode);
        }

        // 이동
        _movement.Move();
        
        // StateAuthority(서버)에서만 네트워크 상태 업데이트
        if (Object.HasStateAuthority)
        {
            UpdateAnimation();
            NetworkedVelocity = _movement.GetVelocity();
            
            // NetworkRigidbody2D가 없을 때만 위치 동기화
            if (!_movement.HasNetworkRigidbody())
            {
                NetworkedPosition = transform.position;
            }
        }

        _previousPosition = transform.position;
    }

    /// <summary>
    /// 렌더 업데이트 (네트워크 상태 변경 감지 및 보간)
    /// </summary>
    public override void Render()
    {
        DetectNetworkChanges();
        
        if (_movement != null)
        {
            _movement.InterpolatePosition(NetworkedPosition, Object.HasInputAuthority);
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
    #endregion

    #region Public Methods - Character
    /// <summary>
    /// 캐릭터 인덱스를 설정하고 뷰 오브젝트를 생성합니다.
    /// </summary>
    public void SetCharacterIndex(int characterIndex)
    {
        CharacterIndex = characterIndex;
        _characterIndex = characterIndex;
        TryCreateView();
    }
    #endregion

    #region Public Methods - State Callbacks
    /// <summary>
    /// PlayerState에서 사망 시 호출됩니다.
    /// </summary>
    public void OnPlayerDied()
    {
        AnimationState = "die";
        Debug.Log($"[PlayerController] Player died");
    }

    /// <summary>
    /// PlayerState에서 리스폰 시 호출됩니다.
    /// </summary>
    public void OnPlayerRespawned(Vector3 spawnPosition)
    {
        transform.position = spawnPosition;
        _previousPosition = spawnPosition;
        AnimationState = "idle";
        
        if (_movement != null)
        {
            _movement.ResetVelocity();
        }
        
        Debug.Log($"[PlayerController] Player respawned at {spawnPosition}");
    }
    #endregion

    #region Public Methods - Component Access (허브 역할)
    /// <summary>
    /// 기본 공격을 실행합니다 (PlayerBehavior로 위임).
    /// </summary>
    public void PerformAttack()
    {
        if (_behavior != null)
        {
            _behavior.PerformAttack();
        }
    }

    /// <summary>
    /// 스킬을 사용합니다 (PlayerBehavior로 위임).
    /// </summary>
    public void UseSkill(int skillIndex)
    {
        if (_behavior != null)
        {
            _behavior.UseSkill(skillIndex);
        }
    }

    /// <summary>
    /// 마법을 시전합니다 (PlayerMagicController로 위임).
    /// </summary>
    public void CastMagic(Vector3 targetPosition)
    {
        if (_magicController != null)
        {
            _magicController.CastMagic(targetPosition);
        }
    }

    /// <summary>
    /// 데미지를 받습니다 (PlayerState로 위임).
    /// </summary>
    public void TakeDamage(float damage, PlayerRef attacker = default)
    {
        if (_state != null)
        {
            _state.TakeDamage(damage, attacker);
        }
    }

    /// <summary>
    /// 치료를 받습니다 (PlayerState로 위임).
    /// </summary>
    public void Heal(float healAmount)
    {
        if (_state != null)
        {
            _state.Heal(healAmount);
        }
    }

    /// <summary>
    /// 상호작용을 실행합니다 (PlayerBehavior로 위임).
    /// </summary>
    public void Interact()
    {
        if (_behavior != null)
        {
            _behavior.Interact();
        }
    }

    /// <summary>
    /// 체력 정보를 가져옵니다.
    /// </summary>
    public (float current, float max) GetHealth()
    {
        return (CurrentHealth, MaxHealth);
    }

    /// <summary>
    /// 플레이어가 사망 상태인지 확인합니다.
    /// </summary>
    public bool IsPlayerDead()
    {
        return IsDead;
    }
    #endregion

    #region Private Methods - Initialization
    /// <summary>
    /// 컴포넌트를 초기화합니다.
    /// </summary>
    private void InitializeComponents()
    {
        // 모든 플레이어 컴포넌트 찾기/추가
        _state = GetComponent<PlayerState>();
        if (_state == null)
        {
            _state = gameObject.AddComponent<PlayerState>();
        }

        _behavior = GetComponent<PlayerBehavior>();
        if (_behavior == null)
        {
            _behavior = gameObject.AddComponent<PlayerBehavior>();
        }

        _magicController = GetComponent<PlayerMagicController>();
        if (_magicController == null)
        {
            _magicController = gameObject.AddComponent<PlayerMagicController>();
        }

        _movement = GetComponent<PlayerRigidBodyMovement>();
        if (_movement == null)
        {
            _movement = gameObject.AddComponent<PlayerRigidBodyMovement>();
        }

        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        
        Debug.Log($"[PlayerController] All components initialized");
    }

    /// <summary>
    /// 네트워크 상태 초기값을 설정합니다.
    /// </summary>
    private void InitializeNetworkState()
    {
        if (Object.HasStateAuthority)
        {
            // Animation
            ScaleX = 1f;
            AnimationState = "idle";
            
            // Health & Mana - GameDataManager에서 가져올 예정 (InitializeAllComponents에서)
            // 여기서는 기본값만 설정
            IsDead = false;
        }
    }

    /// <summary>
    /// 캐릭터 뷰 오브젝트를 생성합니다.
    /// </summary>
private void TryCreateView()
{
    if (_viewObj != null) return;
    if (GameDataManager.Instance == null) return;

    int index = CharacterIndex >= 0 ? CharacterIndex : _characterIndex;
    var data = GameDataManager.Instance.CharacterService.GetCharacter(index);
    
    if (data != null && data.characterAnimator != null)
    {
        GameObject instance = new GameObject("ViewObj");
        instance.transform.SetParent(transform, false);

        _viewObj = instance;
        _animator = _viewObj.AddComponent<Animator>();
        SpriteRenderer _spriteRenderer = _viewObj.AddComponent<SpriteRenderer>();

        _spriteRenderer.sprite = data.characterSprite;
        _spriteRenderer.material = GameDataManager.Instance.DefaltSpriteMat;

        _animator.runtimeAnimatorController = data.characterAnimator;

        Debug.Log($"[PlayerController] Character view created: {index}");
    }
    else
    {
        Debug.LogError($"[PlayerController] Character view not found: {index}");
    }
}
    #endregion

    #region Private Methods - Animation
    /// <summary>
    /// 실제 이동 거리를 기반으로 애니메이션 상태를 업데이트합니다.
    /// </summary>
    private void UpdateAnimation()
    {
        if (_animator == null) return;

        // 사망 상태면 애니메이션 업데이트 안 함
        if (IsDead) return;

        Vector2 currentPos = transform.position;
        Vector2 actualMovement = currentPos - _previousPosition;
        float actualSpeed = actualMovement.magnitude / Runner.DeltaTime;

        // 실제로 움직이지 않으면 idle
        if (actualSpeed < MIN_MOVEMENT_SPEED)
        {
            AnimationState = "idle";
        }
        else
        {
            // 실제 이동 방향으로 애니메이션 결정
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
        // 뷰 오브젝트 스케일 업데이트
        if (_viewObj != null)
        {
            Vector3 scale = _viewObj.transform.localScale;
            scale.x = ScaleX;
            _viewObj.transform.localScale = scale;
        }
        
        // 마법 앵커는 좌우 반전하지 않음 (마우스 위치에 따라 결정됨)
    }
    #endregion

    #region Private Methods - Network Synchronization
    /// <summary>
    /// 네트워크 상태 변경을 감지하고 처리합니다.
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
                    if (_magicController != null)
                    {
                        _magicController.UpdateMagicUIFromNetwork(MagicActive);
                    }
                    break;
            }
        }
    }
    #endregion
}
