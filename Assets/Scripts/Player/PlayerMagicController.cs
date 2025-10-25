using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어의 마법 공격을 담당하는 컨트롤러
/// 마법 UI 표시, 마법 발사, 쿨다운, 마나 관리 등을 처리합니다.
/// </summary>
public class PlayerMagicController : MonoBehaviour
{
    #region Serialized Fields
    [Header("Prefabs")]
    [SerializeField] private GameObject magicProjectilePrefab;
    #endregion

    #region Private Fields
    private PlayerController _controller;
    private MagicData _currentMagicData;
    private bool _isInitialized = false; // 초기화 완료 플래그
    
    // Magic UI References (from PlayerController)
    private GameObject _magicViewObj; // 실제로 움직이는 뷰 오브젝트 (IdleFirst, IdleSecond, Active의 부모)
    private GameObject _magicAnchor;
    private GameObject _magicIdleFirstFloor;
    private GameObject _magicIdleSecondFloor;
    private GameObject _magicActiveFloor;
    
    private SpriteRenderer _idleFirstFloorRenderer;
    private SpriteRenderer _idleSecondFloorRenderer;
    private SpriteRenderer _activeFloorRenderer;
    
    private bool _isPlayerColliding = false; // 다른 플레이어와 충돌 중인지
    private PlayerController _collidingPlayer = null; // 충돌 중인 플레이어
    private MagicData _collidingPlayerMagicData = null; // 충돌 중인 플레이어의 마법 데이터
    
    private bool _magicUIToggleActive = false; // 토글 상태: 마법 UI가 활성화되어 있는지
    private bool _previousLeftMouseButton = false; // 이전 프레임의 좌클릭 상태
    private bool _previousRightMouseButton = false; // 이전 프레임의 우클릭 상태
    private float _magicActivationTime = 0f; // 마법을 활성화한 시간 (Time.time)
    #endregion

    #region Properties
    /// <summary>
    /// 연결된 PlayerController
    /// </summary>
    public PlayerController Controller => _controller;

    /// <summary>
    /// 현재 마법 데이터
    /// </summary>
    public MagicData CurrentMagicData => _currentMagicData;

    /// <summary>
    /// 마법을 시전할 수 있는지 확인
    /// </summary>
    public bool CanCastMagic
    {
        get
        {
            if (_controller == null || _currentMagicData == null) return false;
            if (_controller.MagicCooldownTimer.IsRunning) return false;
            if (_controller.IsDead) return false;
            return true;
        }
    }

    /// <summary>
    /// 쿨다운 중인지 확인
    /// </summary>
    public bool IsOnCooldown => _controller != null && _controller.MagicCooldownTimer.IsRunning;

    /// <summary>
    /// 남은 쿨다운 시간
    /// </summary>
    public float RemainingCooldown => _controller?.MagicCooldownTimer.RemainingTime(_controller.Runner) ?? 0f;
    
    /// <summary>
    /// 마법을 활성화한 시간 (Time.time)
    /// </summary>
    public float MagicActivationTime => _magicActivationTime;
    
    /// <summary>
    /// 마법 UI가 활성화되어 있는지
    /// </summary>
    public bool IsMagicUIActive => _magicUIToggleActive;
    
    /// <summary>
    /// 마법 ViewObj (다른 플레이어의 위치 참조용)
    /// </summary>
    public GameObject MagicViewObj => _magicViewObj;
    #endregion

    #region Events
    public System.Action<Vector3, Vector3> OnMagicCast; // (position, direction)
    public System.Action OnCooldownStarted;
    public System.Action OnCooldownEnded;
    #endregion

    #region Initialization
    /// <summary>
    /// PlayerController에서 호출하여 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController controller)
    {
        _controller = controller;
        
        Debug.Log($"[PlayerMagicController] Initialize called - Controller: {_controller != null}");
    }
    
