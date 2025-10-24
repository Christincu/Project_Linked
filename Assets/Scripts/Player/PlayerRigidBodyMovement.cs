using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

/// <summary>
/// 플레이어의 물리 기반 이동을 처리합니다.
/// MonoBehaviour로 작동하며 네트워크 기능은 PlayerController를 통해 접근합니다.
/// </summary>
public class PlayerRigidBodyMovement : MonoBehaviour
{
    #region Constants
    private const float POSITION_INTERPOLATION_SPEED = 15f;
    #endregion

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

        // Rigidbody 컴포넌트 가져오기
        _networkRb = GetComponent<NetworkRigidbody2D>();
        
        if (_networkRb != null)
        {
            _rigidbody = _networkRb.Rigidbody;
        }
        else
        {
            _rigidbody = GetComponent<Rigidbody2D>();
            if (_rigidbody == null)
            {
                _rigidbody = gameObject.AddComponent<Rigidbody2D>();
            }
        }

        ConfigureRigidbody();
        
        Debug.Log($"[PlayerRigidBodyMovement] Initialized - Speed: {_moveSpeed}, MaxVel: {_maxVelocity}");
    }

    /// <summary>
    /// Rigidbody2D를 2D 탑다운 게임에 맞게 설정합니다.
    /// </summary>
    private void ConfigureRigidbody()
    {
        _rigidbody.freezeRotation = true;
        _rigidbody.gravityScale = 0f;
        _rigidbody.drag = 0f;
        _rigidbody.angularDrag = 0f;
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

        // 테스트 모드에서는 선택된 슬롯만 입력 받기
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
    /// 입력에 따라 플레이어를 이동시킵니다.
    /// </summary>
    public void Move()
    {
        // 초기화 전이면 실행 안 함
        if (_rigidbody == null) return;

        // 사망 상태면 이동 불가
        if (_controller != null && _controller.IsDead)
        {
            _rigidbody.velocity = Vector2.zero;
            return;
        }

        if (_inputDirection.magnitude > 0)
        {
            Vector2 targetVelocity = _inputDirection * _moveSpeed;
            
            if (_acceleration >= 1f)
            {
                _rigidbody.velocity = targetVelocity;
            }
            else
            {
                _rigidbody.velocity = Vector2.Lerp(_rigidbody.velocity, targetVelocity, _acceleration);
            }
        }
        else
        {
            _rigidbody.velocity = Vector2.zero;
        }
        
        LimitSpeed();
    }

    /// <summary>
    /// 최대 속도를 제한합니다.
    /// </summary>
    private void LimitSpeed()
    {
        if (_rigidbody == null) return;
        
        if (_rigidbody.velocity.magnitude > _maxVelocity)
        {
            _rigidbody.velocity = _rigidbody.velocity.normalized * _maxVelocity;
        }
    }

    /// <summary>
    /// NetworkRigidbody2D가 없을 때 위치를 보간합니다.
    /// </summary>
    public void InterpolatePosition(Vector3 networkedPosition, bool hasInputAuthority)
    {
        if (_networkRb == null && !hasInputAuthority)
        {
            float interpolationRatio = Time.deltaTime * POSITION_INTERPOLATION_SPEED;
            transform.position = Vector3.Lerp(
                transform.position,
                networkedPosition,
                interpolationRatio
            );
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

