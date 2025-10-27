using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

/// <summary>
/// 플레이어의 물리 기반 이동을 처리합니다. (NetworkRigidbody2D 필수)
/// </summary>
public class PlayerRigidBodyMovement : MonoBehaviour
{
    #region Private Fields
    private NetworkRigidbody2D _networkRb;
    private Rigidbody2D _rigidbody;
    private PlayerController _controller;
    private Vector2 _inputDirection;

    // 이동 설정
    private float _moveSpeed;
    private float _maxVelocity;
    private float _acceleration;
    #endregion

    #region Properties
    public NetworkRigidbody2D NetworkRigidbody2D => _networkRb;
    public Rigidbody2D Rigidbody => _rigidbody;
    public Vector2 InputDirection => _inputDirection;
    #endregion

    #region Initialization
    /// <summary>
    /// 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController controller, InitialPlayerData data)
    {
        _controller = controller;

        if (data != null)
        {
            _moveSpeed = data.MoveSpeed;
            _maxVelocity = data.MaxVelocity;
            _acceleration = data.Acceleration;
        }

        // NetworkRigidbody2D 컴포넌트 가져오기 (필수)
        _networkRb = GetComponent<NetworkRigidbody2D>();

        if (_networkRb != null)
        {
            _rigidbody = _networkRb.Rigidbody;
        }
        else
        {
            // NetworkRigidbody2D가 없으면 경고 후 Rigidbody만 사용 (Fusion 동기화는 안 됨)
            Debug.LogError("[PlayerRigidBodyMovement] NetworkRigidbody2D is missing! Movement will not be networked.");
            _rigidbody = GetComponent<Rigidbody2D>() ?? gameObject.AddComponent<Rigidbody2D>();
        }

        ConfigureRigidbody();

        Debug.Log($"[PlayerRigidBodyMovement] Initialized - Speed: {_moveSpeed}, MaxVel: {_maxVelocity}");
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

        // NetworkRigidbody2D + Rigidbody2D.Interpolate 함께 사용
        _rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
        _rigidbody.sleepMode = RigidbodySleepMode2D.NeverSleep;
    }
    #endregion

    #region Movement
    /// <summary>
    /// 입력을 처리하여 이동 방향을 결정합니다.
    /// </summary>
    public void ProcessInput(InputData? data, bool isTestMode, int playerSlot)
    {
        if (!data.HasValue)
        {
            _inputDirection = Vector2.zero;
            return;
        }

        InputData inputData = data.Value;

        // 테스트 모드 슬롯 확인
        if (isTestMode && inputData.ControlledSlot != playerSlot)
        {
            _inputDirection = Vector2.zero;
            return;
        }

        int x = 0;
        int y = 0;

        if (inputData.GetButton(InputButton.LEFT)) x -= 1;
        if (inputData.GetButton(InputButton.RIGHT)) x += 1;
        if (inputData.GetButton(InputButton.DOWN)) y -= 1;
        if (inputData.GetButton(InputButton.UP)) y += 1;

        _inputDirection = new Vector2(x, y).normalized;
    }

    /// <summary>
    /// 입력에 따라 플레이어를 이동시킵니다. (FixedUpdateNetwork에서 호출)
    /// 서버(State Authority)에서만 물리를 제어합니다.
    /// </summary>
    public void Move()
    {
        if (_rigidbody == null || _controller == null) return;

        // 사망 상태면 이동 불가
        if (_controller.IsDead)
        {
            _rigidbody.velocity = Vector2.zero;
            return;
        }

        // 틱 시간 가져오기
        float deltaTime = _controller.Runner != null ? _controller.Runner.DeltaTime : Time.fixedDeltaTime;

        if (_inputDirection.magnitude > 0)
        {
            // 목표 속도 계산
            Vector2 targetVelocity = _inputDirection * _moveSpeed;

            // 가속도 적용 (Lerp 방식)
            if (_acceleration >= 1f)
            {
                // 즉시 이동
                _rigidbody.velocity = targetVelocity;
            }
            else
            {
                // 부드러운 가속
                float lerpRatio = _acceleration * deltaTime * 10f;
                _rigidbody.velocity = Vector2.Lerp(_rigidbody.velocity, targetVelocity, lerpRatio);
            }
        }
        else
        {
            // 입력 없으면 즉시 정지
            _rigidbody.velocity = Vector2.zero;
        }

        LimitSpeed();
    }

    /// <summary>
    /// 최대 속도를 제한합니다.
    /// </summary>
    private void LimitSpeed()
    {
        if (_rigidbody == null || _maxVelocity <= 0) return;

        if (_rigidbody.velocity.magnitude > _maxVelocity)
        {
            _rigidbody.velocity = _rigidbody.velocity.normalized * _maxVelocity;
        }
    }

    /// <summary>
    /// 리스폰 시 속도를 초기화합니다.
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
    public float GetMoveSpeed() => _moveSpeed;
    public bool HasNetworkRigidbody() => _networkRb != null;
    #endregion
}