    /// <summary>
    /// 매 프레임 호출되어 충돌 상태를 업데이트합니다.
    /// </summary>
    void Update()
    {
        if (!_isInitialized || _controller == null) return;
        
        // 충돌 상태 디버그
        if (_isPlayerColliding && _collidingPlayer != null)
        {
            // 마법 UI가 활성화되어 있을 때 위치 업데이트
            if (_magicUIToggleActive)
            {
                UpdateMagicViewObjPosition(_controller.transform.position);
                UpdateIdleSecondFloor();
            }
        }
    }
    
    /// <summary>
    /// Magic UI 오브젝트 레퍼런스를 설정합니다. (PlayerController에서 호출)
    /// </summary>
    public void SetMagicUIReferences(GameObject viewObj, GameObject anchor, GameObject idleFirst, GameObject idleSecond, GameObject active)
    {
        _magicViewObj = viewObj;
        _magicAnchor = anchor;
        _magicIdleFirstFloor = idleFirst;
        _magicIdleSecondFloor = idleSecond;
        _magicActiveFloor = active;
        
        // 디버깅: 레퍼런스 확인
        Debug.Log($"[PlayerMagicController] Magic UI References Set - ViewObj: {_magicViewObj != null}, Anchor: {_magicAnchor != null}, " +
                  $"IdleFirst: {_magicIdleFirstFloor != null}, IdleSecond: {_magicIdleSecondFloor != null}, " +
                  $"Active: {_magicActiveFloor != null}");
        
        // SpriteRenderer 가져오기
        if (_magicIdleFirstFloor != null)
            _idleFirstFloorRenderer = _magicIdleFirstFloor.GetComponent<SpriteRenderer>();
        if (_magicIdleSecondFloor != null)
            _idleSecondFloorRenderer = _magicIdleSecondFloor.GetComponent<SpriteRenderer>();
        if (_magicActiveFloor != null)
            _activeFloorRenderer = _magicActiveFloor.GetComponent<SpriteRenderer>();
        
        // 캐릭터 인덱스에 따른 MagicData 로드
        LoadMagicData();
        
        // Magic UI 초기화
        InitializeMagicUI();
        
        _isInitialized = true; // 초기화 완료
        Debug.Log($"[PlayerMagicController] Initialization complete - MagicData: {_currentMagicData != null}");
    }
    
    /// <summary>
    /// 캐릭터 인덱스에 따라 MagicData를 로드합니다.
    /// </summary>
    private void LoadMagicData()
    {
        if (_controller == null || GameDataManager.Instance == null) return;
        
        // 캐릭터 인덱스로 마법 데이터 가져오기
        int characterIndex = _controller.CharacterIndex;
        _currentMagicData = GameDataManager.Instance.MagicService.GetMagic(characterIndex);
        
        if (_currentMagicData == null)
        {
            Debug.LogWarning($"[PlayerMagicController] MagicData not found for character index: {characterIndex}");
        }
    }
    
