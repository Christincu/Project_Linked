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
    [Networked] public Vector3 MagicAnchorLocalPosition { get; set; }
    [Networked] public bool MagicActive { get; set; }
    [Networked] public int MagicActivationTick { get; set; }
    
    // Character Magic Codes (스폰 시 CharacterData에서 로드)
    [Networked] public int Magic1Code { get; set; }
    [Networked] public int Magic2Code { get; set; }

    // Teleporter & Barrier
    [Networked] public TickTimer TeleportCooldownTimer { get; set; }
    [Networked] public NetworkBool DidTeleport { get; set; }
    [Networked] public TickTimer BarrierTimer { get; set; }
    [Networked] public bool HasBarrier { get; set; }
    
    // Dash Skill
    [Networked] public TickTimer DashSkillTimer { get; set; }
    [Networked] public bool HasDashSkill { get; set; }
    [Networked] public int DashEnhancementCount { get; set; }
    [Networked] public bool IsDashFinalEnhancement { get; set; }
    [Networked] public TickTimer DashStunTimer { get; set; }
    [Networked] public NetworkBool DashIsMoving { get; set; }
    [Networked] public Vector2 DashVelocity { get; set; }
    [Networked] public Vector2 DashLastInputDirection { get; set; }
    [Networked] public Vector2 DashPendingRecoilDirection { get; set; }
    [Networked] public NetworkBool DashIsWaitingToRecoil { get; set; }
    public DashMagicObject DashMagicObject { get; set; }
    
    // Threat Score
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

    // Sub-Components
    private PlayerBehavior _behavior;
    private PlayerMagicController _magicController;
    private PlayerState _state;
    private PlayerRigidBodyMovement _movement;
    private PlayerAnimationController _animationController;
    private PlayerViewManager _viewManager;
    private PlayerDetectionManager _detectionManager;
    private PlayerEffectManager _effectManager;
    
    // Internal State
    private bool _isTestMode;
    private int _barrierMoveSpeedEffectId = -1;
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

    public float HealthPercentage => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;
    public bool IsInvincible => !InvincibilityTimer.ExpiredOrNotRunning(Runner);
    public bool IsTestMode => _isTestMode;
    public float CurrentThreatScore => ThreatScore;
    #endregion

    #region Fusion Callbacks

    public override void Spawned()
    {
        base.Spawned();
        
        // 1. 기본 설정
        Runner.SetIsSimulated(Object, true);
        _isTestMode = MainGameManager.Instance != null && MainGameManager.Instance.IsTestMode;
        _gameDataManager = GameDataManager.Instance;

        // 2. 컴포넌트 캐싱 및 생성
        InitializeComponents();

        // 3. 네트워크 변수 초기값 설정 (서버 권한)
        InitializeNetworkState();

        // 4. 하위 시스템 초기화 (데이터 로드 등)
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

        // 서버: 체력 초기화
        if (Object.HasStateAuthority)
        {
            // 이미 설정된 값이 없다면 초기값 적용 (재접속 시 데이터 보존 고려)
            if (MaxHealth <= 0)
            {
                MaxHealth = initialData.MaxHealth;
                CurrentHealth = initialData.StartingHealth;
                IsDead = false;
            }
        }

        // 서버: 마법 코드 초기화 (CharacterIndex가 설정된 후)
        if (Object.HasStateAuthority && CharacterIndex >= 0)
        {
            InitializeMagicCodes();
        }
        
        // 로컬/리모트 공통: 하위 컴포넌트 의존성 주입
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

        // View Object(외형) 생성
        _viewManager?.TryCreateView();
    }

    public override void FixedUpdateNetwork()
    {
        if (_movement == null || _state == null) return;

        InputData? inputData = null;
        if (GetInput<InputData>(out var data))
        {
            inputData = data;
        }

        // 1. 사망 상태가 아닐 때만 동작
        if (!_state.IsDead)
        {
            HandleMovement(inputData);
            
            // 돌진 중이 아닐 때만 마법 사용 가능
            if (inputData.HasValue && !HasDashSkill)
            {
                _magicController?.ProcessInput(inputData.Value, this, _isTestMode);
            }
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
            if (_state.IsDead && _state.RespawnTimer.Expired(Runner))
            {
                _state.Respawn();
            }

            _animationController?.UpdateAnimation();

            if (DidTeleport) DidTeleport = false;

            UpdateBarrierState();
            UpdateThreatScore();
            _effectManager?.UpdateEffects();
        }
    }

    public override void Render()
    {
        // 네트워크 변수 변경 감지 및 시각적 동기화
        DetectNetworkChanges();
        _viewManager?.SyncViewObjPosition();
        _magicController?.OnRender();
    }

    private void HandleMovement(InputData? inputData)
    {
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
        
        // 마법 코드 초기화 (CharacterIndex 변경 시)
        if (Object.HasStateAuthority)
        {
            InitializeMagicCodes();
        }
        
        _viewManager?.TryCreateView();
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
        _movement?.ResetVelocity();
        _animationController?.Initialize(this);
    }

    #endregion

    #region RPC Methods

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_UpdateMagicAnchorPosition(Vector3 localPosition)
    {
        MagicAnchorLocalPosition = localPosition;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_ActivateMagic(int magicSlot, int activatedMagicCode)
    {
        MagicActive = true;
        ActivatedMagicCode = activatedMagicCode;
        AbsorbedMagicCode = -1;
        MagicActivationTick = Runner.Tick;
        ActiveMagicSlotNetworked = magicSlot;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_DeactivateMagic()
    {
        DeactivateMagicInternal();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_ChangeMagicCode(int slot, int magicCode)
    {
        if (slot == 1)
        {
            Magic1Code = magicCode;
            // 현재 사용 중인 슬롯의 마법이 바뀌면 활성화된 마법 코드도 갱신
            if (MagicActive && ActiveMagicSlotNetworked == 1)
            {
                ActivatedMagicCode = magicCode;
            }
        }
        else if (slot == 2)
        {
            Magic2Code = magicCode;
            // 현재 사용 중인 슬롯의 마법이 바뀌면 활성화된 마법 코드도 갱신
            if (MagicActive && ActiveMagicSlotNetworked == 2)
            {
                ActivatedMagicCode = magicCode;
            }
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_CastMagic(Vector3 targetPosition)
    {
        if (_magicController != null)
        {
            _magicController.CastMagic(targetPosition);
            
            // 보호막 마법(코드 10)은 선택 후 비활성화되므로 즉시 끄지 않음
            int magicCodeToCast = GetCurrentMagicCodeToCast();
            if (magicCodeToCast != 10)
            {
                DeactivateMagicInternal();
            }
        }
    }

    public void DeactivateMagicInternal()
    {
        MagicActive = false;
        MagicActivationTick = 0;
        ActivatedMagicCode = -1;
        AbsorbedMagicCode = -1;
        ActiveMagicSlotNetworked = 0;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_ApplyBarrier(NetworkId targetPlayerId)
    {
        if (_magicController == null || Runner == null) return;
        
        var targetObj = Runner.FindObject(targetPlayerId);
        if (targetObj != null)
        {
            var targetPlayer = targetObj.GetComponent<PlayerController>();
            if (targetPlayer != null && !targetPlayer.IsDead)
            {
                _magicController.GetBarrierMagicHandler()?.ApplyBarrierToPlayer(targetPlayer);
            }
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_TriggerExplosionVfx(Vector3 position, float radius)
    {
        BarrierMagicCombinationData barrierData = GetBarrierData();
        if (barrierData?.explosionVfxPrefab != null)
        {
            GameObject vfx = Instantiate(barrierData.explosionVfxPrefab, position, Quaternion.identity);
            // 폭발 반지름에 배율을 적용하여 이펙트 크기 설정
            float vfxScale = radius * barrierData.explosionVfxScale;
            vfx.transform.localScale = Vector3.one * vfxScale;
            Destroy(vfx, 2.0f);
        }
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

        Movement?.ResetVelocity();
        DidTeleport = true;
        _animationController?.Initialize(this);
    }

    #endregion

    #region Private Methods - Initialization & Helper

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

    private void InitializeNetworkState()
    {
        if (!Object.HasStateAuthority) return;

        ScaleX = 1f;
        AnimationState = "idle";
        IsDead = false;
        
        // 타이머 및 상태 초기화
        TeleportCooldownTimer = TickTimer.None;
        BarrierTimer = TickTimer.None;
        DashSkillTimer = TickTimer.None;
        DashStunTimer = TickTimer.None;
        
        DidTeleport = false;
        HasBarrier = false;
        HasDashSkill = false;
        DashIsMoving = false;
        
        DashEnhancementCount = 0;
        IsDashFinalEnhancement = false;
        ThreatScore = 0f;
        
        DashVelocity = Vector2.zero;
        DashLastInputDirection = Vector2.zero;
        DashPendingRecoilDirection = Vector2.zero;
        
        // 마법 코드 초기화 (CharacterData에서 로드)
        InitializeMagicCodes();

        ActivatedMagicCode = -1;
        AbsorbedMagicCode = -1;
        ActiveMagicSlotNetworked = 0;
        MagicActive = false;
        MagicActivationTick = 0;
    }
    
    /// <summary>
    /// CharacterData에서 마법 코드를 로드하여 네트워크 변수에 저장합니다.
    /// </summary>
    private void InitializeMagicCodes()
    {
        if (_gameDataManager == null) return;
        
        CharacterData characterData = _gameDataManager.CharacterService.GetCharacter(CharacterIndex);
        if (characterData != null)
        {
            Magic1Code = characterData.magicData1 != null ? characterData.magicData1.magicCode : -1;
            Magic2Code = characterData.magicData2 != null ? characterData.magicData2.magicCode : -1;
        }
        else
        {
            Magic1Code = -1;
            Magic2Code = -1;
        }
    }

    public bool IsEnemyNearby(EnemyDetector enemy)
    {
        return _detectionManager != null && _detectionManager.IsEnemyNearby(enemy);
    }

    private int GetCurrentMagicCodeToCast()
    {
        if (AbsorbedMagicCode != -1 && ActivatedMagicCode != -1)
        {
            int combined = _gameDataManager.MagicService.GetCombinedMagic(ActivatedMagicCode, AbsorbedMagicCode);
            if (combined != -1) return combined;
        }
        return ActivatedMagicCode;
    }
    
    #endregion

    #region Network Sync Logic (Change Detector)

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
                    // CharacterIndex 변경 시 마법 코드도 업데이트
                    if (Object.HasStateAuthority)
                    {
                        InitializeMagicCodes();
                    }
                    _viewManager?.TryCreateView();
                    break;
                case nameof(CurrentHealth):
                case nameof(MaxHealth):
                    _state?.OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
                    break;
                case nameof(MagicActive):
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
                case nameof(Magic1Code):
                case nameof(Magic2Code):
                    // 마법 코드 변경 시 UI 업데이트 (MainCanvas에서 감지)
                    // 여기서는 특별한 처리가 필요 없지만, 변경 감지를 위해 명시적으로 추가
                    break;
                case nameof(HasBarrier):
                    UpdateThreatScore();
                    break;
            }
        }

        if (magicStateChanged)
        {
            MagicController.UpdateMagicUIState(MagicActive);
        }
    }

    #endregion

    #region Barrier Logic (Cleaned up)

    private void UpdateBarrierState()
    {
        if (!HasBarrier || IsDead) return;

        if (BarrierTimer.IsRunning)
        {
            float remainingTime = BarrierTimer.RemainingTime(Runner) ?? 0f;
            var barrierData = GetBarrierData();
            
            // 데이터가 없으면 기본값 처리 (MoveSpeedEffect 관리)
            ManageBarrierMoveSpeedEffect(remainingTime, barrierData);

            if (BarrierTimer.Expired(Runner))
            {
                // 타이머 만료 시 베리어만 해제 (플레이어가 살아있으면 폭발하지 않음)
                // 폭발은 플레이어가 죽었을 때만 발생 (PlayerState.HandleDeath에서 처리)
                ClearBarrierState();
                Debug.Log($"[BarrierMagic] {name} barrier expired");
            }
        }
        else
        {
            // 타이머가 돌지 않는데 베리어 상태라면 강제 해제
            ClearBarrierState();
        }
    }

    private void ManageBarrierMoveSpeedEffect(float remainingTime, BarrierMagicCombinationData data)
    {
        float speedUpDuration = data != null ? data.moveSpeedEffectDuration : 3f; // 기본 3초
        float barrierDuration = data != null ? data.barrierDuration : 15f;
        float speedEndTime = barrierDuration - speedUpDuration;

        // 속도 증가 구간 (시작 직후 N초간)
        if (remainingTime > speedEndTime)
        {
            if (_barrierMoveSpeedEffectId == -1 && _effectManager != null)
            {
                float multiplier = data != null ? data.moveSpeedMultiplier : 1.5f;
                float duration = remainingTime - speedEndTime;
                _barrierMoveSpeedEffectId = _effectManager.AddEffect(EffectType.MoveSpeed, multiplier, duration);
            }
        }
        else
        {
            RemoveBarrierSpeedEffect();
        }
    }

    private void ClearBarrierState()
    {
        HasBarrier = false;
        BarrierTimer = TickTimer.None;
        RemoveBarrierSpeedEffect();
        UpdateThreatScore();
    }

    private void RemoveBarrierSpeedEffect()
    {
        if (_barrierMoveSpeedEffectId != -1 && _effectManager != null)
        {
            _effectManager.RemoveEffect(_barrierMoveSpeedEffectId);
            _barrierMoveSpeedEffectId = -1;
        }
    }

    private void UpdateThreatScore()
    {
        if (!Object.HasStateAuthority) return;

        if (IsDead)
        {
            ThreatScore = 0f;
            return;
        }

        if (HasBarrier)
        {
            var data = GetBarrierData();
            ThreatScore = data != null ? data.threatScore : 200f;
        }
        else
        {
            ThreatScore = 0f;
        }
    }

    private BarrierMagicCombinationData GetBarrierData()
    {
        if (_gameDataManager?.MagicService == null) return null;
        return _gameDataManager.MagicService.GetCombinationDataByResult(10) as BarrierMagicCombinationData;
    }

    #endregion
}