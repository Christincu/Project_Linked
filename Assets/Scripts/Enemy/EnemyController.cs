using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

/// <summary>
/// 네트워크 동기화를 지원하는 적 컨트롤러 (Photon Fusion)
/// NetworkRigidbody를 사용하여 위치 및 속도를 동기화합니다.
/// </summary>
public class EnemyController : NetworkBehaviour
{
    #region Constants
    private const float MIN_MOVEMENT_SPEED = 0.1f; // Wall collision detection threshold
    #endregion

    #region Networked Properties
    // Animation
    [Networked] public NetworkString<_16> AnimationState { get; set; }
    [Networked] public float ScaleX { get; set; }

    // Enemy Info
    [Networked] public int EnemyIndex { get; set; }

    // Health
    [Networked] public float CurrentHealth { get; set; }
    [Networked] public float MaxHealth { get; set; }
    [Networked] public NetworkBool IsDead { get; set; }

    // AI State
    [Networked] public Vector2 HomePosition { get; set; }
    [Networked] public Vector2 InvestigatePosition { get; set; }
    [Networked] public TickTimer InvestigationTimer { get; set; }
    [Networked] public NetworkBool IsInvestigating { get; set; }
    #endregion

    #region Private Fields - Components
    private GameDataManager _gameDataManager;
    private GameObject _viewObj;
    private Animator _animator;
    private ChangeDetector _changeDetector;

    // Enemy Components
    private EnemyState _state;
    private EnemyDetector _detector;
    private EnemyMovement _movement;
    #endregion

    #region Private Fields - State
    private Vector2 _previousPosition;
    private string _lastAnimationState = "";
    private bool _isTestMode;
    #endregion

    #region Properties
    public GameDataManager GameDataManager => _gameDataManager;
    public GameObject ViewObj => _viewObj;
    public EnemyState State => _state;
    public EnemyDetector Detector => _detector;
    public EnemyMovement Movement => _movement;
    public float MoveSpeed => _movement != null ? _movement.GetMoveSpeed() : 0f;

    // Health Properties
    public float HealthPercentage => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;
    #endregion

    #region Fusion Callbacks
    /// <summary>
    /// 네트워크 오브젝트 생성 시 호출됩니다.
    /// </summary>
    public override void Spawned()
    {
        _isTestMode = MainGameManager.Instance != null && MainGameManager.Instance.IsTestMode;
        _gameDataManager = GameDataManager.Instance;
        _previousPosition = transform.position;

        InitializeComponents();
        InitializeNetworkState();

        // ViewObjParent 생성 및 Interpolation Target 설정
        EnsureViewObjParentExists();

        // 네트워크 동기화 컴포넌트 자동 보강 (없으면 추가)
        EnsureNetworkSyncComponentExists();

        TryCreateView();

        // 초기화 및 데이터 동기화 대기
        StartCoroutine(InitializeAllComponents());
    }

    /// <summary>
    /// 모든 컴포넌트를 초기화하고 데이터 동기화를 처리합니다.
    /// </summary>
    private IEnumerator InitializeAllComponents()
    {
        yield return null;

        // EnemyData 가져오기
        EnemyData enemyData = null;
        if (GameDataManager.Instance != null)
        {
            enemyData = GameDataManager.Instance.EnemyService.GetEnemy(EnemyIndex);
        }

        // 1. 네트워크 변수 초기화 (서버만)
        if (Object.HasStateAuthority)
        {
            if (enemyData != null)
            {
                MaxHealth = enemyData.maxHealth;
                CurrentHealth = enemyData.startingHealth;
            }
            else
            {
                // 기본값 사용 (EnemyData가 없는 경우)
                MaxHealth = 10f;
                CurrentHealth = 10f;
            }
            IsDead = false;
            HomePosition = transform.position;
        }

        // 2. 종속 컴포넌트 초기화
        _state?.Initialize(this, enemyData);
        _detector?.Initialize(this, enemyData);
        _movement?.Initialize(this, enemyData);
        
        // 3. Rigidbody2D 물리 설정 (플레이어가 밀어도 관성이 빠르게 감소하도록)
        ConfigureRigidbodyPhysics();
        
        // 4. 적 감시 범위 트리거 콜리더 초기화 (플레이어의 트리거와 충돌하기 위해)
        EnsureDetectionTriggerColliderExists();
    }

