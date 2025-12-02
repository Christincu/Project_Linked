using UnityEngine;
using Fusion;

/// <summary>
/// DashMagicObject의 이동 관련 로직
/// </summary>
public partial class DashMagicObject
{
    #region Movement Logic
    /// <summary>
    /// 돌진 이동을 처리합니다 (관성 기반).
    /// </summary>
    private void ProcessDashMovement()
    {
        if (_owner == null || Runner == null) return;
        if (_dashData == null) return;
        
        // 입력 가져오기
        Vector2 inputDirection = GetInputDirection();
        
        // 틱 시간 가져오기
        float deltaTime = Runner.DeltaTime;
        
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
        
        // 속도 제한 적용
        ClampVelocityToMaxSpeed();
        
        // Rigidbody에 속도 적용
        UpdateRigidbodyVelocity(_owner.DashVelocity);
        
        // 정지 상태 및 입력 방향 업데이트
        UpdateMovementState(inputDirection);
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
                if (velocity.sqrMagnitude <= MIN_VELOCITY_SQR)
                {
                    velocity = Vector2.zero;
                    _owner.DashIsMoving = false;
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
    /// 이동 상태를 업데이트합니다.
    /// </summary>
    private void UpdateMovementState(Vector2 inputDirection)
    {
        if (_owner == null) return;
        
        // 정지 상태 체크
        if (_owner.DashVelocity.sqrMagnitude < MIN_VELOCITY_SQR)
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
        if (inputDirection.sqrMagnitude > MIN_VELOCITY_SQR)
        {
            _owner.DashLastInputDirection = inputDirection;
        }
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
        if (_owner == null || Runner == null) return Vector2.zero;
        
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
            if (inputDir.sqrMagnitude > MIN_VELOCITY_SQR)
            {
                _hasReceivedInput = true;
            }
        }
        
        return inputDir;
    }
    
    /// <summary>
    /// 속도를 최대 속도로 제한합니다.
    /// </summary>
    private void ClampVelocityToMaxSpeed()
    {
        if (_owner == null || _dashData == null) return;
        
        float maxSpeed = _dashData.maxSpeed;
        float currentSpeedSqr = _owner.DashVelocity.sqrMagnitude;
        float maxSpeedSqr = maxSpeed * maxSpeed;
        
        if (currentSpeedSqr > maxSpeedSqr)
        {
            _owner.DashVelocity = _owner.DashVelocity.normalized * maxSpeed;
        }
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
}

