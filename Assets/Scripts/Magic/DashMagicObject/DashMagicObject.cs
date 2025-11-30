using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

/// <summary>
/// 화염 돌진 마법 오브젝트
/// 두 플레이어의 마법 합체 발동 시 각 플레이어에게 부착되는 오브젝트
/// 돌진 스킬의 코어 로직을 처리합니다.
/// </summary>
public partial class DashMagicObject : NetworkBehaviour
{
    #region Networked Properties
    /// <summary>
    /// 이 대시 오브젝트의 소유 플레이어(PlayerRef)를 네트워크로 동기화합니다.
    /// 클라이언트는 이 정보를 기반으로 _owner 참조를 복원합니다.
    /// </summary>
    [Networked] private PlayerRef OwnerPlayer { get; set; }
    #endregion

    #region Serialized Fields
    [Header("Collision Settings")]
    [Tooltip("공격 판정용 Circle Collider 반지름")]
    [SerializeField] private float attackColliderRadius = 1f;
    
    [Tooltip("적 레이어 마스크 (충돌 감지용)")]
    [SerializeField] private LayerMask enemyLayer;
    
    [Tooltip("플레이어 레이어 마스크 (충돌 감지용)")]
    [SerializeField] private LayerMask playerLayer;
    #endregion
    
    #region Private Fields
    private PlayerController _owner;
    private DashMagicCombinationData _dashData;
    private GameDataManager _gameDataManager;
    private CircleCollider2D _attackCollider;
    
    // 이동 관련
    private Vector2 _currentVelocity = Vector2.zero;
    private Vector2 _lastInputDirection = Vector2.zero;
    private bool _hasReceivedInput = false; // 입력을 받았는지 여부
    
    // 충돌 관련
    private HashSet<EnemyController> _recentHitEnemies = new HashSet<EnemyController>(); // 최근 충돌한 적 (재충돌 방지)
    // [개선] NetworkId 기반 쿨다운 관리 (더 안정적)
    private Dictionary<NetworkId, TickTimer> _enemyCollisionCooldowns = new Dictionary<NetworkId, TickTimer>();
    private HashSet<PlayerController> _recentHitPlayers = new HashSet<PlayerController>(); // 최근 충돌한 플레이어
    
    // 베리어 시각화 관련
    private SpriteRenderer _barrierSpriteRenderer;
    private CircleCollider2D _barrierCollider; // 베리어 콜라이더
    private int _lastEnhancementCount = -999; // 초기값을 다르게 설정하여 첫 프레임 갱신 보장
    private bool _lastIsFinalEnhancement = false;
    #endregion
    
    #region Unity / Fusion Callbacks
    void Awake()
    {
        // [개선] 공격 판정용 Circle Collider는 이제 시각적/디버그 용도로만 사용
        // 실제 충돌 감지는 FixedUpdateNetwork에서 OverlapCircle 사용
        _attackCollider = gameObject.AddComponent<CircleCollider2D>();
        _attackCollider.radius = attackColliderRadius;
        _attackCollider.isTrigger = true;
        _attackCollider.enabled = false; // 초기에는 비활성화
        
        // 레이어 마스크 초기화 (에디터에서 설정되지 않은 경우)
        if (enemyLayer.value == 0)
        {
            enemyLayer = LayerMask.GetMask("Enemy", "Default");
        }
        if (playerLayer.value == 0)
        {
            playerLayer = LayerMask.GetMask("Player", "Default");
        }
    }