    /// <summary>
    /// Fusion 네트워크 업데이트 (매 틱마다 호출)
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (_detector == null || _movement == null || _state == null) return;

        // 사망하지 않은 경우에만 AI 동작
        if (!_state.IsDead && Object.HasStateAuthority)
        {
            // 1. 플레이어 탐지 (위협점수 기반 우선순위)
            if (_detector.DetectPlayer(out PlayerController detectedPlayer))
            {
                // 현재 추적 중인 플레이어와 비교하여 위협점수가 더 높은 플레이어가 있으면 타겟 변경
                PlayerController currentTarget = _detector.DetectedPlayer;
                if (currentTarget != null && detectedPlayer != null)
                {
                    // 위협점수가 더 높은 플레이어를 우선 선택
                    if (detectedPlayer.ThreatScore > currentTarget.ThreatScore)
                    {
                        // 더 위협적인 플레이어 발견 - 타겟 변경
                        Vector2 playerPos = (Vector2)detectedPlayer.transform.position;
                        _movement.MoveTo(playerPos);
                        IsInvestigating = false;
                    }
                    else
                    {
                        // 기존 타겟 유지
                        Vector2 playerPos = (Vector2)currentTarget.transform.position;
                        _movement.MoveTo(playerPos);
                        IsInvestigating = false;
                    }
                }
                else
                {
                    // 플레이어 탐지됨 - 플레이어 위치로 이동
                    Vector2 playerPos = (Vector2)detectedPlayer.transform.position;
                    _movement.MoveTo(playerPos);
                    IsInvestigating = false;
                }
            }
            else
            {
                // 플레이어를 탐지하지 못함
                PlayerController previousPlayer = _detector.DetectedPlayer;
                
                if (previousPlayer != null)
                {
                    // 이전에 탐지했던 경우 - 조사 시작
                    if (!IsInvestigating)
                    {
                        IsInvestigating = true;
                        InvestigationTimer = TickTimer.CreateFromSeconds(Runner, 3f);
                        _movement.MoveTo(_detector.LastDetectedPosition);
                    }
                    else
                    {
                        // 조사 시간 종료 - 집으로 복귀
                        if (InvestigationTimer.Expired(Runner))
                        {
                            IsInvestigating = false;
                            _detector.OnPlayerLost();
                            _movement.MoveTo(HomePosition);
                        }
                    }
                }
                else if (IsInvestigating)
                {
                    // 조사 완료 - 집으로 복귀
                    IsInvestigating = false;
                    _movement.MoveTo(HomePosition);
                }
                else
                {
                    // 집에 있음 - 가끔 대기
                    if (!_movement.IsMoving)
                    {
                        _movement.MoveTo(HomePosition);
                    }
                }
            }

            // 2. 이동 적용
            _movement.UpdateMovement();

            // 3. 애니메이션 업데이트
            UpdateAnimation();
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

    #region Public Methods - Enemy & State Callbacks
    /// <summary>
    /// 적 인덱스를 설정하고 뷰 오브젝트를 생성합니다.
    /// </summary>
    public void SetEnemyIndex(int enemyIndex)
    {
        EnemyIndex = enemyIndex;
        TryCreateView();
    }
    #endregion

    #region Private Methods - Initialization
    /// <summary>
    /// 컴포넌트를 초기화합니다 (없으면 추가).
    /// </summary>
    private void InitializeComponents()
    {
        _state = GetComponent<EnemyState>() ?? gameObject.AddComponent<EnemyState>();
        _detector = GetComponent<EnemyDetector>() ?? gameObject.AddComponent<EnemyDetector>();
        _movement = GetComponent<EnemyMovement>() ?? gameObject.AddComponent<EnemyMovement>();

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
            IsInvestigating = false;
        }
    }

