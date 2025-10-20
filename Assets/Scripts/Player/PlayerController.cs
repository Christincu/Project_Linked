using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion; 

public class PlayerController : NetworkBehaviour, IPlayerLeft
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("References")]
    // 캐릭터 프리팹 인덱스
    private int _characterIndex = 0; 
    // 캐릭터 뷰 오브젝트 (스프라이트/애니메이터 포함)
    private GameObject _viewObj; 
    private Animator _animator;
    private Rigidbody2D _rigidbody;
    
    // 네트워크 동기화되는 위치와 속도
    [Networked] public Vector2 NetworkedPosition { get; set; }
    [Networked] public Vector2 NetworkedVelocity { get; set; }
    // 네트워크 동기화되는 애니메이션 상태
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
        _rigidbody = GetComponent<Rigidbody2D>();
        if (_rigidbody == null)
        {
            _rigidbody = gameObject.AddComponent<Rigidbody2D>();
        }

        // Rigidbody 설정: 2D 탑다운 게임 이동에 필수
        _rigidbody.freezeRotation = true;
        _rigidbody.gravityScale = 0f;
        _rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate; // 부드러운 이동
        
        // 네트워크 물리 설정 - InputAuthority만 물리 제어
        if (!Object.HasInputAuthority)
        {
            _rigidbody.isKinematic = true; // 다른 플레이어는 kinematic으로 설정
        }

        // 변경 감지기 초기화
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        // 실제 이동 판별 로직을 위한 초기 위치 설정
        _previousPosition = transform.position;
        
        // 서버/호스트에서만 초기값 설정
        if (Object.HasStateAuthority)
        {
            NetworkedPosition = transform.position;
            NetworkedVelocity = Vector2.zero;
            ScaleX = 1f;
            AnimationState = "idle";
        }

        // 테스트 모드 캐시
        _isTestMode = FindObjectOfType<TestGameManager>() != null;

        // 동기화된 캐릭터 인덱스로 로컬 뷰 생성 시도
        TryCreateView();
    }

    public void PlayerLeft(PlayerRef player)
    {
        if(player == Object.InputAuthority)
        {
            Runner.Despawn(Object);
        }
    }
    
    // 캐릭터 인덱스를 설정하고(네트워크 동기화), 뷰 오브젝트 생성
    public void SetCharacterIndex(int characterIndex)
    {
        CharacterIndex = characterIndex; // 네트워크 변수로 반영
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
        // Fusion 네트워크 입력 사용 (테스트 씬이 아닐 땐 슬롯 필터 무시)
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

        // 입력 기반 이동 처리 (로컬 예측/서버 권위 모두 수행)
        Move();

        // 권위 측에서만 네트워크 상태 갱신
        if (Object.HasStateAuthority)
        {
            NetworkedPosition = transform.position;
            NetworkedVelocity = _rigidbody.velocity;
            UpdateAnimation();
        }

        // 원격 플레이어 보간 이동 (InputAuthority가 없는 뷰)
        if (!Object.HasInputAuthority)
        {
            _rigidbody.MovePosition(NetworkedPosition);
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
        
        // InputAuthority가 없는 경우 위치 보간 (부드러운 이동)
        if (!Object.HasInputAuthority)
        {
            // Rigidbody2D의 MovePosition이 이미 보간을 처리하므로 추가 작업 불필요
            // 하지만 애니메이션을 위해 속도 정보 사용
            if (_animator != null && _rigidbody != null)
            {
                // 원격 플레이어의 움직임 감지를 위해 velocity 확인
                Vector2 velocity = NetworkedVelocity;
                if (velocity.magnitude > 0.1f)
                {
                    // 이동 중인 경우 애니메이션 업데이트
                    UpdateRemoteAnimation(velocity);
                }
            }
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
        // Rigidbody.velocity 직접 설정으로 즉발적인 이동 구현
        _rigidbody.velocity = _inputDirection * moveSpeed;
        
        // 이동 입력이 있을 경우, 마지막 이동 방향 업데이트
        if (_inputDirection.magnitude > 0)
        {
            _lastMoveDirection = _inputDirection;
        }
    }

    private void UpdateAnimation()
    {
        if (_animator == null) return;

        // 이전 위치와 현재 위치를 비교하여 물리적으로 이동했는지 확인
        Vector2 currentPosition = transform.position;
        float distanceMoved = Vector2.Distance(_previousPosition, currentPosition);
        
        // 임계값(0.001f) 이상 움직였는지 확인하여 실제 움직임 여부 판단
        bool isActuallyMoving = distanceMoved > 0.001f;

        if (!isActuallyMoving)
        {
            // idle 애니메이션
            AnimationState = "idle";
        }
        else
        {
            // 이동 중: 애니메이션 방향 결정
            Vector2 actualVelocity = _rigidbody.velocity;

            // 수직 움직임 우선 판별
            if (Mathf.Abs(actualVelocity.y) > Mathf.Abs(actualVelocity.x))
            {
                // 상/하 애니메이션
                if (actualVelocity.y > 0)
                {
                    AnimationState = "up";
                }
                else
                {
                    AnimationState = "down";
                }
            }
            else
            {
                // 좌/우 애니메이션
                AnimationState = "horizontal";
                
                // 좌우 반전 (플립)
                if (actualVelocity.x < 0)
                {
                    ScaleX = 1f;
                }
                else if (actualVelocity.x > 0)
                {
                    ScaleX = -1f;
                }
            }
        }
    }

    public NetWorkInputData GetNetworkInputData()
    {
        NetWorkInputData data = new NetWorkInputData();
        data.direction = NetworkedVelocity;

        return data;
    }
}