    /// <summary>
    /// Magic UI를 초기화합니다.
    /// </summary>
    private void InitializeMagicUI()
    {
        if (_currentMagicData == null) return;
        
        // _magicViewObj 초기화 및 구조 설정
        if (_magicViewObj != null)
        {
            // _magicViewObj의 초기 위치를 (0, 0, 0)으로 설정
            _magicViewObj.transform.localPosition = Vector3.zero;
            
            // IdleFirst, IdleSecond, Active를 _magicViewObj의 자식으로 설정
            if (_magicIdleFirstFloor != null && _magicIdleFirstFloor.transform.parent != _magicViewObj.transform)
            {
                _magicIdleFirstFloor.transform.SetParent(_magicViewObj.transform, false);
            }
            if (_magicIdleSecondFloor != null && _magicIdleSecondFloor.transform.parent != _magicViewObj.transform)
            {
                _magicIdleSecondFloor.transform.SetParent(_magicViewObj.transform, false);
            }
            if (_magicActiveFloor != null && _magicActiveFloor.transform.parent != _magicViewObj.transform)
            {
                _magicActiveFloor.transform.SetParent(_magicViewObj.transform, false);
            }
        }
        
        // 스프라이트 설정
        if (_idleFirstFloorRenderer != null && _currentMagicData.magicIdleSprite != null)
        {
            _idleFirstFloorRenderer.sprite = _currentMagicData.magicIdleSprite;
        }
        
        if (_activeFloorRenderer != null && _currentMagicData.magicActiveSprite != null)
        {
            _activeFloorRenderer.sprite = _currentMagicData.magicActiveSprite;
        }
        
        // _magicAnchor에 충돌 감지 스크립트 추가
        if (_magicAnchor != null)
        {
            var collision = _magicAnchor.GetComponent<MagicAnchorCollision>();
            if (collision == null)
            {
                collision = _magicAnchor.AddComponent<MagicAnchorCollision>();
            }
            collision.Initialize(this);
        }
        
        // 초기 상태: 모두 비활성화
        if (_magicViewObj != null)
            _magicViewObj.SetActive(false);
        if (_magicAnchor != null)
            _magicAnchor.SetActive(false);
        if (_magicIdleFirstFloor != null)
            _magicIdleFirstFloor.SetActive(false);
        if (_magicIdleSecondFloor != null)
            _magicIdleSecondFloor.SetActive(false);
        if (_magicActiveFloor != null)
            _magicActiveFloor.SetActive(false);
    }
    #endregion

    #region Magic Casting
    /// <summary>
    /// 마법을 시전합니다.
    /// </summary>
    /// <param name="targetPosition">목표 위치 (월드 좌표)</param>
    public void CastMagic(Vector3 targetPosition)
    {
        if (_controller == null || _currentMagicData == null) return;
        if (!_controller.Object.HasStateAuthority) return;

        // 시전 가능 여부 확인
        if (!CanCastMagic) return;
        if (_controller.State == null) return;

        // 쿨다운 시작
        _controller.MagicCooldownTimer = TickTimer.CreateFromSeconds(_controller.Runner, _currentMagicData.cooldown);

        // 발사 방향 계산
        Vector3 startPos = _controller.transform.position;
        Vector3 direction = (targetPosition - startPos).normalized;

        // 마법 발사체 생성
        SpawnMagicProjectile(startPos, direction);

        Debug.Log($"[PlayerMagicController] Magic cast!");

        // 이벤트 발생
        OnMagicCast?.Invoke(startPos, direction);
        OnCooldownStarted?.Invoke();
    }

    /// <summary>
    /// 마법 발사체를 생성합니다.
    /// </summary>
    private void SpawnMagicProjectile(Vector3 position, Vector3 direction)
    {
        if (magicProjectilePrefab == null)
        {
            Debug.LogWarning("[PlayerMagicController] Magic projectile prefab is null!");
            return;
        }

        if (_currentMagicData == null) return;

        // TODO: NetworkObject로 발사체 스폰
        // 현재는 로컬 오브젝트로 임시 생성
        GameObject projectile = Instantiate(magicProjectilePrefab, position, Quaternion.identity);
        
        // 발사체 초기화 (데미지, 속도, 방향 등)
        // TODO: MagicProjectile 스크립트 추가 필요
        
        // 일정 시간 후 제거
        Destroy(projectile, _currentMagicData.range / _currentMagicData.speed);
    }
    #endregion