    /// <summary>
    /// ViewObjParent가 존재하는지 확인하고 없으면 생성합니다.
    /// NetworkRigidbody2D의 Interpolation Target으로 설정합니다.
    /// </summary>
    private void EnsureViewObjParentExists()
    {
        // ViewObjParent가 이미 존재하는지 확인
        Transform viewObjParent = transform.Find("ViewObjParent");
        
        if (viewObjParent == null)
        {
            // ViewObjParent 생성
            GameObject viewObjParentObj = new GameObject("ViewObjParent");
            viewObjParentObj.transform.SetParent(transform, false);
            viewObjParentObj.transform.localPosition = Vector3.zero;
            viewObjParentObj.transform.localRotation = Quaternion.identity;
            viewObjParentObj.transform.localScale = Vector3.one;
            
            viewObjParent = viewObjParentObj.transform;
            
            Debug.Log($"[EnemyController] ViewObjParent created");
        }
        
        // NetworkRigidbody2D의 Interpolation Target 설정
        var networkRb = GetComponent<NetworkRigidbody2D>();
        if (networkRb != null)
        {
            if (networkRb.InterpolationTarget == null || networkRb.InterpolationTarget != viewObjParent)
            {
                networkRb.InterpolationTarget = viewObjParent;
                Debug.Log($"[EnemyController] Interpolation Target set to ViewObjParent");
            }
        }
        else
        {
            Debug.LogWarning($"[EnemyController] NetworkRigidbody2D not found!");
        }
    }

    /// <summary>
    /// NetworkRigidbody2D 또는 NetworkTransform 중 하나가 존재하도록 보장합니다.
    /// 없으면 NetworkTransform을 추가해 기본 위치/회전 동기화가 이루어지도록 합니다.
    /// </summary>
    private void EnsureNetworkSyncComponentExists()
    {
        // 우선 NetworkRigidbody2D가 있다면 그대로 사용
        var networkRb2D = GetComponent<NetworkRigidbody2D>();
        if (networkRb2D != null)
        {
            return;
        }

        // 없다면 NetworkTransform 존재 여부 확인 후 없으면 추가
        var networkTransform = GetComponent<NetworkTransform>();
        if (networkTransform == null)
        {
            networkTransform = gameObject.AddComponent<NetworkTransform>();
            Debug.Log("[EnemyController] NetworkTransform added for transform sync");
        }
    }
    
    /// <summary>
    /// Rigidbody2D 물리 설정을 구성합니다.
    /// 플레이어가 적을 밀어도 관성이 빠르게 감소하여 NavMeshAgent 이동이 방해받지 않도록 합니다.
    /// </summary>
    private void ConfigureRigidbodyPhysics()
    {
        var networkRb = GetComponent<NetworkRigidbody2D>();
        if (networkRb != null && networkRb.Rigidbody != null)
        {
            Rigidbody2D rb = networkRb.Rigidbody;
            // drag를 높여서 외부 힘(플레이어가 밀어낸 힘)에 의한 관성을 빠르게 감소시킴
            // 높은 값일수록 관성이 빠르게 감소 (기본값 0, 권장값 5~10)
            rb.drag = 8f;
        }
    }
    
    /// <summary>
    /// 적 감시 범위 트리거 콜리더를 초기화합니다 (플레이어의 트리거와 충돌하기 위해).
    /// </summary>
    private void EnsureDetectionTriggerColliderExists()
    {
        // 이미 Collider2D가 있는지 확인 (모든 종류)
        Collider2D existingCollider = GetComponent<Collider2D>();
        
        if (existingCollider == null)
        {
            // Collider2D가 없으면 트리거 콜리더 추가
            CircleCollider2D triggerCollider = gameObject.AddComponent<CircleCollider2D>();
            triggerCollider.isTrigger = true;
            triggerCollider.radius = 0.5f; // 기본 크기 (실제 크기는 중요하지 않음, 플레이어가 감지하는 용도)
            Debug.Log("[EnemyController] Detection trigger collider added");
        }
        // Collider2D가 이미 있으면 그대로 사용 (트리거든 아니든 상관없이 플레이어의 트리거와 충돌 가능)
        
        // EnemyDetector가 있는지 확인 (플레이어의 트리거가 감지할 수 있도록)
        // EnemyDetector는 자동으로 추가되므로 별도 체크 불필요
        
        // 충돌 감지를 위한 콜리더 확인 및 설정
        EnsureCollisionColliderExists();
    }
    
