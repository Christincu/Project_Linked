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
    private NetworkRigidbody2D _networkRb;
    private Rigidbody2D _rigidbody;
    private PlayerController _controller;
    private Vector2 _inputDirection;
    private float _moveSpeed;
    private float _maxVelocity;
    private float _acceleration;

    public NetworkRigidbody2D NetworkRigidbody2D => _networkRb;
    public Rigidbody2D Rigidbody => _rigidbody;
    public Vector2 InputDirection => _inputDirection;

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

        _networkRb = GetComponent<NetworkRigidbody2D>();
        _rigidbody = GetComponent<Rigidbody2D>();

        ConfigureRigidbody();
        _rigidbody.velocity = Vector2.zero;
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

        // NetworkRigidbody2D가 Interpolation을 처리하므로 Unity Interpolation은 끔
        _rigidbody.interpolation = RigidbodyInterpolation2D.None;
        _rigidbody.sleepMode = RigidbodySleepMode2D.NeverSleep;
    }

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
            Vector2 targetVelocity = _inputDirection;
            if (_acceleration >= 1f)
            {
                _rigidbody.velocity = targetVelocity;
            }
            else
            {
                float lerpRatio = _acceleration * deltaTime * 10f;
                _rigidbody.velocity = Vector2.Lerp(_rigidbody.velocity, targetVelocity, lerpRatio);
            }
        }
        else
        {
            _rigidbody.velocity = Vector2.zero;
        }
    }
}