    #region Magic UI Control
    /// <summary>
    /// 네트워크 동기화된 MagicActive 상태를 반영하여 UI를 업데이트합니다.
    /// PlayerController의 DetectNetworkChanges에서 호출됩니다.
    /// </summary>
    public void UpdateMagicUIFromNetwork(bool isActive)
    {
        if (!_isInitialized) return;
        
        Debug.Log($"[PlayerMagicController] UpdateMagicUIFromNetwork called - isActive: {isActive}");
        
        // 네트워크에서 받은 상태를 토글 상태에도 반영
        _magicUIToggleActive = isActive;
        
        if (isActive)
        {
            // 활성화 시간 기록 (네트워크 동기화 시)
            if (_magicActivationTime == 0f)
            {
                _magicActivationTime = Time.time;
                Debug.Log($"[PlayerMagicController] MagicActivationTime set from network: {_magicActivationTime}");
            }
            
            // Magic UI 활성화 (위치 업데이트 없이)
            if (_magicAnchor != null)
                _magicAnchor.SetActive(true);
            if (_magicViewObj != null)
                _magicViewObj.SetActive(true);
            if (_magicIdleFirstFloor != null)
                _magicIdleFirstFloor.SetActive(true);
            if (_magicActiveFloor != null)
                _magicActiveFloor.SetActive(true);
        }
        else
        {
            // 활성화 시간 리셋
            _magicActivationTime = 0f;
            
            // _magicViewObj를 (0, 0, 0) 위치로 이동 후 비활성화
            if (_magicViewObj != null)
            {
                _magicViewObj.transform.localPosition = Vector3.zero;
                _magicViewObj.SetActive(false);
            }
            
            // Magic UI 비활성화
            if (_magicIdleFirstFloor != null)
                _magicIdleFirstFloor.SetActive(false);
            if (_magicActiveFloor != null)
                _magicActiveFloor.SetActive(false);
            if (_magicIdleSecondFloor != null)
                _magicIdleSecondFloor.SetActive(false);
            if (_magicAnchor != null)
                _magicAnchor.SetActive(false);
        }
        
        // 충돌 상태에 따라 IdleSecondFloor 업데이트
        UpdateIdleSecondFloor();
    }
    
    /// <summary>
    /// 입력을 처리하여 마법 UI를 업데이트합니다.
    /// PlayerController에서 호출됩니다.
    /// </summary>
    public void ProcessInput(InputData inputData, PlayerController controller, bool isTestMode)
    {
        // 초기화 확인
        if (!_isInitialized) return;
        if (!controller.Object.HasInputAuthority) return;
        
        // 현재 마우스 버튼 상태
        bool currentLeftButton = inputData.GetMouseButton(InputMouseButton.LEFT);
        bool currentRightButton = inputData.GetMouseButton(InputMouseButton.RIGHT);
        
        // 클릭 감지: 이전 프레임에는 안 눌렸고 현재 프레임에 눌린 경우
        bool leftClickDown = currentLeftButton && !_previousLeftMouseButton;
        bool rightClickDown = currentRightButton && !_previousRightMouseButton;
        
        // 토글 처리: 클릭할 때마다 활성화/비활성화 전환
        if (leftClickDown || rightClickDown)
        {
            _magicUIToggleActive = !_magicUIToggleActive;
            
            if (_magicUIToggleActive)
            {
                ActivateMagicUI(controller);
            }
            else
            {
                DeactivateMagicUI(controller);
            }
        }
        
        // 이전 프레임 상태 업데이트
        _previousLeftMouseButton = currentLeftButton;
        _previousRightMouseButton = currentRightButton;
        
        // 토글 상태가 활성화되어 있으면 계속 마우스 위치 업데이트
        if (_magicUIToggleActive)
        {
            UpdateMagicAnchorPosition(inputData, controller);
        }
        
        // 충돌 상태에 따라 IdleSecondFloor 활성화
        UpdateIdleSecondFloor();
    }
    
    /// <summary>
    /// MagicAnchor의 위치를 플레이어가 바라보는 방향(좌/우)으로 업데이트합니다.
    /// _magicViewObj가 충돌 중일 경우 두 플레이어의 중간 위치로 이동합니다.
    /// </summary>
    private void UpdateMagicAnchorPosition(InputData inputData, PlayerController controller)
    {
        if (_magicAnchor == null) return;
        
        // 플레이어 위치
        Vector3 playerPos = controller.transform.position;
        
        // 일반 모드: 마우스 위치를 기반으로 앵커의 로컬 위치 업데이트
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;
        
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(inputData.MousePosition);
        mouseWorldPos.z = 0; // 2D 게임이므로 z는 0
        
        // 마우스가 플레이어 기준 좌우 어디에 있는지 확인
        float horizontalDirection = mouseWorldPos.x - playerPos.x;
        
        // MagicAnchor의 로컬 위치 가져오기
        Vector3 anchorLocalPos = _magicAnchor.transform.localPosition;
        
        // 좌우 방향만 업데이트 (위아래는 고정)
        float distance = Mathf.Abs(anchorLocalPos.x); // 기본 거리 유지
        anchorLocalPos.x = horizontalDirection >= 0 ? distance : -distance;
        
        _magicAnchor.transform.localPosition = anchorLocalPos;
        
        // _magicViewObj 위치 업데이트 (충돌 여부에 따라)
        UpdateMagicViewObjPosition(playerPos);
    }
    
