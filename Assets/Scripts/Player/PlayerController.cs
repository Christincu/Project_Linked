using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion; 

public class PlayerController : MonoBehaviour
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
    
    // 현재 입력 방향 (normalized)
    private Vector2 _inputDirection; 
    // 정지 시 캐릭터가 바라볼 방향 유지를 위한 변수
    private Vector2 _lastMoveDirection; 
    // 실제 이동 여부 판별을 위해 이전 FixedUpdate의 위치를 저장
    private Vector2 _previousPosition; 

    private void Start()
    {
        _rigidbody = GetComponent<Rigidbody2D>();
        if (_rigidbody == null)
        {
            _rigidbody = gameObject.AddComponent<Rigidbody2D>();
        }

        // Rigidbody 설정: 2D 탑다운 게임 이동에 필수
        _rigidbody.freezeRotation = true;
        _rigidbody.gravityScale = 0f;

        // 캐릭터 뷰(외형) 오브젝트 생성 및 Animator 참조
        GameObject instance = Instantiate(GameDataManager.Instance.CharacterService.GetCharacter(_characterIndex).viewObj, transform);
        _viewObj = instance.gameObject;
        _animator = _viewObj.GetComponent<Animator>();

        // 실제 이동 판별 로직을 위한 초기 위치 설정
        _previousPosition = transform.position; 
    }

    private void Update()
    {
        // 입력 처리 (FixedUpdate에서 물리 연산에 사용)
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical"); 
        
        _inputDirection = new Vector2(horizontal, vertical).normalized;
    }

    private void FixedUpdate()
    {
        Move();
        UpdateAnimation();
        
        // FixedUpdate 끝에서 현재 위치를 저장하여 다음 프레임에서 실제 이동 여부(벽 충돌 여부)를 판별
        _previousPosition = transform.position;
    }

    private void Move()
    {
        // Rigidbody.velocity 직접 설정으로 즉발적인 이동 구현
        // 충돌해도 velocity 값은 설정한 대로 유지됨
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

        //이전 위치와 현재 위치를 비교하여 물리적으로 이동했는지 확인 (벽 충돌 체크)
        Vector2 currentPosition = transform.position;
        float distanceMoved = Vector2.Distance(_previousPosition, currentPosition);
        
        // 임계값(0.001f) 이상 움직였는지 확인하여 실제 움직임 여부 판단
        bool isActuallyMoving = distanceMoved > 0.001f;

        if (!isActuallyMoving)
        {
            // 입력이 있었더라도 벽에 막혔거나 입력이 없을 경우: idle 애니메이션 재생
            _animator.Play("idle");
        }
        else
        {
            // 이동 중: 애니메이션 방향 결정을 위해 Rigidbody.velocity 사용
            Vector2 actualVelocity = _rigidbody.velocity; 

            // 수직 움직임 우선 판별
            if (Mathf.Abs(actualVelocity.y) > Mathf.Abs(actualVelocity.x))
            {
                // 상/하 애니메이션
                if (actualVelocity.y > 0)
                {
                    _animator.Play("up");
                }
                else
                {
                    _animator.Play("down");
                }
            }
            else
            {
                // 좌/우 애니메이션
                _animator.Play("horizontal");
                
                // 뷰 오브젝트의 localScale을 조정하여 스프라이트 좌우 반전
                if (actualVelocity.x < 0)
                {
                    transform.localScale = new Vector3(1, 1, 1); // 오른쪽
                }
                else if (actualVelocity.x > 0)
                {
                    transform.localScale = new Vector3(-1, 1, 1); // 왼쪽
                }
            }
        }
    }
}