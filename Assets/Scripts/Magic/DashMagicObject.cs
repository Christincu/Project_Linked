using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

/// <summary>
/// 화염 돌진 마법 오브젝트
/// 두 플레이어의 마법 합체 발동 시 각 플레이어에게 부착되는 오브젝트
/// 돌진 스킬의 모든 로직을 처리합니다.
/// </summary>
public class DashMagicObject : MonoBehaviour
{
    #region Serialized Fields
    [Header("Collision Settings")]
    [Tooltip("공격 판정용 Circle Collider 반지름")]
    [SerializeField] private float attackColliderRadius = 1f;
    
    [Tooltip("정면 판정 범위 (원뿔 각도)")]
    [SerializeField] private float frontAngleRange = 120f;
    #endregion
    
    #region Private Fields
    private PlayerController _owner;
    private DashMagicCombinationData _dashData;
    private GameDataManager _gameDataManager;
    private CircleCollider2D _attackCollider;
    private GameObject _frontDetectionObj; // 정면 판정 오브젝트
    private CircleCollider2D _frontDetectionCollider; // 정면 판정 콜리더
    
    // 이동 관련
    private Vector2 _currentVelocity = Vector2.zero;
    private Vector2 _lastInputDirection = Vector2.zero;
    private bool _hasReceivedInput = false; // 입력을 받았는지 여부
    
    // 충돌 관련
    private HashSet<EnemyController> _recentHitEnemies = new HashSet<EnemyController>(); // 최근 충돌한 적 (재충돌 방지)
    private Dictionary<EnemyController, TickTimer> _enemyCollisionCooldowns = new Dictionary<EnemyController, TickTimer>();
    private HashSet<PlayerController> _recentHitPlayers = new HashSet<PlayerController>(); // 최근 충돌한 플레이어
    
    // 카메라 관련
    private MainCameraController _cameraController;
    private Vector3 _originalCameraPosition;
    private float _originalCameraSize;
    private bool _cameraLocked = false;
    
    // 베리어 시각화 관련
    private SpriteRenderer _barrierSpriteRenderer;
    #endregion
    
    #region Unity Callbacks
    void Awake()
    {
        // 공격 판정용 Circle Collider 추가
        _attackCollider = gameObject.AddComponent<CircleCollider2D>();
        _attackCollider.radius = attackColliderRadius;
        _attackCollider.isTrigger = true;
        _attackCollider.enabled = false; // 초기에는 비활성화
        
        // 정면 판정 오브젝트 생성
        _frontDetectionObj = new GameObject("FrontDetection");
        _frontDetectionObj.transform.SetParent(transform);
        _frontDetectionObj.transform.localPosition = Vector3.zero;
        _frontDetectionCollider = _frontDetectionObj.AddComponent<CircleCollider2D>();
        _frontDetectionCollider.radius = 0.5f;
        _frontDetectionCollider.isTrigger = true;
        _frontDetectionCollider.enabled = false; // 초기에는 비활성화
    }
    
    void FixedUpdate()
    {
        if (_owner == null || _owner.Runner == null) return;
        if (!_owner.Object.HasStateAuthority) return;
        
        // 스킬 상태 확인
        if (!_owner.HasDashSkill || _owner.DashSkillTimer.ExpiredOrNotRunning(_owner.Runner))
        {
            // 스킬 종료
            EndDashSkill();
            return;
        }
        
        // 정지/행동불능 타이머 확인
        if (_owner.DashStunTimer.IsRunning && !_owner.DashStunTimer.Expired(_owner.Runner))
        {
            // 정지 상태 - 이동 및 입력 처리 안 함
            _owner.DashVelocity = Vector2.zero;
            _owner.DashIsMoving = false;
            UpdateRigidbodyVelocity(Vector2.zero);
            return;
        }
        
        // 정지 상태가 끝났으면 공격 콜라이더 활성화
        if (!_attackCollider.enabled && (_owner.DashStunTimer.ExpiredOrNotRunning(_owner.Runner)))
        {
            _attackCollider.enabled = true;
        }
        
        // 입력 처리 및 이동 업데이트
        ProcessDashMovement();
        
        // 스킬 종료 조건 확인
        CheckEndConditions();
        
        // 충돌 타임아웃 정리
        CleanupCollisionCooldowns();
    }
    