    /// <summary>
    /// 네트워크 오브젝트가 스폰되었을 때 호출됩니다.
    /// (모든 클라이언트에서 실행되며, OwnerRef를 기반으로 _owner를 복원합니다.)
    /// </summary>
    public override void Spawned()
    {
        base.Spawned();

        // [수정] 클라이언트에서도 초기화 수행 (서버는 Initialize에서 이미 했지만, 클라이언트는 여기서 해야 함)
        InitializeClientComponents();
        
        // 이미 서버에서 Initialize로 _owner를 설정했을 수 있으므로,
        // _owner가 비어 있고 OwnerPlayer가 유효할 때만 복원 시도
        if (_owner == null && Runner != null && OwnerPlayer != PlayerRef.None)
        {
            // MainGameManager를 통해 PlayerController 찾기
            if (MainGameManager.Instance != null)
            {
                _owner = MainGameManager.Instance.GetPlayer(OwnerPlayer);
            }
        }
        
        // [추가] OwnerPlayer가 아직 설정되지 않았을 수 있으므로, 
        // 스폰된 오브젝트의 InputAuthority를 통해 찾기 시도
        if (_owner == null && Runner != null)
        {
            // 스폰된 오브젝트의 InputAuthority를 통해 플레이어 찾기
            PlayerRef inputAuth = Object.InputAuthority;
            if (inputAuth != PlayerRef.None && MainGameManager.Instance != null)
            {
                _owner = MainGameManager.Instance.GetPlayer(inputAuth);
            }
        }
    }
    
    /// <summary>
    /// [핵심 수정] Render 메서드에서 시각적 업데이트를 강력하게 처리
    /// 모든 클라이언트에서 실행되며, 네트워크 변수 변경을 감지하여 스프라이트를 업데이트합니다.
    /// </summary>
    public override void Render()
    {
        // [수정] 클라이언트 컴포넌트 초기화 확인
        if (_barrierSpriteRenderer == null)
        {
            InitializeClientComponents();
        }
        
        // [수정] OwnerPlayer가 설정될 때까지 대기하거나, InputAuthority로 찾기
        if (_owner == null)
        {
            // 1. OwnerPlayer를 통해 찾기
            if (Runner != null && OwnerPlayer != PlayerRef.None)
            {
                if (MainGameManager.Instance != null)
                {
                    _owner = MainGameManager.Instance.GetPlayer(OwnerPlayer);
                }
            }
            
            // 2. OwnerPlayer가 아직 설정되지 않았으면 InputAuthority로 찾기
            if (_owner == null && Runner != null)
            {
                PlayerRef inputAuth = Object.InputAuthority;
                if (inputAuth != PlayerRef.None && MainGameManager.Instance != null)
                {
                    _owner = MainGameManager.Instance.GetPlayer(inputAuth);
                }
            }
            
            // 3. 여전히 못 찾았으면 씬에서 직접 찾기 (Fallback)
            if (_owner == null && Runner != null)
            {
                PlayerRef inputAuth = Object.InputAuthority;
                if (inputAuth != PlayerRef.None)
                {
                    var allPlayers = FindObjectsOfType<PlayerController>();
                    foreach (var player in allPlayers)
                    {
                        if (player != null && player.Object != null && 
                            player.Object.IsValid && player.Object.InputAuthority == inputAuth)
                        {
                            _owner = player;
                            break;
                        }
                    }
                }
            }
        }

        if (_owner == null) 
        {
            // [디버그] _owner를 찾지 못한 경우 로그 출력 (너무 자주 출력되지 않도록)
            if (Time.frameCount % 60 == 0) // 1초마다 한 번만 로그
            {
                Debug.LogWarning($"[DashMagicObject] Cannot find owner. OwnerPlayer: {OwnerPlayer}, InputAuthority: {Object.InputAuthority}");
            }
            return;
        }

        // [수정] 위치 동기화 방식 변경: 부모-자식 관계 활용
        // 매 프레임 owner의 위치로 강제 이동시키는 대신,
        // 부모-자식 관계(SetParent)를 믿고 로컬 좌표를 0으로 유지합니다.
        // 부모(Player)는 이미 NetworkRigidbody2D가 보간(Interpolation)을 해주고 있습니다.
        if (transform.parent != _owner.transform)
        {
            transform.SetParent(_owner.transform);
        }
        
        // 로컬 위치를 0으로 고정 (부모 따라가기)
        // 이렇게 하면 Player의 부드러운 이동을 그대로 따라갑니다.
        transform.localPosition = Vector3.zero;

        // 1. 데이터 로드 안전장치 (클라이언트 늦은 로드 대응)
        if (_dashData == null) LoadDashData();
        if (_dashData == null) return;

        // 2. 스킬 활성화 상태일 때만 스프라이트 표시
        if (_owner.HasDashSkill)
        {
            // 3. 상태 변경 감지 OR 스프라이트가 비어있으면 갱신
            bool stateChanged = (_lastEnhancementCount != _owner.DashEnhancementCount) ||
                                (_lastIsFinalEnhancement != _owner.IsDashFinalEnhancement);

            if (stateChanged || (_barrierSpriteRenderer != null && _barrierSpriteRenderer.sprite == null))
            {
                UpdateBarrierSprite();

                // 상태 갱신
                _lastEnhancementCount = _owner.DashEnhancementCount;
                _lastIsFinalEnhancement = _owner.IsDashFinalEnhancement;
            }
        }
        else
        {
            // 스킬 비활성화 시 숨김
            if (_barrierSpriteRenderer != null && _barrierSpriteRenderer.sprite != null)
            {
                _barrierSpriteRenderer.sprite = null;
                _lastEnhancementCount = -999; // 리셋
            }
        }
    }
    
