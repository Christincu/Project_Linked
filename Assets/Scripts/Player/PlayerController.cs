using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

public class PlayerController : NetworkBehaviour, IPlayerLeft
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float maxVelocity = 8f;
    [SerializeField] private float acceleration = 1f;  // 1이면 즉시 반응

    [Header("References")]
    // 캐릭터 프리팹 인덱스
    private int _characterIndex = 0;
    // 캐릭터 뷰 오브젝트 (스프라이트/애니메이터 포함)
    private GameObject _viewObj;
    private Animator _animator;
    private NetworkRigidbody2D _networkRb;
    private Rigidbody2D _rigidbody;

    // 네트워크 동기화되는 애니메이션 상태와 velocity
    [Networked] public Vector3 NetworkedPosition { get; set; }
    [Networked] public Vector2 NetworkedVelocity { get; set; }
    [Networked] public NetworkString<_16> AnimationState { get; set; }
    // 네트워크 동기화되는 스케일 (좌우 반전용)
    [Networked] public float ScaleX { get; set; }

    // 현재 입력 방향 (normalized)
    private Vector2 _inputDirection;
    // 정지 시 캐릭터가 바라볼 방향 유지를 위한 변수
    private Vector2 _lastMoveDirection;
    // 실제 이동 여부 판별을 위해 이전 FixedUpdate의 위치를 저장
    private Vector2 _previousPosition;

    // 변경 감지기
    private ChangeDetector _changeDetector;
    // 마지막 애니메이션 상태 (중복 재생 방지)
    private string _lastAnimationState = "";

    // 테스트에서 어떤 슬롯의 입력을 받을지 결정 (0: 첫째, 1: 둘째)
    [Networked] public int PlayerSlot { get; set; }
    // 캐릭터 인덱스를 네트워크 동기화하여 모든 피어에서 동일한 뷰를 생성
    [Networked] public int CharacterIndex { get; set; }

    // 테스트 모드 여부 (TestGameManager 존재 여부로 판단)
    private bool _isTestMode;

    // 네트워크 오브젝트 생성 시 호출
    public override void Spawned()
    {
        // NetworkRigidbody2D 가져오기 (자동 동기화)
        _networkRb = GetComponent<NetworkRigidbody2D>();
        if (_networkRb != null)
        {
            _rigidbody = _networkRb.Rigidbody;
        }
        else
        {
            // NetworkRigidbody2D가 없으면 일반 Rigidbody2D 사용
            _rigidbody = GetComponent<Rigidbody2D>();
            if (_rigidbody == null)
            {
                _rigidbody = gameObject.AddComponent<Rigidbody2D>();
            }
        }

        // Rigidbody 설정: 2D 탑다운 게임 이동에 필수
        _rigidbody.freezeRotation = true;
        _rigidbody.gravityScale = 0f;
        _rigidbody.drag = 0f;
        _rigidbody.angularDrag = 0f;
        _rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
        _rigidbody.sleepMode = RigidbodySleepMode2D.NeverSleep;

        // 변경 감지기 초기화
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        // 실제 이동 판별 로직을 위한 초기 위치 설정
        _previousPosition = transform.position;

        // 서버/호스트에서만 초기값 설정
        if (Object.HasStateAuthority)
        {
            ScaleX = 1f;
            AnimationState = "idle";
        }

        // 클라이언트 예측을 위한 설정 (부드러운 움직임)
        if (Object.HasInputAuthority && _networkRb != null)
        {
            Runner.SetPlayerAlwaysInterested(Object.InputAuthority, Object, true);
        }

        // 테스트 모드 캐시
        _isTestMode = FindObjectOfType<TestGameManager>() != null;

        // 동기화된 캐릭터 인덱스로 로컬 뷰 생성 시도
        TryCreateView();

        Debug.Log($"[Spawned] Player initialized - HasInputAuth: {Object.HasInputAuthority}, HasStateAuth: {Object.HasStateAuthority}, IsInSimulation: {Object.IsInSimulation}, NetworkRb: {_networkRb != null}");
    }

    public void PlayerLeft(PlayerRef player)
    {
        if (player == Object.InputAuthority)
        {
            Runner.Despawn(Object);
        }
    }

    // 캐릭터 인덱스를 설정하고(네트워크 동기화), 뷰 오브젝트 생성
    public void SetCharacterIndex(int characterIndex)
    {
        CharacterIndex = characterIndex;
        _characterIndex = characterIndex;
        TryCreateView();
    }

    private void TryCreateView()
    {
        if (_viewObj != null) return;
        if (GameDataManager.Instance == null) return;

        int index = CharacterIndex >= 0 ? CharacterIndex : _characterIndex;
        var data = GameDataManager.Instance.CharacterService.GetCharacter(index);
        if (data != null && data.viewObj != null)
        {
            GameObject instance = Instantiate(data.viewObj, transform);
            _viewObj = instance.gameObject;
            _animator = _viewObj.GetComponent<Animator>();
            Debug.Log($"Character view created: {index}");
        }
    }

    // Fusion 네트워크 입력 처리
    public override void FixedUpdateNetwork()
    {
        // 모든 피어가 GetInput으로 입력 받음 (Fusion이 자동으로 라우팅)
        if (GetInput<InputData>(out var data) && (!_isTestMode || data.ControlledSlot == PlayerSlot))
        {
            int x = 0;
            int y = 0;
            if (data.GetButton(InputButton.LEFT)) x -= 1;
            if (data.GetButton(InputButton.RIGHT)) x += 1;
            if (data.GetButton(InputButton.DOWN)) y -= 1;
            if (data.GetButton(InputButton.UP)) y += 1;
            _inputDirection = new Vector2(x, y).normalized;
        }
        else
        {
            _inputDirection = Vector2.zero;
        }

        // 항상 이동 처리
        Move();
        
        // 디버그: 입력 및 velocity 확인
        if (_inputDirection.magnitude > 0)
        {
            Debug.Log($"[Tick {Runner.Tick}] InputAuth: {Object.HasInputAuthority}, StateAuth: {Object.HasStateAuthority}, Input: {_inputDirection}, Velocity: {_rigidbody.velocity}");
        }
        
        // StateAuthority(서버)에서만 애니메이션 상태 및 velocity 업데이트
        if (Object.HasStateAuthority)
        {
            UpdateAnimation();
            NetworkedVelocity = _rigidbody.velocity;  // velocity 동기화
        }

        // NetworkedPosition은 StateAuthority(서버)에서만 업데이트 (NetworkRigidbody2D 없을 때 대비)
        if (Object.HasStateAuthority && _networkRb == null)
        {
            NetworkedPosition = transform.position;
        }

        // FixedUpdate 끝에서 현재 위치를 저장
        _previousPosition = transform.position;
    }

    // 렌더 업데이트 (애니메이션 동기화)
    public override void Render()
    {
        // 변경 감지
        foreach (var change in _changeDetector.DetectChanges(this))
        {
            switch (change)
            {
                case nameof(AnimationState):
                    // 애니메이션 상태가 변경되었을 때만 재생
                    PlayAnimation(AnimationState.ToString());
                    break;
                case nameof(ScaleX):
                    // 스케일 변경
                    UpdateScale();
                    break;
                case nameof(CharacterIndex):
                    // 캐릭터 인덱스 변경 시 뷰 생성 시도
                    TryCreateView();
                    break;
            }
        }

        // NetworkRigidbody2D가 없을 때만 수동 보간 (NetworkRigidbody2D는 자동 동기화)
        if (_networkRb == null && !Object.HasInputAuthority)
        {
            float interpolationRatio = Time.deltaTime * 15f;
            transform.position = Vector3.Lerp(
                transform.position,
                NetworkedPosition,
                interpolationRatio
            );
        }
    }

    // 애니메이션 재생 (중복 재생 방지)
    private void PlayAnimation(string stateName)
    {
        if (_animator != null && !string.IsNullOrEmpty(stateName) && _lastAnimationState != stateName)
        {
            _animator.Play(stateName);
            _lastAnimationState = stateName;
        }
    }

    // 스케일 업데이트
    private void UpdateScale()
    {
        if (_viewObj != null)
        {
            Vector3 scale = _viewObj.transform.localScale;
            scale.x = ScaleX;
            _viewObj.transform.localScale = scale;
        }
    }

    // 원격 플레이어 애니메이션 업데이트 (보간용)
    private void UpdateRemoteAnimation(Vector2 velocity)
    {
        string currentState = AnimationState.ToString();

        // 현재 애니메이션 상태가 idle이 아니라면 이미 올바르게 설정되어 있음
        if (!string.IsNullOrEmpty(currentState) && currentState != "idle")
        {
            PlayAnimation(currentState);
        }
    }

    private void Move()
    {
        if (_inputDirection.magnitude > 0)
        {
            // 즉시 반응 또는 약간의 가속감 (acceleration 조절 가능)
            Vector2 targetVelocity = _inputDirection * moveSpeed;
            
            if (acceleration >= 1f)
            {
                // 즉시 반응
                _rigidbody.velocity = targetVelocity;
            }
            else
            {
                // 부드러운 가속 (선택적)
                _rigidbody.velocity = Vector2.Lerp(_rigidbody.velocity, targetVelocity, acceleration);
            }
            
            _lastMoveDirection = _inputDirection;
        }
        else
        {
            // 즉시 정지 (사용자 요구사항)
            _rigidbody.velocity = Vector2.zero;
        }
        
        // 속도 제한
        LimitSpeed();
    }

    private void LimitSpeed()
    {
        // 최대 속도 제한
        if (_rigidbody.velocity.magnitude > maxVelocity)
        {
            _rigidbody.velocity = _rigidbody.velocity.normalized * maxVelocity;
        }
    }

    private void UpdateAnimation()
    {
        if (_animator == null) return;

        // velocity 기반으로 애니메이션 결정 (간단하고 확실함)
        Vector2 vel = _rigidbody.velocity;

        if (vel.magnitude < 0.01f)
        {
            // 거의 정지 상태
            AnimationState = "idle";
        }
        else
        {
            // 이동 중: 수직/수평 판별
            if (Mathf.Abs(vel.y) > Mathf.Abs(vel.x))
            {
                // 상하 이동
                AnimationState = vel.y > 0 ? "up" : "down";
            }
            else
            {
                // 좌우 이동
                AnimationState = "horizontal";
                ScaleX = vel.x < 0 ? 1f : -1f;
            }
        }
    }
}