    /// <summary>
    /// PlayerController의 FixedUpdateNetwork에서 호출되는 업데이트 메서드
    /// 네트워크 틱과 동기화되어 실행됩니다.
    /// </summary>
    public void FixedUpdateNetwork()
    {
        if (_owner == null || _owner.Runner == null) return;
        if (!_owner.Object.HasStateAuthority) return;
        
        // 스킬 상태 확인
        if (!_owner.HasDashSkill || _owner.DashSkillTimer.ExpiredOrNotRunning(_owner.Runner))
        {
            // 스킬 종료
            EndDashSkill();
            return;
        }
        
        // 정지/행동불능 타이머 확인
        if (_owner.DashStunTimer.IsRunning && !_owner.DashStunTimer.Expired(_owner.Runner))
        {
            // 정지 상태 - 이동 및 입력 처리 안 함
            _owner.DashVelocity = Vector2.zero;
            _owner.DashIsMoving = false;
            UpdateRigidbodyVelocity(Vector2.zero);
            return;
        }
        
        // 정지 상태가 끝났으면 공격 콜라이더 활성화
        if (!_attackCollider.enabled && (_owner.DashStunTimer.ExpiredOrNotRunning(_owner.Runner)))
        {
            _attackCollider.enabled = true;
        }
        
        // 입력 처리 및 이동 업데이트
        ProcessDashMovement();
        
        // 스킬 종료 조건 확인
        CheckEndConditions();
        
        // 충돌 타임아웃 정리
        CleanupCollisionCooldowns();
    }
    
    void OnTriggerEnter2D(Collider2D other)
    {
        if (_owner == null || _owner.Runner == null)
        {
            return;
        }
        if (!_owner.Object.HasStateAuthority)
        {
            return;
        }
        if (!_owner.HasDashSkill)
        {
            return;
        }
        
        // 적 충돌 처리
        EnemyController enemy = other.GetComponent<EnemyController>();
        if (enemy == null)
        {
            enemy = other.GetComponentInParent<EnemyController>();
        }
        if (enemy == null && other.attachedRigidbody != null)
        {
            enemy = other.attachedRigidbody.GetComponent<EnemyController>();
        }
        
        if (enemy != null && !enemy.IsDead)
        {
            Debug.Log($"[DashMagicObject] {_owner.name} - Enemy collision detected: {enemy.name}");
            HandleEnemyCollision(enemy, other);
            return;
        }
        
        // 플레이어 충돌 처리
        PlayerController otherPlayer = other.GetComponent<PlayerController>();
        if (otherPlayer == null)
        {
            otherPlayer = other.GetComponentInParent<PlayerController>();
        }
        if (otherPlayer == null && other.attachedRigidbody != null)
        {
            otherPlayer = other.attachedRigidbody.GetComponent<PlayerController>();
        }
        
        if (otherPlayer == null)
        {
            Debug.Log($"[DashMagicObject] {_owner.name} - No PlayerController found in {other.name}");
            return;
        }
        
        if (otherPlayer == _owner)
        {
            Debug.Log($"[DashMagicObject] {_owner.name} - Collision with self, ignoring");
            return;
        }
        
        if (!otherPlayer.HasDashSkill)
        {
            Debug.Log($"[DashMagicObject] {_owner.name} - Other player {otherPlayer.name} doesn't have DashSkill");
            return;
        }
        
        if (otherPlayer.IsDead)
        {
            Debug.Log($"[DashMagicObject] {_owner.name} - Other player {otherPlayer.name} is dead");
            return;
        }
        
        Debug.Log($"[DashMagicObject] {_owner.name} - Player collision detected: {otherPlayer.name}, calling HandlePlayerCollision");
        HandlePlayerCollision(otherPlayer, other);
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
        _gameDataManager = FindObjectOfType<GameDataManager>();
        
        if (_owner == null || _dashData == null)
        {
            return;
        }
        
        // 플레이어에게 부착
        transform.SetParent(_owner.transform);
        transform.localPosition = Vector3.zero;
        
        // 공격 콜라이더 반지름 설정
        if (_attackCollider != null)
        {
            _attackCollider.radius = attackColliderRadius;
        }
        
        // 베리어 스프라이트 렌더러 가져오기
        _barrierSpriteRenderer = GetComponent<SpriteRenderer>();
        if (_barrierSpriteRenderer != null && _dashData != null)
        {
            // 기본 스프라이트 설정
            UpdateBarrierSprite();
        }
        
        // 스킬 발동 (State Authority에서만)
        if (_owner.Object.HasStateAuthority)
        {
            ActivateDashSkill();
        }
    }
    