    /// <summary>
    /// 플레이어와 충돌 감지를 위한 콜리더를 확인하고 설정합니다.
    /// </summary>
    private void EnsureCollisionColliderExists()
    {
        // 모든 Collider2D 확인
        Collider2D[] colliders = GetComponents<Collider2D>();
        bool hasNonTriggerCollider = false;
        
        foreach (var col in colliders)
        {
            if (!col.isTrigger)
            {
                hasNonTriggerCollider = true;
                break;
            }
        }
        
        // 트리거가 아닌 콜리더가 없으면 추가 (충돌 감지용)
        if (!hasNonTriggerCollider)
        {
            CircleCollider2D collisionCollider = gameObject.AddComponent<CircleCollider2D>();
            collisionCollider.isTrigger = false; // 물리 충돌용
            collisionCollider.radius = 0.5f;
            Debug.Log("[EnemyController] Collision collider added for player damage");
        }
    }
    
    /// <summary>
    /// 플레이어와 충돌 시 데미지를 줍니다.
    /// PlayerDetectionTrigger는 제외합니다.
    /// </summary>
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!Object.HasStateAuthority) return;
        if (_state == null || _state.IsDead) return;
        
        // PlayerDetectionTrigger는 무시 (플레이어 감지용 트리거)
        if (collision.gameObject.GetComponent<PlayerDetectionTrigger>() != null) return;
        
        // PlayerController 찾기
        PlayerController player = collision.gameObject.GetComponent<PlayerController>();
        if (player == null)
        {
            player = collision.gameObject.GetComponentInParent<PlayerController>();
        }
        if (player == null && collision.rigidbody != null)
        {
            player = collision.rigidbody.GetComponent<PlayerController>();
        }
        if (player == null && collision.transform.root != null)
        {
            player = collision.transform.root.GetComponent<PlayerController>();
        }
        
        // 플레이어와 충돌 시 데미지 적용
        if (player != null && !player.IsDead && player.State != null)
        {
            player.State.TakeDamage(1f);
            Debug.Log($"[EnemyController] {name} hit {player.name}, dealt 1 damage");
        }
    }
    
    /// <summary>
    /// 트리거 충돌 시에도 데미지를 줍니다 (트리거 콜리더가 있는 경우).
    /// PlayerDetectionTrigger는 제외합니다.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!Object.HasStateAuthority) return;
        if (_state == null || _state.IsDead) return;
        
        // PlayerDetectionTrigger는 무시 (플레이어 감지용 트리거)
        if (other.GetComponent<PlayerDetectionTrigger>() != null) return;
        
        // PlayerController 찾기
        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
        {
            player = other.GetComponentInParent<PlayerController>();
        }
        if (player == null && other.attachedRigidbody != null)
        {
            player = other.attachedRigidbody.GetComponent<PlayerController>();
        }
        if (player == null && other.transform.root != null)
        {
            player = other.transform.root.GetComponent<PlayerController>();
        }
        
        // 플레이어와 충돌 시 데미지 적용
        if (player != null && !player.IsDead && player.State != null)
        {
            player.State.TakeDamage(1f);
            Debug.Log($"[EnemyController] {name} hit {player.name} (trigger), dealt 1 damage");
        }
    }

    /// <summary>
    /// 적 뷰 오브젝트를 생성합니다. (EnemyIndex 동기화 후 호출)
    /// </summary>
    private void TryCreateView()
    {
        if (_viewObj != null || GameDataManager.Instance == null) return;

        var data = GameDataManager.Instance.EnemyService.GetEnemy(EnemyIndex);

        if (data != null && data.viewObj != null)
        {
            if (_viewObj != null) Destroy(_viewObj);

            GameObject instance = new GameObject("ViewObj");
            
            // ViewObjParent를 찾아서 그 자식으로 설정
            Transform viewObjParent = transform.Find("ViewObjParent");
            if (viewObjParent != null)
            {
                instance.transform.SetParent(viewObjParent, false);
                Debug.Log($"[EnemyController] ViewObj created under ViewObjParent");
            }
            else
            {
                // ViewObjParent가 없으면 루트에 생성 (fallback)
                Debug.LogWarning($"[EnemyController] ViewObjParent not found! Creating ViewObj at root");
                instance.transform.SetParent(transform, false);
            }

            _viewObj = Instantiate(data.viewObj, instance.transform);
            _animator = _viewObj.GetComponent<Animator>();
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

                case nameof(EnemyIndex):
                    TryCreateView();
                    break;
            }
        }
    }
    #endregion
}