    /// <summary>
    /// _magicViewObj의 위치를 업데이트합니다.
    /// 충돌 중일 경우, 나중에 활성화한 플레이어의 마법이 먼저 활성화한 플레이어의 위치로 흡수됩니다.
    /// </summary>
    private void UpdateMagicViewObjPosition(Vector3 playerPos)
    {
        if (_magicViewObj == null) return;
        
        // 다른 플레이어와 충돌 중일 경우
        if (_isPlayerColliding && _collidingPlayer != null && _collidingPlayer.MagicController != null)
        {
            // 두 플레이어 중 누가 먼저 마법을 활성화했는지 확인
            float myActivationTime = _magicActivationTime;
            float otherActivationTime = _collidingPlayer.MagicController.MagicActivationTime;
            
            bool shouldAbsorb = false;
            
            // 시간 비교
            if (otherActivationTime > 0 && myActivationTime > 0)
            {
                float timeDiff = Mathf.Abs(myActivationTime - otherActivationTime);
                
                if (timeDiff > 0.01f) // 0.01초 이상 차이나면
                {
                    if (otherActivationTime < myActivationTime)
                    {
                        // 상대방이 먼저 활성화
                        shouldAbsorb = true;
                    }
                }
                else // 거의 동시면 캐릭터 인덱스로 결정
                {
                    int myCharIndex = _controller.CharacterIndex;
                    int otherCharIndex = _collidingPlayer.CharacterIndex;
                    
                    if (myCharIndex != otherCharIndex)
                    {
                        // 캐릭터 인덱스가 큰 쪽이 흡수됨
                        if (myCharIndex > otherCharIndex)
                        {
                            shouldAbsorb = true;
                        }
                    }
                    else
                    {
                        // 캐릭터도 같으면 Object ID로 결정 (결정론적)
                        if (_controller.Object.Id.Raw > _collidingPlayer.Object.Id.Raw)
                        {
                            shouldAbsorb = true;
                        }
                    }
                }
            }
            
            if (shouldAbsorb)
            {
                Debug.Log($"[PlayerMagicController] ABSORBING - My time: {myActivationTime}, Other time: {otherActivationTime}, My char: {_controller.CharacterIndex}, Other char: {_collidingPlayer.CharacterIndex}, My ID: {_controller.Object.Id}, Other ID: {_collidingPlayer.Object.Id}");
                
                // 상대방의 MagicViewObj 위치로 이동
                if (_collidingPlayer.MagicController.MagicViewObj != null)
                {
                    Vector3 targetPos = _collidingPlayer.MagicController.MagicViewObj.transform.position;
                    _magicViewObj.transform.position = targetPos;
                }
            }
            else
            {
                // 내가 먼저 활성화했으면, 내 앵커 위치 유지
                if (_magicAnchor != null)
                {
                    _magicViewObj.transform.position = _magicAnchor.transform.position;
                }
            }
        }
        else
        {
            // 일반 모드: 앵커 위치로 이동
            if (_magicAnchor != null)
            {
                _magicViewObj.transform.position = _magicAnchor.transform.position;
            }
        }
    }
    