    /// <summary>
    /// 돌진 스킬을 활성화합니다.
    /// </summary>
    private void ActivateDashSkill()
    {
        if (_owner == null || _owner.Runner == null) return;
        
        // 기본 지속 시간 설정
        float duration = _dashData.baseDuration;
        _owner.DashSkillTimer = TickTimer.CreateFromSeconds(_owner.Runner, duration);
        _owner.HasDashSkill = true;
        _owner.DashEnhancementCount = 0;
        _owner.IsDashFinalEnhancement = false;
        _owner.DashIsMoving = false;
        _owner.DashVelocity = Vector2.zero;
        _owner.DashLastInputDirection = Vector2.zero;
        
        // 초기 정지 및 조작 불능 상태 (0.5초)
        _owner.DashStunTimer = TickTimer.CreateFromSeconds(_owner.Runner, _dashData.initialStunDuration);
        
        // 입력 상태 초기화
        _hasReceivedInput = false;
        
        // 플레이어 위치로 이동
        transform.position = _owner.transform.position;
        
        // 카메라 설정 (로컬 플레이어만)
        if (_owner.Object.HasInputAuthority)
        {
            SetupCamera();
        }
        
    }
    #endregion
    
    #region Movement Logic
    /// <summary>
    /// 돌진 이동을 처리합니다 (관성 기반).
    /// </summary>
    private void ProcessDashMovement()
    {
        if (_owner == null || _owner.Runner == null) return;
        if (_dashData == null) return;
        
        // 입력 가져오기
        Vector2 inputDirection = GetInputDirection();
        
        // 틱 시간 가져오기
        float deltaTime = _owner.Runner.DeltaTime;
        
        // 최종 강화 상태인지 확인
        bool isFinalEnhancement = _owner.IsDashFinalEnhancement;
        
        if (isFinalEnhancement)
        {
            // 최종 강화: 관성 제거, 기존 이동 로직 사용
            ProcessFinalEnhancementMovement(inputDirection, deltaTime);
        }
        else
        {
            // 일반 상태: 관성 기반 이동
            ProcessInertiaBasedMovement(inputDirection, deltaTime);
        }
        
        // 속도 제한 (최대 속도에 도달해도 가속은 계속 적용, 단지 제한만 됨)
        float maxSpeed = _dashData.maxSpeed;
        if (_owner.DashVelocity.magnitude > maxSpeed)
        {
            _owner.DashVelocity = _owner.DashVelocity.normalized * maxSpeed;
        }
        
        // Rigidbody에 속도 적용
        UpdateRigidbodyVelocity(_owner.DashVelocity);
        
        // 정지 상태 복귀 체크
        if (_owner.DashVelocity.magnitude < 0.1f)
        {
            _owner.DashIsMoving = false;
            _owner.DashVelocity = Vector2.zero;
            UpdateRigidbodyVelocity(Vector2.zero);
        }
        else
        {
            _owner.DashIsMoving = true;
        }
        
        // 마지막 입력 방향 저장
        if (inputDirection.magnitude > 0)
        {
            _owner.DashLastInputDirection = inputDirection;
        }
    }
    
    /// <summary>
    /// 관성 기반 이동 처리
    /// </summary>
    private void ProcessInertiaBasedMovement(Vector2 inputDirection, float deltaTime)
    {
        Vector2 velocity = _owner.DashVelocity;
        float maxSpeed = _dashData.maxSpeed;
        float acceleration = _dashData.movementAcceleration;
        float deceleration = _dashData.deceleration;
        
        // 입력이 있는 경우
        if (inputDirection.magnitude > 0)
        {
            Vector2 inputNormalized = inputDirection.normalized;
            
            // 입력 방향으로 가속 적용
            velocity += inputNormalized * acceleration * deltaTime;
            
            // 최대 속도 제한
            if (velocity.magnitude > maxSpeed)
            {
                velocity = velocity.normalized * maxSpeed;
            }
            
            _owner.DashIsMoving = true;
            _owner.DashLastInputDirection = inputNormalized;
        }
        else
        {
            // 입력 없음: 자연 감속
            if (velocity.magnitude > 0.01f)
            {
                velocity -= velocity.normalized * deceleration * deltaTime;
                
                // 속도가 너무 작아지면 0으로 설정
                if (velocity.magnitude <= 0.01f)
                {
                    velocity = Vector2.zero;
                    _owner.DashIsMoving = false;
                }
                else
                {
                    _owner.DashIsMoving = true;
                }
            }
            else
            {
                velocity = Vector2.zero;
                _owner.DashIsMoving = false;
            }
        }
        
        _owner.DashVelocity = velocity;
    }
    
