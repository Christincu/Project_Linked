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
    private GameObject _frontDetectionObj; // 정면 판정 오브젝트
    private CircleCollider2D _frontDetectionCollider; // 정면 판정 콜리더
    
    // 이동 관련
    private Vector2 _currentVelocity = Vector2.zero;
    private Vector2 _lastInputDirection = Vector2.zero;
    private bool _hasReceivedInput = false; // 입력을 받았는지 여부
    
    // 충돌 관련
    private HashSet<EnemyController> _recentHitEnemies = new HashSet<EnemyController>(); // 최근 충돌한 적 (재충돌 방지)
    // [개선] NetworkId 기반 쿨다운 관리 (더 안정적)
    private Dictionary<NetworkId, TickTimer> _enemyCollisionCooldowns = new Dictionary<NetworkId, TickTimer>();
    private HashSet<PlayerController> _recentHitPlayers = new HashSet<PlayerController>(); // 최근 충돌한 플레이어
    
    // 카메라 관련
    private MainCameraController _cameraController;
    private Vector3 _originalCameraPosition;
    private float _originalCameraSize;
    private bool _cameraLocked = false;
    
    // 베리어 시각화 관련
    private SpriteRenderer _barrierSpriteRenderer;
    private int _lastEnhancementCount = -1;
    private bool _lastIsFinalEnhancement = false;
    #endregion
    
    #region Unity Callbacks
    void Awake()
    {
        // [개선] 공격 판정용 Circle Collider는 이제 시각적/디버그 용도로만 사용
        // 실제 충돌 감지는 FixedUpdateNetwork에서 OverlapCircle 사용
        _attackCollider = gameObject.AddComponent<CircleCollider2D>();
        _attackCollider.radius = attackColliderRadius;
        _attackCollider.isTrigger = true;
        _attackCollider.enabled = false; // 초기에는 비활성화
        
        // 정면 판정 오브젝트 생성 (플레이어 충돌 판정용)
        _frontDetectionObj = new GameObject("FrontDetection");
        _frontDetectionObj.transform.SetParent(transform);
        _frontDetectionObj.transform.localPosition = Vector3.zero;
        _frontDetectionCollider = _frontDetectionObj.AddComponent<CircleCollider2D>();
        _frontDetectionCollider.radius = 0.5f;
        _frontDetectionCollider.isTrigger = true;
        _frontDetectionCollider.enabled = false; // 초기에는 비활성화
        
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
    
    void Update()
    {
        if (_owner == null) return;
        
        // 카메라 설정 (Input Authority만, HasDashSkill이 true가 되었을 때)
        if (_owner.Object.HasInputAuthority && _owner.HasDashSkill && !_cameraLocked)
        {
            SetupCamera();
        }
        
        // 베리어 스프라이트 업데이트 (모든 클라이언트에서)
        // 네트워크 변수 변경 감지
        if (_owner.HasDashSkill)
        {
            // 강화 상태가 변경되었는지 확인
            bool enhancementChanged = _lastEnhancementCount != _owner.DashEnhancementCount ||
                                     _lastIsFinalEnhancement != _owner.IsDashFinalEnhancement;
            
            // 초기화 시 또는 변경 시 업데이트
            if (enhancementChanged || _lastEnhancementCount == -1)
            {
                UpdateBarrierSprite();
                _lastEnhancementCount = _owner.DashEnhancementCount;
                _lastIsFinalEnhancement = _owner.IsDashFinalEnhancement;
            }
            
            // 스프라이트가 null이면 다시 시도 (초기화 지연 대응)
            if (_barrierSpriteRenderer != null && _barrierSpriteRenderer.sprite == null && _dashData != null)
            {
                UpdateBarrierSprite();
            }
        }
        else
        {
            // 스킬이 비활성화되면 스프라이트 숨기기
            if (_barrierSpriteRenderer != null)
            {
                _barrierSpriteRenderer.sprite = null;
            }
            _lastEnhancementCount = -1;
            _lastIsFinalEnhancement = false;
        }
        
        // 카메라 위치 업데이트 (Input Authority만, 이동 중일 때)
        if (_owner.Object.HasInputAuthority && _cameraLocked && _owner.HasDashSkill)
        {
            UpdateCameraPosition();
        }
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
        
        // [신규] 반동 대기(Freeze) 및 발사 로직 처리
        if (_owner.DashIsWaitingToRecoil)
        {
            // 정지 타이머가 아직 도는 중이라면 -> 위치 고정 (Freeze)
            if (_owner.DashStunTimer.IsRunning && !_owner.DashStunTimer.Expired(_owner.Runner))
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
        
        // 입력 처리 및 이동 업데이트 (반동 대기 중이 아닐 때만)
        if (!_owner.DashIsWaitingToRecoil)
        {
            ProcessDashMovement();
        }
        
        // [핵심 개선] 물리 충돌 감지 (FixedUpdateNetwork 내에서 처리하여 롤백 안전)
        DetectCollisions();
        
        // 스킬 종료 조건 확인
        CheckEndConditions();
        
        // 충돌 타임아웃 정리
        CleanupCollisionCooldowns();
    }
    
    /// <summary>
    /// [DEPRECATED] OnTriggerEnter2D는 Fusion의 Resimulation 중 중복 호출될 수 있어 제거됨.
    /// 대신 FixedUpdateNetwork에서 DetectCollisions()를 사용합니다.
    /// </summary>
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
        
        // [DEPRECATED] OnTriggerEnter2D는 더 이상 사용하지 않습니다.
        // FixedUpdateNetwork의 DetectCollisions()를 사용하세요.
        // 이 메서드는 하위 호환성을 위해 남겨두었지만 호출되지 않습니다.
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
        
        // 초기 강화 상태 추적
        _lastEnhancementCount = -1;
        _lastIsFinalEnhancement = false;
        
        // 초기 스프라이트 설정 (모든 클라이언트에서)
        if (_barrierSpriteRenderer != null && _dashData != null)
        {
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
    /// [핵심 개선] FixedUpdateNetwork 내에서 물리 충돌을 직접 감지합니다.
    /// 롤백 상황에서도 안전하며, 정확한 타이밍에 충돌을 처리합니다.
    /// </summary>
    private void DetectCollisions()
    {
        if (!_attackCollider.enabled) return;
        if (_owner == null || _owner.Runner == null) return;
        if (!_owner.DashIsMoving) return; // 정지 상태에서는 충돌 처리 안 함
        
        // 공격 범위 내의 모든 콜라이더 감지
        // State Authority에서만 실행되므로 Physics2D 사용이 안전함
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(
            transform.position,
            attackColliderRadius,
            enemyLayer | playerLayer // 적과 플레이어 모두 감지
        );
        
        if (hitColliders == null || hitColliders.Length == 0) return;
        
        foreach (var hit in hitColliders)
        {
            if (hit == null) continue;
            
            // 1. 적 충돌 처리
            var enemy = hit.GetComponent<EnemyController>();
            if (enemy == null)
            {
                enemy = hit.GetComponentInParent<EnemyController>();
            }
            if (enemy == null && hit.attachedRigidbody != null)
            {
                enemy = hit.attachedRigidbody.GetComponent<EnemyController>();
            }
            
            if (enemy != null && !enemy.IsDead && enemy.Object != null)
            {
                // 재충돌 쿨다운 확인 (NetworkId 기반)
                if (_enemyCollisionCooldowns.TryGetValue(enemy.Object.Id, out TickTimer cooldown))
                {
                    if (cooldown.IsRunning && !cooldown.Expired(_owner.Runner))
                    {
                        // 쿨다운 중: 플레이어가 피해를 입음
                        if (_owner.State != null)
                        {
                            _owner.State.TakeDamage(_dashData.baseDamage);
                        }
                        continue; // 아직 쿨다운 중임
                    }
                }
                
                Debug.Log($"[DashHit] {_owner.name} - Hit Enemy: {enemy.name} at position {transform.position}");
                HandleEnemyCollision(enemy);
                continue;
            }
            
            // 2. 플레이어 충돌 처리
            var otherPlayer = hit.GetComponent<PlayerController>();
            if (otherPlayer == null)
            {
                otherPlayer = hit.GetComponentInParent<PlayerController>();
            }
            if (otherPlayer == null && hit.attachedRigidbody != null)
            {
                otherPlayer = hit.attachedRigidbody.GetComponent<PlayerController>();
            }
            
            if (otherPlayer != null && otherPlayer != _owner && !otherPlayer.IsDead)
            {
                // 상대방도 돌진 스킬 사용 중인지 확인
                if (otherPlayer.HasDashSkill && otherPlayer.DashIsMoving)
                {
                    HandlePlayerCollision(otherPlayer);
                }
            }
        }
    }
    
    /// <summary>
    /// 적 충돌을 처리합니다.
    /// </summary>
    private void HandleEnemyCollision(EnemyController enemy)
    {
        if (enemy == null || enemy.IsDead || enemy.Object == null) return;
        if (_owner == null || !_owner.HasDashSkill) return;
        if (!_owner.DashIsMoving) return; // 정지 상태에서는 충돌 처리 안 함
        
        // 최종 강화 상태에서는 충돌 판정 제거
        if (_owner.IsDashFinalEnhancement) return;
        
        // 데미지 계산
        float damage = CalculateDamage();
        
        // 적에게 데미지 적용
        if (enemy.State != null)
        {
            Debug.Log($"[DashHit] {_owner.name} - Applying {damage} damage to {enemy.name} " +
                     $"(Current Health: {enemy.State.CurrentHealth}/{enemy.State.MaxHealth})");
            enemy.State.TakeDamage(damage);
            Debug.Log($"[DashHit] {_owner.name} - After damage: {enemy.name} " +
                     $"(Current Health: {enemy.State.CurrentHealth}/{enemy.State.MaxHealth})");
        }
        else
        {
            Debug.LogWarning($"[DashHit] {_owner.name} - Enemy {enemy.name} has no State component!");
        }
        
        // 적 넉백
        ApplyEnemyKnockback(enemy);
        
        // 지속 시간 감소
        float remainingTime = _owner.DashSkillTimer.RemainingTime(_owner.Runner) ?? 0f;
        float newTime = Mathf.Max(0f, remainingTime - _dashData.durationReductionOnHit);
        _owner.DashSkillTimer = TickTimer.CreateFromSeconds(_owner.Runner, newTime);
        
        // 재충돌 방지 타이머 설정 (NetworkId 기반)
        _enemyCollisionCooldowns[enemy.Object.Id] = TickTimer.CreateFromSeconds(_owner.Runner, _dashData.enemyCollisionCooldown);
    }
    
    /// <summary>
    /// 적에게 넉백을 적용합니다.
    /// [개선] 넉백 방향 계산 보강 (비상용 방향 계산 추가)
    /// </summary>
    private void ApplyEnemyKnockback(EnemyController enemy)
    {
        if (enemy == null || _owner == null || _owner.Runner == null) return;
        
        // 1. 넉백 방향 결정
        // 현재 속도를 기준으로 하되, 속도가 0에 가까우면 '마지막 입력 방향'을 사용
        Vector2 knockbackDirection = _owner.DashVelocity.normalized;
        
        if (knockbackDirection.magnitude < 0.1f)
        {
            knockbackDirection = _owner.DashLastInputDirection.normalized;
            
            // 입력도 없으면 적과 나의 위치 차이로 계산 (비상용)
            if (knockbackDirection.magnitude < 0.1f)
            {
                Vector2 toEnemy = (enemy.transform.position - transform.position);
                if (toEnemy.magnitude > 0.1f)
                {
                    knockbackDirection = toEnemy.normalized;
                }
                else
                {
                    knockbackDirection = Vector2.up; // 최종 기본값
                }
            }
        }
        
        // 2. 각도 비틀기 (랜덤성 추가)
        // Atan2는 라디안을 반환하므로 Deg2Rad 변환 주의
        float baseAngle = Mathf.Atan2(knockbackDirection.y, knockbackDirection.x) * Mathf.Rad2Deg;
        
        // -45 ~ +45도 사이 랜덤
        float randomAngleOffset = Random.Range(-_dashData.enemyKnockbackAngleRange, _dashData.enemyKnockbackAngleRange);
        float finalAngleRad = (baseAngle + randomAngleOffset) * Mathf.Deg2Rad;
        Vector2 finalDirection = new Vector2(Mathf.Cos(finalAngleRad), Mathf.Sin(finalAngleRad));
        
        // 3. 적에게 힘 가하기
        // [중요] 적이 NetworkRigidbody2D를 가지고 있어야 동기화됩니다.
        var networkRb = enemy.GetComponent<NetworkRigidbody2D>();
        if (networkRb != null && networkRb.Rigidbody != null)
        {
            // 데이터에서 넉백 힘 가져오기
            float knockbackForce = _dashData.enemyKnockbackForce;
            
            // 속도를 직접 덮어씌워서 확실하게 밀려나게 함 (Impulse 효과)
            networkRb.Rigidbody.velocity = finalDirection * knockbackForce;
            
            // 넉백 타이머 시작 (데이터에서 가져온 시간 동안 NavMeshAgent 무시)
            enemy.KnockbackTimer = TickTimer.CreateFromSeconds(_owner.Runner, _dashData.enemyKnockbackDuration);
            
            Debug.Log($"[DashKnockback] {_owner.name} - Applied knockback to {enemy.name}: " +
                     $"Direction={finalDirection}, Force={knockbackForce}, " +
                     $"BaseAngle={baseAngle:F1}°, RandomOffset={randomAngleOffset:F1}°, " +
                     $"FinalAngle={(baseAngle + randomAngleOffset):F1}°");
        }
        else
        {
            Debug.LogWarning($"[DashKnockback] {_owner.name} - Enemy {enemy.name} has no NetworkRigidbody2D component!");
        }
    }
    
    /// <summary>
    /// 플레이어 충돌을 처리합니다.
    /// </summary>
    private void HandlePlayerCollision(PlayerController otherPlayer)
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
            // 정면 충돌: 강화 및 정지→튕김 시퀀스 시작
            Debug.Log($"[DashCollision] {_owner.name} - Perfect Hit! Freezing for {_dashData.playerCollisionFreezeDuration}s...");
            
            // --- A. 강화 적용 ---
            HandleEnhancement(otherPlayer);
            
            // --- B. 정지(Freeze) 시작 ---
            // playerCollisionFreezeDuration 동안 조작 및 이동 불가
            _owner.DashStunTimer = TickTimer.CreateFromSeconds(_owner.Runner, _dashData.playerCollisionFreezeDuration);
            
            // 속도 0으로 초기화
            _owner.DashVelocity = Vector2.zero;
            _owner.DashIsMoving = false;
            UpdateRigidbodyVelocity(Vector2.zero);
            
            // --- C. 반동(Recoil) 예약 ---
            // 상대방의 반대 방향 계산 (서로 멀어지는 방향)
            Vector2 recoilDir = (transform.position - otherPlayer.transform.position).normalized;
            if (recoilDir.magnitude < 0.1f)
            {
                // 예외 처리: 위치가 같으면 마지막 입력 방향의 반대
                recoilDir = -_owner.DashLastInputDirection.normalized;
                if (recoilDir.magnitude < 0.1f)
                {
                    recoilDir = Vector2.up; // 최종 기본값
                }
            }
            _owner.DashPendingRecoilDirection = recoilDir;
            _owner.DashIsWaitingToRecoil = true;
            
            // --- D. 쿨다운 등록 ---
            // 정지 시간 + 튕겨나가는 시간 동안은 다시 충돌하지 않도록 여유를 둠
            _recentHitPlayers.Add(otherPlayer);
            StartCoroutine(RemoveFromRecentHitPlayers(otherPlayer, _dashData.playerCollisionFreezeDuration + 0.5f));
            
            // 상대방도 동일한 처리 (상호 반동)
            if (otherPlayer.DashMagicObject != null)
            {
                otherPlayer.DashMagicObject.OnPlayerCollision(_owner);
            }
        }
        else
        {
            // 잘못된 충돌: 스킬 즉시 종료 및 행동 불능
            Debug.LogWarning($"[DashCollision] {_owner.name} - Wrong collision with {otherPlayer.name}, skill ending and stun applied");
            HandleWrongCollision();
        }
    }
    
    /// <summary>
    /// 다른 플레이어의 DashMagicObject에서 호출됩니다.
    /// 상대방도 동일한 정지→튕김 시퀀스를 시작합니다.
    /// </summary>
    public void OnPlayerCollision(PlayerController otherPlayer)
    {
        if (otherPlayer == null || _owner == null || _owner.Runner == null) return;
        if (!_owner.Object.HasStateAuthority) return;
        
        // 재충돌 체크
        if (_recentHitPlayers.Contains(otherPlayer)) return;
        
        // 상대방의 반대 방향 계산 (서로 멀어지는 방향)
        Vector2 recoilDir = (transform.position - otherPlayer.transform.position).normalized;
        if (recoilDir.magnitude < 0.1f)
        {
            recoilDir = -_owner.DashLastInputDirection.normalized;
            if (recoilDir.magnitude < 0.1f)
            {
                recoilDir = Vector2.up; // 최종 기본값
            }
        }
        
        // 정지(Freeze) 시작
        _owner.DashStunTimer = TickTimer.CreateFromSeconds(_owner.Runner, _dashData.playerCollisionFreezeDuration);
        _owner.DashVelocity = Vector2.zero;
        _owner.DashIsMoving = false;
        UpdateRigidbodyVelocity(Vector2.zero);
        
        // 반동(Recoil) 예약
        _owner.DashPendingRecoilDirection = recoilDir;
        _owner.DashIsWaitingToRecoil = true;
        
        // 쿨다운 등록
        _recentHitPlayers.Add(otherPlayer);
        StartCoroutine(RemoveFromRecentHitPlayers(otherPlayer, _dashData.playerCollisionFreezeDuration + 0.5f));
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
    /// 0.5초 대기 후 실제로 튕겨져 나가는 로직
    /// </summary>
    private void ExecuteRecoil()
    {
        if (_owner == null || _owner.Runner == null) return;
        
        Debug.Log($"[DashRecoil] {_owner.name} - Launching Recoil! Direction: {_owner.DashPendingRecoilDirection}");
        
        // 1. 상태 해제
        _owner.DashIsWaitingToRecoil = false;
        
        // 2. 물리 힘 적용 (순간적인 Impulse)
        // DashVelocity에 직접 값을 넣어 ProcessMovement가 이를 이어받게 함
        Vector2 launchVelocity = _owner.DashPendingRecoilDirection * _dashData.playerCollisionRecoilForce;
        _owner.DashVelocity = launchVelocity;
        _owner.DashIsMoving = true;
        UpdateRigidbodyVelocity(launchVelocity);
        
        // 3. 공격 콜라이더 활성화 (다시 충돌 가능하도록)
        if (_attackCollider != null)
        {
            _attackCollider.enabled = true;
        }
        
        // 4. 발사되는 순간 스킬 지속시간을 약간 연장해주면 조작감이 좋아짐
        float currentRemaining = _owner.DashSkillTimer.RemainingTime(_owner.Runner) ?? 0f;
        _owner.DashSkillTimer = TickTimer.CreateFromSeconds(_owner.Runner, currentRemaining + _dashData.playerCollisionRecoilTimeExtension);
        
        // 5. 반동 방향 초기화
        _owner.DashPendingRecoilDirection = Vector2.zero;
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
        // 단, 반동 대기 중이면 종료하지 않음
        if (!_owner.DashIsWaitingToRecoil &&
            _owner.DashVelocity.magnitude < 0.1f && 
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
        
        // 카메라 복원 (로컬 플레이어만, 안전한 null 체크)
        if (_owner.Object != null && _owner.Object.HasInputAuthority)
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
        
        // 카메라 크기 저장 (아직 저장되지 않았을 때만)
        if (_originalCameraSize <= 0f)
        {
            _originalCameraSize = mainCamera.orthographicSize;
        }
        
        // 카메라 줌 아웃 (3배)
        float zoomMultiplier = _dashData != null ? _dashData.cameraZoomOutMultiplier : 3f;
        mainCamera.orthographicSize = _originalCameraSize * zoomMultiplier;
        
        // 카메라 고정 (MainCameraController 비활성화)
        _cameraController.enabled = false;
        _cameraLocked = true;
        
        // 초기 카메라 위치 설정
        UpdateCameraPosition();
    }
    
    /// <summary>
    /// 카메라 위치를 두 플레이어의 중앙으로 업데이트합니다.
    /// </summary>
    private void UpdateCameraPosition()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;
        
        // 다른 플레이어 찾기
        PlayerController otherPlayer = FindOtherDashPlayer();
        if (otherPlayer == null) return;
        
        // 두 플레이어의 중앙 계산
        Vector3 center = (_owner.transform.position + otherPlayer.transform.position) * 0.5f;
        
        // 카메라를 중앙으로 이동
        mainCamera.transform.position = new Vector3(center.x, center.y, mainCamera.transform.position.z);
    }
    
    /// <summary>
    /// 카메라를 원래 상태로 복원합니다.
    /// </summary>
    private void RestoreCamera()
    {
        if (!_cameraLocked) return;
        
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;
        
        // 카메라 크기 복원
        if (_originalCameraSize > 0f)
        {
            mainCamera.orthographicSize = _originalCameraSize;
        }
        
        // 카메라 제어 복원
        if (_cameraController != null)
        {
            _cameraController.enabled = true;
        }
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
    /// [개선] NetworkId 기반으로 변경
    /// </summary>
    private void CleanupCollisionCooldowns()
    {
        if (_owner == null || _owner.Runner == null) return;
        
        var keysToRemove = new List<NetworkId>();
        
        foreach (var kvp in _enemyCollisionCooldowns)
        {
            if (kvp.Value.ExpiredOrNotRunning(_owner.Runner))
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
        if (_barrierSpriteRenderer == null || _owner == null) return;
        
        // _dashData가 없으면 다시 가져오기 시도
        if (_dashData == null)
        {
            if (_gameDataManager == null)
            {
                _gameDataManager = FindObjectOfType<GameDataManager>();
            }
            
            if (_gameDataManager != null && _gameDataManager.MagicService != null)
            {
                MagicCombinationData combinationData = _gameDataManager.MagicService.GetCombinationDataByResult(11);
                _dashData = combinationData as DashMagicCombinationData;
            }
        }
        
        if (_dashData == null) return;
        
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
            _barrierSpriteRenderer.enabled = true;
        }
        else
        {
            // 스프라이트가 없으면 렌더러 비활성화
            _barrierSpriteRenderer.enabled = false;
        }
    }
    #endregion
    
    #region Cleanup
    void OnDestroy()
    {
        // 카메라 복원 (안전한 null 체크)
        if (_cameraLocked && _owner != null && _owner.Object != null && _owner.Object.HasInputAuthority)
        {
            RestoreCamera();
        }
        
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

