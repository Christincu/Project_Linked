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
    private MagicData _magicData1; // 좌클릭 마법
    private MagicData _magicData2; // 우클릭 마법
    private int _activeMagicSlot = 0; // 현재 활성화된 마법 슬롯 (0: 없음, 1: 좌클릭, 2: 우클릭)
    private bool _isInitialized = false;

    // Magic UI References
    private GameObject _magicViewObj;
    private GameObject _magicAnchor;
    private GameObject _magicIdleFirstFloor;
    private GameObject _magicIdleSecondFloor;
    private GameObject _magicActiveFloor;

    private SpriteRenderer _idleFirstFloorRenderer;
    private SpriteRenderer _idleSecondFloorRenderer;
    private SpriteRenderer _activeFloorRenderer;

    // 충돌 상태
    private bool _isPlayerColliding = false;
    private PlayerController _collidingPlayer = null;
    
    // 흡수 상태
    private PlayerController _absorbedPlayer = null; // 흡수한 플레이어
    private bool _wasAbsorbed = false; // 내가 흡수당했는지 여부

    // 입력 상태
    private bool _previousLeftMouseButton = false;
    private bool _previousRightMouseButton = false;
    #endregion

    #region Properties
    public PlayerController Controller => _controller;

    /// <summary>
    /// 현재 활성화된 마법 데이터를 반환합니다.
    /// 네트워크 동기화된 슬롯 번호를 사용합니다.
    /// </summary>
    public MagicData CurrentMagicData
    {
        get
        {
            // 네트워크 동기화된 슬롯 번호 사용
            int slot = _controller != null ? _controller.ActiveMagicSlotNetworked : _activeMagicSlot;
            return slot == 1 ? _magicData1 : (slot == 2 ? _magicData2 : null);
        }
    }

    /// <summary>
    /// 현재 활성화된 마법 슬롯 (로컬 또는 네트워크 동기화)
    /// </summary>
    public int ActiveMagicSlot
    {
        get
        {
            // InputAuthority는 로컬 값, 다른 클라이언트는 네트워크 값 사용
            if (_controller != null && _controller.Object.HasInputAuthority)
                return _activeMagicSlot;
            return _controller != null ? _controller.ActiveMagicSlotNetworked : 0;
        }
    }

    /// <summary>
    /// 마법을 시전할 수 있는지 확인 (쿨다운, 사망 상태 체크)
    /// </summary>
    public bool CanCastMagic
    {
        get
        {
            if (_controller == null || CurrentMagicData == null || _controller.IsDead) return false;
            return _controller.MagicCooldownTimer.ExpiredOrNotRunning(_controller.Runner);
        }
    }

    public bool IsOnCooldown => _controller != null && _controller.MagicCooldownTimer.IsRunning;
    public float RemainingCooldown => _controller?.MagicCooldownTimer.RemainingTime(_controller.Runner) ?? 0f;

    public bool IsMagicUIActive => _controller != null ? _controller.MagicActive : false;
    public GameObject MagicViewObj => _magicViewObj;
    #endregion

    #region Events
    public System.Action<Vector3, Vector3> OnMagicCast;
    public System.Action OnCooldownStarted;
    public System.Action OnCooldownEnded;
    #endregion

    // ---

    #region Initialization
    public void Initialize(PlayerController controller)
    {
        _controller = controller;
    }

    void Update()
    {
        // 로컬 Input Authority를 가진 플레이어만 UI 위치 갱신
        if (!_isInitialized || _controller == null || !_controller.Object.HasInputAuthority) return;

        if (_controller.MagicActive)
        {
            UpdateMagicViewObjPosition(_controller.transform.position);
            
            // 흡수 상태가 있으면 흡수된 마법 표시, 아니면 일반 충돌 표시
            if (_absorbedPlayer != null && !_absorbedPlayer.MagicActive)
            {
                UpdateIdleSecondFloorForAbsorbed();
            }
            else
            {
                UpdateIdleSecondFloor();
            }
        }
    }

    public void SetMagicUIReferences(GameObject viewObj, GameObject anchor, GameObject idleFirst, GameObject idleSecond, GameObject active)
    {
        _magicViewObj = viewObj;
        _magicAnchor = anchor;
        _magicIdleFirstFloor = idleFirst;
        _magicIdleSecondFloor = idleSecond;
        _magicActiveFloor = active;

        _idleFirstFloorRenderer = _magicIdleFirstFloor?.GetComponent<SpriteRenderer>();
        _idleSecondFloorRenderer = _magicIdleSecondFloor?.GetComponent<SpriteRenderer>();
        _activeFloorRenderer = _magicActiveFloor?.GetComponent<SpriteRenderer>();

        LoadMagicData();
        InitializeMagicUI();

        _isInitialized = true;
    }

    private void LoadMagicData()
    {
        if (_controller == null || GameDataManager.Instance == null) return;

        int characterIndex = _controller.CharacterIndex;
        CharacterData characterData = GameDataManager.Instance.CharacterService.GetCharacter(characterIndex);

        if (characterData == null) return;

        _magicData1 = characterData.magicData1;
        _magicData2 = characterData.magicData2;
    }

    private void InitializeMagicUI()
    {
        if (_magicViewObj != null)
        {
            _magicViewObj.transform.localPosition = Vector3.zero;
            _magicIdleFirstFloor?.transform.SetParent(_magicViewObj.transform, false);
            _magicIdleSecondFloor?.transform.SetParent(_magicViewObj.transform, false);
            _magicActiveFloor?.transform.SetParent(_magicViewObj.transform, false);
        }

        if (_magicAnchor != null)
        {
            var collision = _magicAnchor.GetComponent<MagicAnchorCollision>() ?? _magicAnchor.AddComponent<MagicAnchorCollision>();
            collision.Initialize(this);
        }

        _magicViewObj?.SetActive(false);
        _magicAnchor?.SetActive(false);
        _magicIdleFirstFloor?.SetActive(false);
        _magicIdleSecondFloor?.SetActive(false);
        _magicActiveFloor?.SetActive(false);
    }
    #endregion

    // ---

    #region Magic Casting
    public void CastMagic(Vector3 targetPosition)
    {
        if (_controller == null || CurrentMagicData == null || !_controller.Object.HasStateAuthority) return;
        if (!CanCastMagic || _controller.State == null) return;

        _controller.MagicCooldownTimer = TickTimer.CreateFromSeconds(_controller.Runner, CurrentMagicData.cooldown);

        Vector3 startPos = _controller.transform.position;
        Vector3 direction = (targetPosition - startPos).normalized;

        SpawnMagicProjectile(startPos, direction);

        OnMagicCast?.Invoke(startPos, direction);
        OnCooldownStarted?.Invoke();
    }

    private void SpawnMagicProjectile(Vector3 position, Vector3 direction)
    {
        if (magicProjectilePrefab == null || CurrentMagicData == null) return;

        // TODO: NetworkObject로 스폰 로직으로 변경 필요
        GameObject projectile = Instantiate(magicProjectilePrefab, position, Quaternion.identity);
        Destroy(projectile, CurrentMagicData.range / CurrentMagicData.speed);
    }
    #endregion

    // ---

    #region Magic UI Control & Input

    /// <summary>
    /// 네트워크에서 MagicActive 상태가 변경될 때 호출됩니다.
    /// </summary>
    public void UpdateMagicUIFromNetwork(bool isActive)
    {
        if (!_isInitialized) return;

        if (isActive)
        {
            // 네트워크 동기화된 슬롯으로 마법 데이터 가져오기
            int networkSlot = _controller.ActiveMagicSlotNetworked;
            MagicData activeData = networkSlot == 1 ? _magicData1 : (networkSlot == 2 ? _magicData2 : null);

            if (activeData == null)
            {
                Debug.LogWarning($"[PlayerMagicController] No magic data for slot {networkSlot}");
                return;
            }

            // InputAuthority가 아닌 클라이언트도 로컬 슬롯 동기화
            if (!_controller.Object.HasInputAuthority)
            {
                _activeMagicSlot = networkSlot;
            }

            // UI 활성화
            _magicAnchor?.SetActive(true);
            _magicViewObj?.SetActive(true);
            _magicIdleFirstFloor?.SetActive(true);
            _magicActiveFloor?.SetActive(true);

            // 스프라이트 설정
            if (_idleFirstFloorRenderer != null && activeData.magicIdleSprite != null)
            {
                _idleFirstFloorRenderer.sprite = activeData.magicIdleSprite;
            }
            if (_activeFloorRenderer != null && activeData.magicActiveSprite != null)
            {
                _activeFloorRenderer.sprite = activeData.magicActiveSprite;
            }
        }
        else
        {
            // 비활성화 시 로컬 슬롯도 초기화 (InputAuthority는 이미 DeactivateMagicUI에서 처리)
            if (!_controller.Object.HasInputAuthority)
            {
                _activeMagicSlot = 0;
            }

            // 흡수 상태 초기화 (마법이 비활성화되면 흡수도 해제)
            _absorbedPlayer = null;

            // UI 비활성화 및 위치 초기화
            if (_magicViewObj != null)
            {
                _magicViewObj.transform.localPosition = Vector3.zero;
                _magicViewObj.SetActive(false);
            }
            if (_magicAnchor != null)
            {
                _magicAnchor.transform.localPosition = Vector3.zero; // 앵커 위치도 초기화
                _magicAnchor.SetActive(false);
            }

            _magicIdleFirstFloor?.SetActive(false);
            _magicActiveFloor?.SetActive(false);
            _magicIdleSecondFloor?.SetActive(false);
        }

        // 흡수 상태 체크하여 적절한 업데이트 호출
        if (_absorbedPlayer != null && !_absorbedPlayer.MagicActive)
        {
            UpdateIdleSecondFloorForAbsorbed();
        }
        else
        {
            UpdateIdleSecondFloor();
        }
    }
    
    /// <summary>
    /// 마법이 흡수당했을 때 호출됩니다. (PlayerController에서 호출)
    /// </summary>
    public void OnAbsorbed()
    {
        if (!_isInitialized) return;
        
        _wasAbsorbed = true;
        _activeMagicSlot = 0;
        
        Debug.Log($"[PlayerMagicController] Magic was absorbed - cannot toggle until reactivated");
    }
    
    /// <summary>
    /// 흡수 상태를 해제합니다. (다시 마법을 활성화할 수 있음)
    /// </summary>
    public void ClearAbsorbedState()
    {
        _wasAbsorbed = false;
    }

    public void ProcessInput(InputData? inputData, PlayerController controller, bool isTestMode)
    {
        if (!_isInitialized || !inputData.HasValue || !controller.Object.HasInputAuthority) return;

        InputData data = inputData.Value;

        bool currentLeftButton = data.GetMouseButton(InputMouseButton.LEFT);
        bool currentRightButton = data.GetMouseButton(InputMouseButton.RIGHT);

        bool leftClickDown = currentLeftButton && !_previousLeftMouseButton;
        bool rightClickDown = currentRightButton && !_previousRightMouseButton;

        // 흡수당한 상태에서는 마법 토글 불가
        if (!_wasAbsorbed)
        {
            if (leftClickDown)
            {
                ToggleMagicUI(controller, 1);
            }
            else if (rightClickDown)
            {
                ToggleMagicUI(controller, 2);
            }
        }

        _previousLeftMouseButton = currentLeftButton;
        _previousRightMouseButton = currentRightButton;

        if (controller.MagicActive)
        {
            UpdateMagicAnchorPosition(data, controller);
            
            // Space 키로 상대방 마법 흡수
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TryAbsorbOtherPlayerMagic();
            }
        }
    }
    
    /// <summary>
    /// 충돌 중인 상대방의 마법을 흡수합니다.
    /// </summary>
    private void TryAbsorbOtherPlayerMagic()
    {
        if (!_isPlayerColliding || _collidingPlayer == null || !_collidingPlayer.MagicActive) return;
        
        // 우선순위가 있을 때만 흡수 가능
        bool myPriority = _controller.Object.Id.Raw < _collidingPlayer.Object.Id.Raw;
        if (!myPriority) return;
        
        // 이미 흡수한 플레이어인지 확인
        if (_absorbedPlayer == _collidingPlayer)
        {
            Debug.Log($"[PlayerMagicController] Already absorbed player {_collidingPlayer.Object.InputAuthority}");
            return;
        }
        
        // 흡수 실행
        _absorbedPlayer = _collidingPlayer;
        
        // 상대방에게 흡수당했다고 알림
        _controller.RPC_AbsorbMagic(_collidingPlayer.Object.InputAuthority);
        
        Debug.Log($"[PlayerMagicController] Absorbed magic from player {_collidingPlayer.Object.InputAuthority}");
    }

    private void ToggleMagicUI(PlayerController controller, int targetSlot)
    {
        MagicData magicData = targetSlot == 1 ? _magicData1 : _magicData2;
        if (magicData == null) return;

        if (_activeMagicSlot == targetSlot)
        {
            DeactivateMagicUI(controller);
        }
        else
        {
            ActivateMagicUI(controller, targetSlot, magicData);
        }
    }

    private void ActivateMagicUI(PlayerController controller, int magicSlot, MagicData magicData)
    {
        if (magicData == null) return;

        // 로컬 슬롯 설정 (InputAuthority만)
        _activeMagicSlot = magicSlot;
        
        // 흡수 상태 해제 (다시 활성화 시도 시)
        if (_wasAbsorbed)
        {
            _wasAbsorbed = false;
            Debug.Log($"[PlayerMagicController] Cleared absorbed state - can activate magic again");
        }

        // 네트워크 동기화: RPC를 통해 서버에 요청 → 서버가 브로드캐스트
        if (controller.Object.HasInputAuthority && !controller.MagicActive)
        {
            controller.RPC_ActivateMagic(magicSlot);
        }
    }

    private void DeactivateMagicUI(PlayerController controller)
    {
        // 로컬 슬롯 초기화
        _activeMagicSlot = 0;

        // 네트워크 동기화: RPC를 통해 서버에 요청 → 서버가 브로드캐스트
        if (controller.Object.HasInputAuthority && controller.MagicActive)
        {
            controller.RPC_DeactivateMagic();
        }
    }

    private void UpdateMagicAnchorPosition(InputData inputData, PlayerController controller)
    {
        if (_magicAnchor == null) return;

        Vector3 playerPos = controller.transform.position;
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;

        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(inputData.MousePosition);
        mouseWorldPos.z = 0;

        float horizontalDirection = mouseWorldPos.x - playerPos.x;
        Vector3 anchorLocalPos = _magicAnchor.transform.localPosition;
        float distance = 1f;
        if (_magicAnchor.transform.parent != null)
        {
            distance = Mathf.Abs(_magicAnchor.transform.localPosition.x);
        }

        anchorLocalPos.x = horizontalDirection >= 0 ? distance : -distance;
        _magicAnchor.transform.localPosition = anchorLocalPos;

        // RPC를 통해 서버에 앵커 위치 업데이트 요청
        if (controller.Object.HasInputAuthority)
        {
            controller.RPC_UpdateMagicAnchorPosition(anchorLocalPos);
        }

        UpdateMagicViewObjPosition(playerPos);
    }

    /// <summary>
    /// MagicViewObj의 위치를 업데이트합니다.
    /// 플레이어 간 충돌 시 우선순위에 따라 위치를 결정합니다.
    /// 흡수된 플레이어는 계속 합체 상태를 유지합니다.
    /// </summary>
    private void UpdateMagicViewObjPosition(Vector3 playerPos)
    {
        if (_magicViewObj == null || _magicAnchor == null) return;

        // 흡수한 플레이어가 있고 그 플레이어의 마법이 비활성화된 경우 (흡수 상태 유지)
        if (_absorbedPlayer != null && !_absorbedPlayer.MagicActive && 
            _absorbedPlayer.MagicController != null)
        {
            // 흡수된 플레이어의 ViewObj를 계속 표시 (합체 유지)
            GameObject absorbedViewObj = _absorbedPlayer.MagicController.MagicViewObj;
            if (absorbedViewObj != null)
            {
                // 내 앵커 위치를 사용하지만, 두 번째 층에 흡수된 마법 표시
                _magicViewObj.transform.position = _magicAnchor.transform.position;
                UpdateIdleSecondFloorForAbsorbed();
                return;
            }
        }

        // 일반 충돌 확인 및 우선순위 판단
        if (_isPlayerColliding && _collidingPlayer != null &&
            _collidingPlayer.MagicActive && _collidingPlayer.MagicController != null)
        {
            GameObject otherMagicViewObj = _collidingPlayer.MagicController.MagicViewObj;

            if (otherMagicViewObj != null)
            {
                // 결정론적 우선순위: NetworkObject ID가 낮은 쪽이 우선
                bool myPriority = _controller.Object.Id.Raw < _collidingPlayer.Object.Id.Raw;

                if (myPriority)
                {
                    // 내가 우선순위 - 내 앵커 위치 사용
                    _magicViewObj.transform.position = _magicAnchor.transform.position;
                }
                else
                {
                    // 상대가 우선순위 - 상대 ViewObj 위치 사용 (겹침 표현)
                    _magicViewObj.transform.position = otherMagicViewObj.transform.position;
                }
                return;
            }
        }

        // 충돌이 없거나 상대가 마법을 사용하지 않음 - 기본 위치
        _magicViewObj.transform.position = _magicAnchor.transform.position;
    }
    
    /// <summary>
    /// 흡수된 플레이어의 마법을 두 번째 층에 계속 표시합니다.
    /// </summary>
    private void UpdateIdleSecondFloorForAbsorbed()
    {
        if (_magicIdleSecondFloor == null || _idleSecondFloorRenderer == null || 
            _absorbedPlayer == null || _absorbedPlayer.MagicController == null)
        {
            return;
        }

        MagicData absorbedMagicData = _absorbedPlayer.MagicController.CurrentMagicData;

        if (absorbedMagicData != null && absorbedMagicData.magicIdleSprite != null)
        {
            _idleSecondFloorRenderer.sprite = absorbedMagicData.magicIdleSprite;
            _magicIdleSecondFloor.SetActive(true);
        }
    }

    /// <summary>
    /// 플레이어 충돌 시 두 번째 층 스프라이트를 업데이트합니다.
    /// 내가 우선순위일 때 상대방의 마법 스프라이트를 표시합니다.
    /// 흡수 상태가 아닐 때만 호출됩니다.
    /// </summary>
    private void UpdateIdleSecondFloor()
    {
        // 기본 검증
        if (_magicIdleSecondFloor == null || _idleSecondFloorRenderer == null)
        {
            return;
        }
        
        // 흡수 상태가 있으면 이 메서드는 호출되지 않아야 함
        if (_absorbedPlayer != null && !_absorbedPlayer.MagicActive)
        {
            UpdateIdleSecondFloorForAbsorbed();
            return;
        }

        // 충돌 상태가 아니면 비활성화
        if (!_isPlayerColliding || _collidingPlayer == null ||
            _collidingPlayer.MagicController == null || !_collidingPlayer.MagicActive)
        {
            _magicIdleSecondFloor.SetActive(false);
            return;
        }

        // 결정론적 우선순위 판단 (UpdateMagicViewObjPosition 로직과 동일)
        bool myPriority = _controller.Object.Id.Raw < _collidingPlayer.Object.Id.Raw;

        // 내가 우선순위일 때만 상대방의 스프라이트를 두 번째 층에 표시
        if (myPriority)
        {
            MagicData otherMagicData = _collidingPlayer.MagicController.CurrentMagicData;

            if (otherMagicData != null && otherMagicData.magicIdleSprite != null)
            {
                _idleSecondFloorRenderer.sprite = otherMagicData.magicIdleSprite;
                _magicIdleSecondFloor.SetActive(true);
                return;
            }
        }

        // 우선순위가 없거나 상대 데이터가 없으면 비활성화
        _magicIdleSecondFloor.SetActive(false);
    }

    /// <summary>
    /// 다른 클라이언트로부터 앵커 위치를 동기화합니다.
    /// InputAuthority가 아닌 클라이언트에서만 처리합니다.
    /// </summary>
    public void UpdateAnchorPositionFromNetwork(Vector3 localPosition)
    {
        if (!_isInitialized || _magicAnchor == null) return;

        // InputAuthority는 자신의 마우스 위치로 앵커를 제어하므로 네트워크 값 무시
        if (!Controller.Object.HasInputAuthority && Controller.MagicActive)
        {
            _magicAnchor.transform.localPosition = localPosition;
            UpdateMagicViewObjPosition(Controller.transform.position);
        }
    }

    #region Collision Callbacks
    /// <summary>
    /// MagicAnchorCollision에서 호출됩니다.
    /// 다른 플레이어와 충돌 시 충돌 상태를 저장합니다.
    /// </summary>
    public void OnPlayerCollisionEnter(PlayerController otherPlayer)
    {
        if (otherPlayer == null || otherPlayer == _controller) return;

        _isPlayerColliding = true;
        _collidingPlayer = otherPlayer;

        // 충돌 발생 시 UI 즉시 업데이트
        if (_controller.MagicActive)
        {
            UpdateMagicViewObjPosition(_controller.transform.position);
            UpdateIdleSecondFloor();
        }
    }

    /// <summary>
    /// MagicAnchorCollision에서 호출됩니다.
    /// 다른 플레이어와 충돌 종료 시 충돌 상태를 초기화합니다.
    /// 단, 흡수 상태는 유지됩니다.
    /// </summary>
    public void OnPlayerCollisionExit(PlayerController otherPlayer)
    {
        if (otherPlayer == null || otherPlayer == _controller) return;

        // 충돌하던 플레이어가 나갔을 때만 초기화
        if (_collidingPlayer == otherPlayer)
        {
            _isPlayerColliding = false;
            _collidingPlayer = null;
            
            // 흡수 상태는 유지 (_absorbedPlayer는 초기화하지 않음)

            // 충돌 종료 시 UI 즉시 업데이트
            if (_controller.MagicActive)
            {
                UpdateMagicViewObjPosition(_controller.transform.position);
                
                // 흡수 상태 체크
                if (_absorbedPlayer != null && !_absorbedPlayer.MagicActive)
                {
                    UpdateIdleSecondFloorForAbsorbed();
                }
                else
                {
                    UpdateIdleSecondFloor();
                }
            }
        }
    }
    #endregion
    #endregion
}