    /// <summary>
    /// 최종 강화 상태 이동 처리 (관성 제거, 기존 이동 로직 사용)
    /// </summary>
    private void ProcessFinalEnhancementMovement(Vector2 inputDirection, float deltaTime)
    {
        if (inputDirection.magnitude > 0)
        {
            // 기존 이동 속도에 배율 적용
            float baseMoveSpeed = _owner.MoveSpeed;
            float enhancedMoveSpeed = baseMoveSpeed * _dashData.finalEnhancementSpeedMultiplier;
            
            Vector2 targetVelocity = inputDirection.normalized * enhancedMoveSpeed;
            _owner.DashVelocity = targetVelocity;
        }
        else
        {
            // 입력 없으면 즉시 정지
            _owner.DashVelocity = Vector2.zero;
        }
    }
    
    /// <summary>
    /// 입력 방향을 가져옵니다.
    /// State Authority에서 Input Authority 플레이어의 입력을 가져옵니다.
    /// 테스트 모드에서는 ControlledSlot 체크를 수행합니다.
    /// </summary>
    private Vector2 GetInputDirection()
    {
        if (_owner == null || _owner.Runner == null) return Vector2.zero;
        
        Vector2 inputDir = Vector2.zero;
        
        // GetInput은 State Authority에서 실행될 때 자동으로 Input Authority 플레이어의 입력을 가져옵니다
        if (_owner.GetInput<InputData>(out var inputData))
        {
            // 테스트 모드 슬롯 확인 (일반 이동 로직과 동일)
            bool isTestMode = MainGameManager.Instance != null && MainGameManager.Instance.IsTestMode;
            if (isTestMode && inputData.ControlledSlot != _owner.PlayerSlot)
            {
                // 테스트 모드에서 이 플레이어가 선택되지 않았으면 입력 무시
                return Vector2.zero;
            }
            
            int x = 0;
            int y = 0;
            
            if (inputData.GetButton(InputButton.LEFT)) x -= 1;
            if (inputData.GetButton(InputButton.RIGHT)) x += 1;
            if (inputData.GetButton(InputButton.DOWN)) y -= 1;
            if (inputData.GetButton(InputButton.UP)) y += 1;
            
            inputDir = new Vector2(x, y).normalized;
            
            // 입력이 있으면 플래그 설정
            if (inputDir.magnitude > 0.1f)
            {
                _hasReceivedInput = true;
            }
        }
        
        return inputDir;
    }
    
    /// <summary>
    /// Rigidbody 속도를 업데이트합니다.
    /// </summary>
    private void UpdateRigidbodyVelocity(Vector2 velocity)
    {
        if (_owner == null || _owner.Movement == null) return;
        
        var rigidbody = _owner.Movement.Rigidbody;
        if (rigidbody != null)
        {
            rigidbody.velocity = velocity;
        }
        
        _owner.DashVelocity = velocity;
    }
    #endregion
    
    #region Collision Handling
    /// <summary>
    /// 적 충돌을 처리합니다.
    /// </summary>
    private void HandleEnemyCollision(EnemyController enemy, Collider2D collisionCollider)
    {
        if (enemy == null || enemy.IsDead) return;
        if (_owner == null || !_owner.HasDashSkill) return;
        if (!_owner.DashIsMoving) return; // 정지 상태에서는 충돌 처리 안 함
        
        // 최종 강화 상태에서는 충돌 판정 제거
        if (_owner.IsDashFinalEnhancement) return;
        
        // 재충돌 방지 체크
        if (_enemyCollisionCooldowns.ContainsKey(enemy))
        {
            TickTimer cooldown = _enemyCollisionCooldowns[enemy];
            if (cooldown.IsRunning && !cooldown.Expired(_owner.Runner))
            {
                // 쿨다운 중: 플레이어가 피해를 입음
                if (_owner.State != null)
                {
                    _owner.State.TakeDamage(_dashData.baseDamage);
                }
                return;
            }
        }
        
        // 데미지 계산
        float damage = CalculateDamage();
        
        // 적에게 데미지 적용
        if (enemy.State != null)
        {
            enemy.State.TakeDamage(damage);
        }
        
        // 적 넉백
        ApplyEnemyKnockback(enemy);
        
        // 지속 시간 감소
        float remainingTime = _owner.DashSkillTimer.RemainingTime(_owner.Runner) ?? 0f;
        float newTime = Mathf.Max(0f, remainingTime - _dashData.durationReductionOnHit);
        _owner.DashSkillTimer = TickTimer.CreateFromSeconds(_owner.Runner, newTime);
        
        // 재충돌 방지 타이머 설정
        _enemyCollisionCooldowns[enemy] = TickTimer.CreateFromSeconds(_owner.Runner, _dashData.enemyCollisionCooldown);
        
    }
    