    /// <summary>
    /// Magic UI를 활성화합니다.
    /// </summary>
    private void ActivateMagicUI(PlayerController controller)
    {
        if (controller == null)
        {
            Debug.LogWarning("[PlayerMagicController] Cannot activate - controller is null");
            return;
        }
        
        // 토글 상태 활성화
        _magicUIToggleActive = true;
        
        // 활성화 시간 기록
        _magicActivationTime = Time.time;
        
        // 네트워크 동기화 (InputAuthority가 있는 경우 네트워크 변수 업데이트)
        if (controller.Object.HasInputAuthority && !controller.MagicActive)
        {
            controller.MagicActive = true;
            Debug.Log($"[PlayerMagicController] MagicActive set to true at time {_magicActivationTime} (InputAuth: {controller.Object.HasInputAuthority}, StateAuth: {controller.Object.HasStateAuthority})");
        }
        
        // 로컬 UI 업데이트
        if (_magicAnchor != null)
        {
            if (!_magicAnchor.activeSelf)
            {
                Debug.Log($"[PlayerMagicController] Activating Magic UI");
            }
            _magicAnchor.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[PlayerMagicController] _magicAnchor is null");
        }
        
        if (_magicViewObj != null)
            _magicViewObj.SetActive(true);
        else
            Debug.LogWarning("[PlayerMagicController] _magicViewObj is null");
        
        if (_magicIdleFirstFloor != null)
            _magicIdleFirstFloor.SetActive(true);
        else
            Debug.LogWarning("[PlayerMagicController] _magicIdleFirstFloor is null");
            
        if (_magicActiveFloor != null)
            _magicActiveFloor.SetActive(true);
        else
            Debug.LogWarning("[PlayerMagicController] _magicActiveFloor is null");
    }
    
    /// <summary>
    /// Magic UI를 비활성화합니다.
    /// _magicViewObj를 (0, 0, 0)으로 이동시킵니다.
    /// </summary>
    private void DeactivateMagicUI(PlayerController controller)
    {
        if (controller == null) return;
        
        // 토글 상태 비활성화
        _magicUIToggleActive = false;
        
        // 활성화 시간 리셋
        _magicActivationTime = 0f;
        
        // 네트워크 동기화 (InputAuthority가 있는 경우 네트워크 변수 업데이트)
        if (controller.Object.HasInputAuthority && controller.MagicActive)
        {
            controller.MagicActive = false;
        }
        
        // _magicViewObj를 (0, 0, 0) 위치로 이동 후 비활성화
        if (_magicViewObj != null)
        {
            _magicViewObj.transform.localPosition = Vector3.zero;
            _magicViewObj.SetActive(false);
        }
        
        // 로컬 UI 업데이트
        if (_magicIdleFirstFloor != null)
            _magicIdleFirstFloor.SetActive(false);
        if (_magicActiveFloor != null)
            _magicActiveFloor.SetActive(false);
        if (_magicIdleSecondFloor != null)
            _magicIdleSecondFloor.SetActive(false);
        if (_magicAnchor != null)
            _magicAnchor.SetActive(false);
    }
    
