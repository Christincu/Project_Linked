using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

/// <summary>
/// 적의 물리 기반 이동을 처리합니다. (NetworkRigidbody2D 필수)
/// </summary>
public class EnemyMovement : MonoBehaviour
{
    #region Private Fields
    private NetworkRigidbody2D _networkRb;
    private Rigidbody2D _rigidbody;
    private EnemyController _controller;
    private Vector2 _targetDirection;

    // 이동 설정 (EnemyData에서 가져옴)
    private EnemyData _enemyData;

    // AI 상태
    private bool _isMovingToTarget = false;
    private Vector2 _targetPosition;
    #endregion

    #region Properties
    public NetworkRigidbody2D NetworkRigidbody2D => _networkRb;
    public Rigidbody2D Rigidbody => _rigidbody;
    public Vector2 TargetDirection => _targetDirection;
    public bool IsMoving => _isMovingToTarget;
    #endregion

    #region Initialization
    /// <summary>
    /// 초기화합니다.
    /// </summary>
    public void Initialize(EnemyController controller, EnemyData data)
    {
        _controller = controller;
        _enemyData = data;

        // NetworkRigidbody2D 컴포넌트 가져오기 (필수)
        _networkRb = GetComponent<NetworkRigidbody2D>();

        if (_networkRb != null)
        {
            _rigidbody = _networkRb.Rigidbody;
        }
        else
        {
            // NetworkRigidbody2D가 없으면 경고 후 Rigidbody만 사용
            Debug.LogError("[EnemyMovement] NetworkRigidbody2D is missing! Movement will not be networked.");
            _rigidbody = GetComponent<Rigidbody2D>() ?? gameObject.AddComponent<Rigidbody2D>();
        }

        ConfigureRigidbody();
    }

    /// <summary>
    /// Rigidbody2D를 2D 탑다운 게임에 맞게 설정합니다.
    /// </summary>
    private void ConfigureRigidbody()
    {
        if (_rigidbody == null) return;

        _rigidbody.freezeRotation = true;
        _rigidbody.gravityScale = 0f;
        _rigidbody.drag = 0f;
        _rigidbody.angularDrag = 0f;
        _rigidbody.interpolation = RigidbodyInterpolation2D.None;
        _rigidbody.sleepMode = RigidbodySleepMode2D.NeverSleep;
    }
    #endregion

    #region Movement
    /// <summary>
    /// 특정 위치로 이동을 시작합니다.
    /// </summary>
    public void MoveTo(Vector2 targetPosition)
    {
        _targetPosition = targetPosition;
        _isMovingToTarget = true;
    }

    /// <summary>
    /// 이동을 중지합니다.
    /// </summary>
    public void Stop()
    {
        _isMovingToTarget = false;
        _targetDirection = Vector2.zero;
        if (_rigidbody != null)
        {
            _rigidbody.velocity = Vector2.zero;
        }
    }

    /// <summary>
    /// 이동을 업데이트합니다. (FixedUpdateNetwork에서 호출)
    /// </summary>
    public void UpdateMovement()
    {
        if (_rigidbody == null || _controller == null || _enemyData == null) return;

        // 사망 상태면 이동 불가
        if (_controller.IsDead)
        {
            Stop();
            return;
        }

        // 이동 중이면 목표 위치로 이동
        if (_isMovingToTarget)
        {
            Vector2 currentPos = transform.position;
            Vector2 direction = _targetPosition - currentPos;
            float distance = direction.magnitude;

            // 목표 위치 도착
            if (distance <= _enemyData.stopDistance)
            {
                Stop();
                return;
            }

            _targetDirection = direction.normalized;
        }
        else
        {
            _targetDirection = Vector2.zero;
        }

        // 이동 처리
        ApplyMovement();
    }

    /// <summary>
    /// 목표 방향으로 이동을 적용합니다.
    /// </summary>
    private void ApplyMovement()
    {
        if (_rigidbody == null) return;

        // 틱 시간 가져오기
        float deltaTime = _controller.Runner != null ? _controller.Runner.DeltaTime : Time.fixedDeltaTime;

        if (_targetDirection.magnitude > 0 && _enemyData != null)
        {
            // 목표 속도 계산
            Vector2 targetVelocity = _targetDirection * _enemyData.moveSpeed;

            // 가속도 적용 (Lerp 방식)
            if (_enemyData.acceleration >= 1f)
            {
                // 즉시 이동
                _rigidbody.velocity = targetVelocity;
            }
            else
            {
                // 부드러운 가속
                float lerpRatio = _enemyData.acceleration * deltaTime * 10f;
                _rigidbody.velocity = Vector2.Lerp(_rigidbody.velocity, targetVelocity, lerpRatio);
            }
        }
        else
        {
            // 목표 방향이 없으면 즉시 정지
            _rigidbody.velocity = Vector2.zero;
        }

        LimitSpeed();
    }

    /// <summary>
    /// 최대 속도를 제한합니다.
    /// </summary>
    private void LimitSpeed()
    {
        if (_rigidbody == null || _enemyData == null || _enemyData.maxVelocity <= 0) return;

        if (_rigidbody.velocity.magnitude > _enemyData.maxVelocity)
        {
            _rigidbody.velocity = _rigidbody.velocity.normalized * _enemyData.maxVelocity;
        }
    }

    /// <summary>
    /// 속도를 초기화합니다.
    /// </summary>
    public void ResetVelocity()
    {
        if (_rigidbody != null)
        {
            _rigidbody.velocity = Vector2.zero;
        }
    }
    #endregion

    #region Public Getters
    public Vector2 GetVelocity() => _rigidbody != null ? _rigidbody.velocity : Vector2.zero;
    public float GetMoveSpeed() => _enemyData != null ? _enemyData.moveSpeed : 0f;
    public bool HasNetworkRigidbody() => _networkRb != null;
    #endregion
}