    /// <summary>
    /// 적에게 넉백을 적용합니다.
    /// </summary>
    private void ApplyEnemyKnockback(EnemyController enemy)
    {
        if (enemy == null || _owner == null) return;
        
        Vector2 knockbackDirection = _owner.DashVelocity.normalized;
        if (knockbackDirection.magnitude < 0.1f)
        {
            knockbackDirection = _owner.DashLastInputDirection;
            if (knockbackDirection.magnitude < 0.1f)
            {
                knockbackDirection = Vector2.up; // 기본값
            }
        }
        
        // ±45도 중 무작위 각도로 넉백
        float baseAngle = Mathf.Atan2(knockbackDirection.y, knockbackDirection.x) * Mathf.Rad2Deg;
        float randomAngle = baseAngle + Random.Range(-_dashData.enemyKnockbackAngleRange, _dashData.enemyKnockbackAngleRange);
        float knockbackAngleRad = randomAngle * Mathf.Deg2Rad;
        Vector2 finalKnockbackDirection = new Vector2(Mathf.Cos(knockbackAngleRad), Mathf.Sin(knockbackAngleRad));
        
        // 적의 Rigidbody에 넉백 적용
        if (enemy.Movement != null)
        {
            // EnemyMovement에서 Rigidbody 가져오기
            var networkRb = enemy.GetComponent<NetworkRigidbody2D>();
            if (networkRb != null && networkRb.Rigidbody != null)
            {
                float knockbackForce = 5f; // 넉백 힘 (데이터에서 가져올 수도 있음)
                networkRb.Rigidbody.velocity = finalKnockbackDirection * knockbackForce;
            }
        }
    }
    
    /// <summary>
    /// 플레이어 충돌을 처리합니다.
    /// </summary>
    private void HandlePlayerCollision(PlayerController otherPlayer, Collider2D collisionCollider)
    {
        Debug.Log($"[DashMagicObject] {_owner.name} - HandlePlayerCollision called with {otherPlayer?.name ?? "null"}");
        
        if (otherPlayer == null)
        {
            Debug.LogWarning($"[DashMagicObject] {_owner.name} - HandlePlayerCollision: otherPlayer is null");
            return;
        }
        
        if (otherPlayer == _owner)
        {
            Debug.Log($"[DashMagicObject] {_owner.name} - HandlePlayerCollision: Collision with self, ignoring");
            return;
        }
        
        if (!_owner.HasDashSkill)
        {
            Debug.Log($"[DashMagicObject] {_owner.name} - HandlePlayerCollision: Owner doesn't have DashSkill");
            return;
        }
        
        if (!otherPlayer.HasDashSkill)
        {
            Debug.Log($"[DashMagicObject] {_owner.name} - HandlePlayerCollision: Other player {otherPlayer.name} doesn't have DashSkill");
            return;
        }
        
        if (!_owner.DashIsMoving)
        {
            Debug.Log($"[DashMagicObject] {_owner.name} - HandlePlayerCollision: Owner is not moving (DashIsMoving={_owner.DashIsMoving})");
            return;
        }
        
        if (!otherPlayer.DashIsMoving)
        {
            Debug.Log($"[DashMagicObject] {_owner.name} - HandlePlayerCollision: Other player {otherPlayer.name} is not moving (DashIsMoving={otherPlayer.DashIsMoving})");
            return;
        }
        
        // 최근 충돌 체크
        if (_recentHitPlayers.Contains(otherPlayer))
        {
            Debug.Log($"[DashMagicObject] {_owner.name} - Recent collision with {otherPlayer.name}, ignoring");
            return;
        }
        
        Debug.Log($"[DashMagicObject] {_owner.name} collided with {otherPlayer.name} - All checks passed, processing collision");
        
        // 정면 판정 확인
        bool isFrontCollision = CheckFrontCollision(otherPlayer);
        
        if (isFrontCollision)
        {
            // 정면 충돌: 강화
            Debug.Log($"[DashMagicObject] {_owner.name} - Front collision with {otherPlayer.name}, enhancing! (Current: {_owner.DashEnhancementCount})");
            HandleEnhancement(otherPlayer);
        }
        else
        {
            // 잘못된 충돌: 스킬 즉시 종료 및 행동 불능
            Debug.LogWarning($"[DashMagicObject] {_owner.name} - Wrong collision with {otherPlayer.name}, skill ending and stun applied");
            HandleWrongCollision();
        }
        
        // 두 플레이어 모두 정지 상태로 복귀
        _owner.DashIsMoving = false;
        _owner.DashVelocity = Vector2.zero;
        UpdateRigidbodyVelocity(Vector2.zero);
        
        if (otherPlayer.DashMagicObject != null)
        {
            otherPlayer.DashMagicObject.OnPlayerCollision(_owner);
        }
        
        // 재충돌 방지
        _recentHitPlayers.Add(otherPlayer);
        StartCoroutine(RemoveFromRecentHitPlayers(otherPlayer, 0.5f));
    }
    