    /// <summary>
    /// IdleSecondFloor를 충돌 상태에 따라 업데이트합니다.
    /// 먼저 활성화한 플레이어의 IdleSecondFloor에 나중에 활성화한 플레이어의 마법 스프라이트가 표시됩니다.
    /// </summary>
    private void UpdateIdleSecondFloor()
    {
        if (_magicIdleSecondFloor == null || _idleSecondFloorRenderer == null) return;
        
        // 다른 플레이어와 충돌 중인지 확인
        if (_isPlayerColliding && _collidingPlayer != null && _collidingPlayer.MagicController != null && _collidingPlayerMagicData != null)
        {
            float myActivationTime = _magicActivationTime;
            float otherActivationTime = _collidingPlayer.MagicController.MagicActivationTime;
            bool otherMagicActive = _collidingPlayer.MagicController.IsMagicUIActive;
            
            bool shouldShowSecondFloor = false;
            
            // 시간 비교
            if (myActivationTime > 0 && otherActivationTime > 0)
            {
                float timeDiff = Mathf.Abs(myActivationTime - otherActivationTime);
                
                if (timeDiff > 0.01f) // 0.01초 이상 차이나면
                {
                    if (myActivationTime < otherActivationTime)
                    {
                        // 내가 먼저 활성화 (상대방이 나중)
                        shouldShowSecondFloor = true;
                    }
                }
                else // 거의 동시면 캐릭터 인덱스로 결정
                {
                    int myCharIndex = _controller.CharacterIndex;
                    int otherCharIndex = _collidingPlayer.CharacterIndex;
                    
                    if (myCharIndex != otherCharIndex)
                    {
                        // 캐릭터 인덱스가 작은 쪽이 우선권 (흡수하는 쪽)
                        if (myCharIndex < otherCharIndex)
                        {
                            shouldShowSecondFloor = true;
                        }
                    }
                    else
                    {
                        // 캐릭터도 같으면 Object ID로 결정 (결정론적)
                        if (_controller.Object.Id.Raw < _collidingPlayer.Object.Id.Raw)
                        {
                            shouldShowSecondFloor = true;
                        }
                    }
                }
            }
            
            Debug.Log($"[PlayerMagicController] UpdateIdleSecondFloor - My time: {myActivationTime}, Other time: {otherActivationTime}, My char: {_controller.CharacterIndex}, Other char: {_collidingPlayer.CharacterIndex}, My ID: {_controller.Object.Id}, Other ID: {_collidingPlayer.Object.Id}, Should show: {shouldShowSecondFloor}");
            
            if (shouldShowSecondFloor)
            {
                Debug.Log($"[PlayerMagicController] SHOWING IdleSecondFloor");
                
                // 상대방의 마법 스프라이트를 내 IdleSecondFloor에 표시
                if (_collidingPlayerMagicData.magicIdleSprite != null)
                {
                    _idleSecondFloorRenderer.sprite = _collidingPlayerMagicData.magicIdleSprite;
                    _magicIdleSecondFloor.SetActive(true);
                    Debug.Log($"[PlayerMagicController] IdleSecondFloor activated with sprite: {_collidingPlayerMagicData.magicIdleSprite.name}");
                    return;
                }
            }
        }
        
        // 그 외의 경우는 비활성화
        _magicIdleSecondFloor.SetActive(false);
    }
    
    /// <summary>
    /// MagicAnchorCollision에서 호출: 다른 플레이어와의 충돌 시작
    /// </summary>
    public void OnPlayerCollisionEnter(PlayerController otherPlayer)
    {
        // 자기 자신이 아닌 다른 플레이어와 충돌했을 때만
        if (otherPlayer != _controller)
        {
            _isPlayerColliding = true;
            _collidingPlayer = otherPlayer;
            
            Debug.Log($"[PlayerMagicController] Collision ENTER - My time: {_magicActivationTime}, Other time: {(otherPlayer.MagicController != null ? otherPlayer.MagicController.MagicActivationTime : -1f)}, My char: {_controller.CharacterIndex}, Other char: {otherPlayer.CharacterIndex}, My ID: {_controller.Object.Id}, Other ID: {otherPlayer.Object.Id}");
            
            // 상대방의 마법 데이터 가져오기
            if (otherPlayer.MagicController != null)
            {
                _collidingPlayerMagicData = otherPlayer.MagicController.CurrentMagicData;
                Debug.Log($"[PlayerMagicController] Colliding with player - MagicData: {_collidingPlayerMagicData != null}");
            }
            else
            {
                Debug.LogWarning($"[PlayerMagicController] Other player's MagicController is null");
            }
        }
    }
    
    /// <summary>
    /// MagicAnchorCollision에서 호출: 충돌 종료
    /// </summary>
    public void OnPlayerCollisionExit(PlayerController otherPlayer)
    {
        if (otherPlayer != _controller)
        {
            _isPlayerColliding = false;
            _collidingPlayer = null;
            _collidingPlayerMagicData = null;
        }
    }
    #endregion
}