    /// <summary>
    /// 네트워크 틱과 동기화되어 실행되는 업데이트 메서드
    /// [클라이언트 예측] State Authority(서버) OR Input Authority(조종하는 클라이언트)에서 실행됩니다.
    /// 프록시(다른 플레이어 화면)는 실행하지 않습니다.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (_owner == null || Runner == null) return;
        
        // [수정] 클라이언트 예측: 서버(StateAuth) OR 조종하는 클라이언트(InputAuth) 둘 다 실행
        // 프록시(다른 플레이어 화면)는 실행하지 않음 -> 서버 데이터만 받아옴
        if (!_owner.Object.HasStateAuthority && !_owner.Object.HasInputAuthority) return;
        
        // [수정] 데이터가 없으면 로드 시도하고, 그래도 없으면 이번 프레임만 스킵
        if (_dashData == null)
        {
            LoadDashData();
            if (_dashData == null) return; // 이번 틱은 포기하지만 다음 틱에 다시 시도
        }
        
        // 스킬 상태 확인
        // 주의: 클라이언트는 아직 서버의 HasDashSkill=true를 못 받았을 수 있음.
        // 하지만 ActivateDashSkill을 로컬에서 예측 실행했다면 HasDashSkill이 true일 것임.
        if (!_owner.HasDashSkill)
        {
            // 클라이언트 예측 중에는 종료 조건을 좀 더 유연하게 처리하거나,
            // 서버 데이터가 왔을 때 보정되도록 둡니다.
            if (_owner.Object.HasStateAuthority) EndDashSkill();
            return;
        }

        // 타이머가 만료되었을 때만 종료 (아직 시작되지 않은 경우는 제외)
        if (_owner.DashSkillTimer.Expired(Runner))
        {
            EndDashSkill();
            return;
        }
        
        // [신규] 반동 대기(Freeze) 및 발사 로직 처리
        if (_owner.DashIsWaitingToRecoil)
        {
            // 정지 타이머가 아직 도는 중이라면 -> 위치 고정 (Freeze)
            if (_owner.DashStunTimer.IsRunning && !_owner.DashStunTimer.Expired(Runner))
            {
                _owner.DashVelocity = Vector2.zero;
                _owner.DashIsMoving = false;
                UpdateRigidbodyVelocity(Vector2.zero);
                
                // 공격 콜라이더 비활성화 (정지 중에는 충돌 처리 안 함)
                if (_attackCollider != null)
                {
                    _attackCollider.enabled = false;
                }
                return; // 이동 로직 실행 안 함
            }
            // 정지 타이머가 만료되었다면 -> 발사 (Launch)
            else
            {
                ExecuteRecoil();
            }
        }
        