    /// <summary>
    /// 다른 플레이어의 DashMagicObject에서 호출됩니다.
    /// </summary>
    public void OnPlayerCollision(PlayerController otherPlayer)
    {
        if (otherPlayer == null) return;
        _recentHitPlayers.Add(otherPlayer);
        StartCoroutine(RemoveFromRecentHitPlayers(otherPlayer, 0.5f));
    }
    
    /// <summary>
    /// 정면 충돌인지 확인합니다.
    /// </summary>
    private bool CheckFrontCollision(PlayerController otherPlayer)
    {
        if (_owner == null || otherPlayer == null) return false;
        
        Vector2 toOther = (otherPlayer.transform.position - _owner.transform.position).normalized;
        Vector2 frontDirection = _owner.DashLastInputDirection;
        
        if (frontDirection.magnitude < 0.1f)
        {
            // 입력 방향이 없으면 현재 속도 방향 사용
            frontDirection = _owner.DashVelocity.normalized;
            if (frontDirection.magnitude < 0.1f)
            {
                // 속도도 없으면 충돌 위치 기준
                frontDirection = toOther;
            }
        }
        
        float angle = Vector2.Angle(frontDirection, toOther);
        float halfAngleRange = _dashData.playerCollisionFrontAngle * 0.5f;
        bool isFront = angle <= halfAngleRange;
        
        Debug.Log($"[DashMagicObject] {_owner.name} - Front collision check with {otherPlayer.name}: " +
                  $"Angle={angle:F1}°, HalfRange={halfAngleRange:F1}°, IsFront={isFront}, " +
                  $"FrontDir={frontDirection}, ToOther={toOther}");
        
        return isFront;
    }
    
    /// <summary>
    /// 강화를 처리합니다.
    /// </summary>
    private void HandleEnhancement(PlayerController otherPlayer)
    {
        if (_owner == null || _owner.Runner == null) return;
        if (_owner.IsDashFinalEnhancement) return; // 이미 최종 강화
        
        int currentEnhancement = _owner.DashEnhancementCount;
        
        // 강화 횟수 증가
        _owner.DashEnhancementCount = Mathf.Min(currentEnhancement + 1, _dashData.finalEnhancementCount);
        
        // 최종 강화 확인
        if (_owner.DashEnhancementCount >= _dashData.finalEnhancementCount)
        {
            // 최종 강화 활성화
            _owner.IsDashFinalEnhancement = true;
            _owner.DashSkillTimer = TickTimer.CreateFromSeconds(_owner.Runner, _dashData.finalEnhancementDuration);
        }
        else
        {
            // 일반 강화: 지속 시간 증가
            float remainingTime = _owner.DashSkillTimer.RemainingTime(_owner.Runner) ?? 0f;
            float newTime = Mathf.Min(
                _dashData.maxDuration,
                remainingTime + _dashData.durationIncreasePerEnhancement
            );
            _owner.DashSkillTimer = TickTimer.CreateFromSeconds(_owner.Runner, newTime);
        }
        
        // 베리어 스프라이트 업데이트
        UpdateBarrierSprite();
        
        // 다른 플레이어도 강화 (상호 강화)
        if (otherPlayer.DashMagicObject != null)
        {
            otherPlayer.DashMagicObject.ApplyEnhancement(_owner.DashEnhancementCount);
        }
    }
    
