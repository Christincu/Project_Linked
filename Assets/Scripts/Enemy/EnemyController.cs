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
            // 1. 플레이어 탐지
            if (_detector.DetectPlayer(out PlayerController detectedPlayer))
            {
                // 플레이어 탐지됨 - 플레이어 위치로 이동
                Vector2 playerPos = (Vector2)detectedPlayer.transform.position;
                _movement.MoveTo(playerPos);
                IsInvestigating = false;
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