        // 정지/행동불능 타이머 확인 (반동 대기 중이 아닐 때)
        if (_owner.DashStunTimer.IsRunning && !_owner.DashStunTimer.Expired(Runner))
        {
            // 정지 상태 - 이동 및 입력 처리 안 함
            _owner.DashVelocity = Vector2.zero;
            _owner.DashIsMoving = false;
            UpdateRigidbodyVelocity(Vector2.zero);
            return;
        }
        
        // 정지 상태가 끝났으면 공격 콜라이더 활성화
        if (!_attackCollider.enabled && (_owner.DashStunTimer.ExpiredOrNotRunning(Runner)))
        {
            _attackCollider.enabled = true;
        }
        
        // 입력 처리 및 이동 업데이트 (반동 대기 중이 아닐 때만)
        if (!_owner.DashIsWaitingToRecoil)
        {
            ProcessDashMovement();
        }
        
        // [핵심 개선] 물리 충돌 감지 (State Authority에서만 실행)
        // 클라이언트 예측에서는 이동만 하고, 충돌은 서버에서만 처리
        if (_owner.Object.HasStateAuthority)
        {
            DetectCollisions();
        }
        
        // 스킬 종료 조건 확인
        CheckEndConditions();
        
        // 충돌 타임아웃 정리
        CleanupCollisionCooldowns();
    }
    #endregion
    
    #region Initialization
    /// <summary>
    /// 돌진 스킬 오브젝트 초기화
    /// </summary>
    public void Initialize(PlayerController owner, DashMagicCombinationData dashData)
    {
        _owner = owner;
        _dashData = dashData;

        // 서버(StateAuthority)에서만 OwnerRef를 설정하면,
        // 클라이언트는 Spawned/Render에서 이 값을 통해 _owner를 복원할 수 있습니다.
        if (Object != null && Object.HasStateAuthority && _owner != null && _owner.Object != null)
        {
            OwnerPlayer = _owner.Object.InputAuthority;
        }
        
        if (_owner == null)
        {
            return;
        }

        // 데이터가 null이면 스스로 찾아봄
        if (_dashData == null)
        {
            LoadDashData();
        }
        
        // 서버에서만 계층 구조를 설정 (위치는 Render에서 강제로 동기화하므로 안전)
        if (Object != null && Object.HasStateAuthority)
        {
            transform.SetParent(_owner.transform);
            transform.localPosition = Vector3.zero;
        }
        
        // [중요] 공격 콜라이더 설정 (이게 없으면 충돌 감지 안됨)
        if (_attackCollider == null)
        {
            _attackCollider = GetComponent<CircleCollider2D>();
        }
        if (_attackCollider != null && _dashData != null)
        {
            _attackCollider.radius = attackColliderRadius;
        }
        
        // 베리어 스프라이트 렌더러 가져오기
        _barrierSpriteRenderer = GetComponent<SpriteRenderer>();
        if (_barrierSpriteRenderer == null)
        {
            _barrierSpriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }
        
        // SpriteRenderer 설정 (모든 클라이언트에서)
        if (_barrierSpriteRenderer != null)
        {
            _barrierSpriteRenderer.sortingOrder = 10; // 플레이어 위에 표시
            _barrierSpriteRenderer.sortingLayerName = "Default";
        }
        
        // 베리어 콜라이더 추가 (isTrigger = true)
        _barrierCollider = GetComponent<CircleCollider2D>();
        if (_barrierCollider == null)
        {
            _barrierCollider = gameObject.AddComponent<CircleCollider2D>();
        }
        if (_barrierCollider != null)
        {
            _barrierCollider.isTrigger = true;
            _barrierCollider.enabled = false; // 초기에는 비활성화 (스프라이트가 설정되면 활성화)
        }

        // 렌더러 초기화
        UpdateBarrierSprite();
        
        // 초기 강화 상태 추적
        _lastEnhancementCount = -999; // 첫 프레임 갱신 보장
        _lastIsFinalEnhancement = false;

        // [핵심] 서버라면 즉시 스킬 활성화 로직 실행
        if (_owner.Object.HasStateAuthority)
        {
            Debug.Log($"[DashMagicObject] Initialized and Activated for {_owner.name}");
            ActivateDashSkill();
        }
    }
    
    /// <summary>
    /// 돌진 스킬을 활성화합니다.
    /// </summary>
    private void ActivateDashSkill()
    {
        if (_owner == null || Runner == null) return;
        if (_dashData == null) return;

        _owner.DashSkillTimer = TickTimer.CreateFromSeconds(Runner, _dashData.baseDuration);
        _owner.HasDashSkill = true;
        _owner.DashEnhancementCount = 0;
        _owner.IsDashFinalEnhancement = false;
        _owner.DashIsMoving = false;
        _owner.DashVelocity = Vector2.zero;
        _owner.DashStunTimer = TickTimer.CreateFromSeconds(Runner, _dashData.initialStunDuration);
        
        // 상태 초기화
        _lastEnhancementCount = -999;
        _hasReceivedInput = false;
    }
    #endregion
    
    #region Helper Methods
    /// <summary>
    /// 클라이언트에서 필요한 컴포넌트들을 초기화합니다.
    /// 서버는 Initialize에서 이미 초기화하지만, 클라이언트는 Spawned/Render에서 호출됩니다.
    /// </summary>
    private void InitializeClientComponents()
    {
        // 베리어 스프라이트 렌더러 초기화 (모든 클라이언트에서)
        if (_barrierSpriteRenderer == null)
        {
            _barrierSpriteRenderer = GetComponent<SpriteRenderer>();
            if (_barrierSpriteRenderer == null)
            {
                _barrierSpriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            }
        }
        
        if (_barrierSpriteRenderer != null)
        {
            _barrierSpriteRenderer.sortingOrder = 10;
            _barrierSpriteRenderer.sortingLayerName = "Default";
        }
        
        // 데이터 로드 시도
        if (_dashData == null)
        {
            LoadDashData();
        }
        
        // 초기 강화 상태 추적 초기화
        _lastEnhancementCount = -999;
        _lastIsFinalEnhancement = false;
    }
    
    /// <summary>
    /// DashMagicCombinationData를 로드합니다.
    /// 클라이언트에서 초기화 지연 시 대응하기 위한 메서드
    /// </summary>
    private void LoadDashData()
    {
        if (_gameDataManager == null)
        {
            _gameDataManager = FindObjectOfType<GameDataManager>();
            if (_gameDataManager == null)
            {
                _gameDataManager = GameDataManager.Instance;
            }
        }
        
        if (_gameDataManager != null && _gameDataManager.MagicService != null)
        {
            MagicCombinationData combinationData = _gameDataManager.MagicService.GetCombinationDataByResult(11);
            _dashData = combinationData as DashMagicCombinationData;
        }
    }
    #endregion
    
    #region Skill End Conditions
    /// <summary>
    /// 스킬 종료 조건을 확인합니다.
    /// </summary>
    private void CheckEndConditions()
    {
        if (_owner == null || Runner == null) return;
        if (!_owner.HasDashSkill) return;
        
        // 정지 상태가 끝난 후 일정 시간(예: 0.5초) 동안은 속도 0으로 종료하지 않음
        float stunEndTime = _dashData.initialStunDuration;
        float remainingStunTime = _owner.DashStunTimer.RemainingTime(Runner) ?? 0f;
        bool isInGracePeriod = remainingStunTime <= 0f && remainingStunTime >= -0.5f; // 정지 종료 후 0.5초 유예 기간
        
        // 속도 0 체크 (입력을 받았고, 유예 기간이 지났으며, 정지 상태가 끝났을 때만)
        // 단, 반동 대기 중이면 종료하지 않음
        if (!_owner.DashIsWaitingToRecoil &&
            _owner.DashVelocity.magnitude < 0.1f && 
            _owner.DashStunTimer.ExpiredOrNotRunning(Runner) &&
            !isInGracePeriod &&
            _hasReceivedInput) // 입력을 받았을 때만 속도 0으로 종료
        {
            // 최종 강화 상태에서는 속도 0으로 종료하지 않음
            if (!_owner.IsDashFinalEnhancement)
            {
                EndDashSkill();
                return;
            }
        }
        
        // 시간 경과 체크
        if (_owner.DashSkillTimer.Expired(Runner))
        {
            EndDashSkill();
            return;
        }
    }
    
    /// <summary>
    /// 돌진 스킬을 종료합니다.
    /// </summary>
    private void EndDashSkill()
    {
        if (_owner == null) return;
        
        // 스킬 상태 초기화 (State Authority에서만)
        if (_owner.Object != null && _owner.Object.HasStateAuthority)
        {
            _owner.HasDashSkill = false;
            _owner.DashSkillTimer = TickTimer.None;
            _owner.DashEnhancementCount = 0;
            _owner.IsDashFinalEnhancement = false;
            _owner.DashIsMoving = false;
            _owner.DashVelocity = Vector2.zero;
            _owner.DashLastInputDirection = Vector2.zero;
            _owner.DashPendingRecoilDirection = Vector2.zero;
            _owner.DashIsWaitingToRecoil = false;
            
            // 속도 초기화 (Rigidbody 속도도 0으로)
            UpdateRigidbodyVelocity(Vector2.zero);
            
            // [중요] PlayerController의 참조 제거 (되돌아가지 못하는 문제 해결)
            _owner.DashMagicObject = null;
        }
        
        // 오브젝트 제거
        if (gameObject != null)
        {
            Destroy(gameObject);
        }
    }
    #endregion
    
    #region Helper Methods
    /// <summary>
    /// 모든 플레이어를 가져옵니다.
    /// </summary>
    private List<PlayerController> GetAllPlayers()
    {
        List<PlayerController> allPlayers = new List<PlayerController>();
        
        if (MainGameManager.Instance != null)
        {
            allPlayers = MainGameManager.Instance.GetAllPlayers();
        }
        
        if (allPlayers == null || allPlayers.Count == 0)
        {
            allPlayers = new List<PlayerController>(FindObjectsOfType<PlayerController>());
        }
        
        return allPlayers;
    }
    
    /// <summary>
    /// 충돌 쿨다운을 정리합니다.
    /// [개선] NetworkId 기반으로 변경
    /// </summary>
    private void CleanupCollisionCooldowns()
    {
        if (_owner == null || Runner == null) return;
        
        var keysToRemove = new List<NetworkId>();
        
        foreach (var kvp in _enemyCollisionCooldowns)
        {
            if (kvp.Value.ExpiredOrNotRunning(Runner))
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            _enemyCollisionCooldowns.Remove(key);
        }
    }
    
    /// <summary>
    /// 최근 충돌 플레이어 목록에서 제거합니다.
    /// </summary>
    private IEnumerator RemoveFromRecentHitPlayers(PlayerController player, float delay)
    {
        yield return new WaitForSeconds(delay);
        _recentHitPlayers.Remove(player);
    }
    #endregion
    
    #region Cleanup
    void OnDestroy()
    {
        // 스킬 상태 정리 (안전한 null 체크)
        if (_owner != null && _owner.Object != null && _owner.Object.HasStateAuthority)
        {
            _owner.HasDashSkill = false;
            _owner.DashSkillTimer = TickTimer.None;
            _owner.DashIsMoving = false;
            _owner.DashVelocity = Vector2.zero;
        }
    }
    #endregion
}