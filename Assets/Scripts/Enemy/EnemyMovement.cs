using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;
using UnityEngine.AI;

/// <summary>
/// 적의 물리 기반 이동을 처리합니다. (NetworkRigidbody2D 필수)
/// </summary>
public class EnemyMovement : MonoBehaviour
{
    #region Private Fields
    private NavMeshAgent _navMeshAgent;
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
    public Vector2 TargetDirection => _targetDirection;
    public bool IsMoving => _isMovingToTarget;
    #endregion

    private void Awake()
    {
        var agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            // 2D 평면 고정을 위한 기본 설정
            agent.updateRotation = false;
            agent.updateUpAxis = false;
        }
    }

    #region Initialization
    /// <summary>
    /// 초기화합니다.
    /// </summary>
    public void Initialize(EnemyController controller, EnemyData data)
    {
        _controller = controller;
        _enemyData = data;

        // NavMeshAgent 컴포넌트 확보 (없으면 추가)
        _navMeshAgent = GetComponent<NavMeshAgent>();
        if (_navMeshAgent == null)
        {
            _navMeshAgent = gameObject.AddComponent<NavMeshAgent>();
        }

        // NetworkRigidbody2D 및 Rigidbody2D 가져오기
        _networkRb = GetComponent<NetworkRigidbody2D>();
        if (_networkRb != null)
        {
            _rigidbody = _networkRb.Rigidbody;
        }
        else
        {
            _rigidbody = GetComponent<Rigidbody2D>();
        }

        ConfigureNavMeshAgent();
    }

    /// <summary>
    /// NavMeshAgent를 2D 탑다운에 맞게 설정합니다.
    /// </summary>
    private void ConfigureNavMeshAgent()
    {
        if (_navMeshAgent == null) return;

        // 2D 전개 설정 (NavMeshComponents 2D 설정과 호환)
        _navMeshAgent.updateRotation = false;
        _navMeshAgent.updateUpAxis = false;
        _navMeshAgent.angularSpeed = 0f;
        _navMeshAgent.autoBraking = true;

        if (_enemyData != null)
        {
            _navMeshAgent.speed = _enemyData.moveSpeed;
            // NavMeshAgent.acceleration은 유닛/초^2 기준
            _navMeshAgent.acceleration = Mathf.Max(0.1f, _enemyData.acceleration * 10f);
            _navMeshAgent.stoppingDistance = Mathf.Max(0f, _enemyData.stopDistance);
        }
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
        if (_navMeshAgent != null)
        {
            _navMeshAgent.isStopped = false;
            _navMeshAgent.SetDestination(_targetPosition);
        }
    }

    /// <summary>
    /// 이동을 중지합니다.
    /// </summary>
    public void Stop()
    {
        _isMovingToTarget = false;
        _targetDirection = Vector2.zero;
        if (_navMeshAgent != null)
        {
            _navMeshAgent.isStopped = true;
            _navMeshAgent.ResetPath();
            _navMeshAgent.velocity = Vector3.zero;
        }
        // Rigidbody2D 속도도 0으로 설정
        if (_rigidbody != null)
        {
            _rigidbody.velocity = Vector2.zero;
        }
    }

    /// <summary>
    /// 이동을 업데이트합니다. (FixedUpdateNetwork에서 호출)
    /// NavMeshAgent의 desiredVelocity를 Rigidbody2D에 직접 적용하여 외부 힘(플레이어가 밀어낸 힘)을 무시합니다.
    /// 단, 넉백 중일 때는 NavMeshAgent 속도를 무시합니다.
    /// </summary>
    public void UpdateMovement()
    {
        if (_navMeshAgent == null || _controller == null || _enemyData == null) return;

        // 사망 상태면 이동 불가
        if (_controller.IsDead)
        {
            Stop();
            return;
        }

        // 넉백 중이면 NavMeshAgent 속도를 무시 (넉백이 우선)
        if (_controller.Runner != null && _controller.KnockbackTimer.IsRunning && !_controller.KnockbackTimer.Expired(_controller.Runner))
        {
            // 넉백 중에는 NavMeshAgent를 정지시키고 Rigidbody 속도는 그대로 유지
            if (_navMeshAgent != null)
            {
                _navMeshAgent.isStopped = true;
            }
            return;
        }

        // 넉백이 끝났으면 NavMeshAgent 다시 활성화
        if (_navMeshAgent != null && _navMeshAgent.isStopped && _isMovingToTarget)
        {
            _navMeshAgent.isStopped = false;
        }

        // 이동 중이면 목표 위치로 이동
        if (_isMovingToTarget)
        {
            // 경로 계산이 끝났고, 목적지에 충분히 근접하면 정지
            if (!_navMeshAgent.pathPending)
            {
                if (_navMeshAgent.remainingDistance <= _navMeshAgent.stoppingDistance)
                {
                    if (!_navMeshAgent.hasPath || _navMeshAgent.velocity.sqrMagnitude <= 0.001f)
                    {
                        Stop();
                        return;
                    }
                }
            }

            // NavMeshAgent의 desiredVelocity를 Rigidbody2D에 직접 적용
            // 이렇게 하면 플레이어가 밀어낸 힘을 무시하고 AI 이동이 우선됩니다
            if (_rigidbody != null && _navMeshAgent.desiredVelocity.sqrMagnitude > 0.01f)
            {
                Vector2 desiredVel = new Vector2(_navMeshAgent.desiredVelocity.x, _navMeshAgent.desiredVelocity.y);
                _rigidbody.velocity = desiredVel;
            }

            // 애니메이션 방향용
            Vector3 desired = _navMeshAgent.desiredVelocity;
            _targetDirection = new Vector2(desired.x, desired.y).normalized;
        }
        else
        {
            // 이동 중이 아니면 Rigidbody2D 속도도 0으로 설정
            if (_rigidbody != null)
            {
                _rigidbody.velocity = Vector2.zero;
            }
            _targetDirection = Vector2.zero;
        }
    }

    /// <summary>
    /// 속도를 초기화합니다.
    /// </summary>
    public void ResetVelocity()
    {
        if (_navMeshAgent != null)
        {
            _navMeshAgent.velocity = Vector3.zero;
        }
        if (_rigidbody != null)
        {
            _rigidbody.velocity = Vector2.zero;
        }
    }
    #endregion

    #region Public Getters
    public Vector2 GetVelocity() => _navMeshAgent != null ? (Vector2)_navMeshAgent.velocity : Vector2.zero;
    public float GetMoveSpeed() => _navMeshAgent != null ? _navMeshAgent.speed : (_enemyData != null ? _enemyData.moveSpeed : 0f);
    #endregion
}