    /// <summary>
    /// 다른 플레이어의 DashMagicObject에서 호출됩니다.
    /// </summary>
    public void ApplyEnhancement(int otherPlayerEnhancement)
    {
        if (_owner == null || _owner.Runner == null) return;
        if (_owner.IsDashFinalEnhancement) return;
        
        // 상대 플레이어와 같은 강화 레벨로 맞춤
        _owner.DashEnhancementCount = Mathf.Min(otherPlayerEnhancement, _dashData.finalEnhancementCount);
        
        if (_owner.DashEnhancementCount >= _dashData.finalEnhancementCount)
        {
            _owner.IsDashFinalEnhancement = true;
            _owner.DashSkillTimer = TickTimer.CreateFromSeconds(_owner.Runner, _dashData.finalEnhancementDuration);
        }
        else
        {
            float remainingTime = _owner.DashSkillTimer.RemainingTime(_owner.Runner) ?? 0f;
            float newTime = Mathf.Min(
                _dashData.maxDuration,
                remainingTime + _dashData.durationIncreasePerEnhancement
            );
            _owner.DashSkillTimer = TickTimer.CreateFromSeconds(_owner.Runner, newTime);
        }
        
        // 베리어 스프라이트 업데이트
        UpdateBarrierSprite();
    }
    
    /// <summary>
    /// 잘못된 충돌을 처리합니다.
    /// </summary>
    private void HandleWrongCollision()
    {
        if (_owner == null || _owner.Runner == null) return;
        
        // 스킬 즉시 종료
        _owner.HasDashSkill = false;
        _owner.DashSkillTimer = TickTimer.None;
        
        // PlayerEffectManager를 사용하여 행동불능 (이동속도 0%)
        if (_owner.EffectManager != null && _dashData != null)
        {
            _owner.EffectManager.AddEffect(EffectType.MoveSpeed, 0f, _dashData.wrongCollisionStunDuration, false);
        }
    }
    
    /// <summary>
    /// 데미지를 계산합니다.
    /// </summary>
    private float CalculateDamage()
    {
        if (_dashData == null) return _dashData.baseDamage;
        
        float damage = _dashData.baseDamage;
        
        if (_owner.IsDashFinalEnhancement)
        {
            // 최종 강화: 강화로 증가한 피해는 합산하지 않고 고정 보너스만 추가
            damage = _dashData.baseDamage + _dashData.finalEnhancementDamageBonus;
        }
        else
        {
            // 일반 강화: 강화 횟수만큼 피해 증가
            damage += _dashData.damageIncreasePerEnhancement * _owner.DashEnhancementCount;
        }
        
        return damage;
    }
    #endregion
    
    #region Skill End Conditions
    /// <summary>
    /// 스킬 종료 조건을 확인합니다.
    /// </summary>
    private void CheckEndConditions()
    {
        if (_owner == null || _owner.Runner == null) return;
        if (!_owner.HasDashSkill) return;
        
        // 정지 상태가 끝난 후 일정 시간(예: 0.5초) 동안은 속도 0으로 종료하지 않음
        float stunEndTime = _dashData.initialStunDuration;
        float remainingStunTime = _owner.DashStunTimer.RemainingTime(_owner.Runner) ?? 0f;
        bool isInGracePeriod = remainingStunTime <= 0f && remainingStunTime >= -0.5f; // 정지 종료 후 0.5초 유예 기간
        
        // 속도 0 체크 (입력을 받았고, 유예 기간이 지났으며, 정지 상태가 끝났을 때만)
        if (_owner.DashVelocity.magnitude < 0.1f && 
            _owner.DashStunTimer.ExpiredOrNotRunning(_owner.Runner) &&
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
        if (_owner.DashSkillTimer.ExpiredOrNotRunning(_owner.Runner))
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
        
        // 스킬 상태 초기화
        _owner.HasDashSkill = false;
        _owner.DashSkillTimer = TickTimer.None;
        _owner.DashEnhancementCount = 0;
        _owner.IsDashFinalEnhancement = false;
        _owner.DashIsMoving = false;
        _owner.DashVelocity = Vector2.zero;
        _owner.DashLastInputDirection = Vector2.zero;
        
        // 속도 초기화
        UpdateRigidbodyVelocity(Vector2.zero);
        
        // 카메라 복원 (로컬 플레이어만)
        if (_owner.Object.HasInputAuthority)
        {
            RestoreCamera();
        }
        
        // 오브젝트 제거
        if (gameObject != null)
        {
            Destroy(gameObject);
        }
        
    }
    #endregion
    
    #region Camera Control
    /// <summary>
    /// 카메라를 두 플레이어의 중앙으로 설정하고 줌 아웃합니다.
    /// </summary>
    private void SetupCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;
        
        _cameraController = mainCamera.GetComponent<MainCameraController>();
        if (_cameraController == null) return;
        
        // 다른 플레이어 찾기
        PlayerController otherPlayer = FindOtherDashPlayer();
        if (otherPlayer == null) return;
        
        // 두 플레이어의 중앙 계산
        Vector3 center = (_owner.transform.position + otherPlayer.transform.position) * 0.5f;
        
        // 카메라 크기 저장
        _originalCameraSize = mainCamera.orthographicSize;
        
        // 카메라 줌 아웃 (3배)
        float zoomMultiplier = _dashData != null ? _dashData.cameraZoomOutMultiplier : 3f;
        mainCamera.orthographicSize = _originalCameraSize * zoomMultiplier;
        
        // 카메라를 중앙으로 이동
        mainCamera.transform.position = new Vector3(center.x, center.y, mainCamera.transform.position.z);
        
        // 카메라 고정 (MainCameraController 비활성화)
        _cameraController.enabled = false;
        _cameraLocked = true;
    }
    
    /// <summary>
    /// 카메라를 원래 상태로 복원합니다.
    /// </summary>
    private void RestoreCamera()
    {
        if (!_cameraLocked) return;
        
        Camera mainCamera = Camera.main;
        if (mainCamera == null || _cameraController == null) return;
        
        // 카메라 크기 복원
        mainCamera.orthographicSize = _originalCameraSize;
        
        // 카메라 제어 복원
        _cameraController.enabled = true;
        _cameraLocked = false;
    }
    
    /// <summary>
    /// 다른 돌진 스킬 사용 플레이어를 찾습니다.
    /// </summary>
    private PlayerController FindOtherDashPlayer()
    {
        List<PlayerController> allPlayers = GetAllPlayers();
        
        foreach (var player in allPlayers)
        {
            if (player != null && player != _owner && player.HasDashSkill && !player.IsDead)
            {
                return player;
            }
        }
        
        return null;
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
    /// </summary>
    private void CleanupCollisionCooldowns()
    {
        if (_owner == null || _owner.Runner == null) return;
        
        var keysToRemove = new List<EnemyController>();
        
        foreach (var kvp in _enemyCollisionCooldowns)
        {
            if (kvp.Key == null || kvp.Key.IsDead || kvp.Value.ExpiredOrNotRunning(_owner.Runner))
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
    
    /// <summary>
    /// 강화 상태에 따라 베리어 스프라이트를 업데이트합니다.
    /// </summary>
    private void UpdateBarrierSprite()
    {
        if (_barrierSpriteRenderer == null || _dashData == null || _owner == null) return;
        
        Sprite targetSprite = null;
        
        // 최종 강화 (강화 2회)
        if (_owner.IsDashFinalEnhancement)
        {
            targetSprite = _dashData.finalEnhancementBarrierSprite;
        }
        // 강화 (강화 1회)
        else if (_owner.DashEnhancementCount == 1)
        {
            targetSprite = _dashData.enhancedBarrierSprite;
        }
        // 기본 (강화 0회)
        else
        {
            targetSprite = _dashData.baseBarrierSprite;
        }
        
        // 스프라이트가 설정되어 있으면 변경
        if (targetSprite != null)
        {
            _barrierSpriteRenderer.sprite = targetSprite;
        }
    }
    #endregion
    
    #region Cleanup
    void OnDestroy()
    {
        // 카메라 복원
        if (_cameraLocked && _owner != null && _owner.Object.HasInputAuthority)
        {
            RestoreCamera();
        }
        
        // 스킬 상태 정리
        if (_owner != null && _owner.Object.HasStateAuthority)
        {
            _owner.HasDashSkill = false;
            _owner.DashSkillTimer = TickTimer.None;
            _owner.DashIsMoving = false;
            _owner.DashVelocity = Vector2.zero;
        }
    }
    #endregion
}

