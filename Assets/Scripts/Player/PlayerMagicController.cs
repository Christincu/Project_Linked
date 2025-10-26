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

    // 입력 상태
    private bool _previousLeftMouseButton = false;
    private bool _previousRightMouseButton = false;
    #endregion

    #region Properties
    public PlayerController Controller => _controller;

    public MagicData CurrentMagicData => _activeMagicSlot == 1 ? _magicData1 : (_activeMagicSlot == 2 ? _magicData2 : null);

    public int ActiveMagicSlot => _activeMagicSlot;

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
            UpdateIdleSecondFloor();
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

    public void UpdateMagicUIFromNetwork(bool isActive)
    {
        if (!_isInitialized) return;

        if (isActive)
        {
            _magicAnchor?.SetActive(true);
            _magicViewObj?.SetActive(true);
            _magicIdleFirstFloor?.SetActive(true);
            _magicActiveFloor?.SetActive(true);
        }
        else
        {
            _magicViewObj.transform.localPosition = Vector3.zero;
            _magicViewObj.SetActive(false);
            _magicIdleFirstFloor.SetActive(false);
            _magicActiveFloor.SetActive(false);
            _magicIdleSecondFloor.SetActive(false);
            _magicAnchor?.SetActive(false);
        }

        UpdateIdleSecondFloor();
    }

    public void ProcessInput(InputData? inputData, PlayerController controller, bool isTestMode)
    {
        if (!_isInitialized || !inputData.HasValue || !controller.Object.HasInputAuthority) return;

        InputData data = inputData.Value;

        bool currentLeftButton = data.GetMouseButton(InputMouseButton.LEFT);
        bool currentRightButton = data.GetMouseButton(InputMouseButton.RIGHT);

        bool leftClickDown = currentLeftButton && !_previousLeftMouseButton;
        bool rightClickDown = currentRightButton && !_previousRightMouseButton;

        if (leftClickDown)
        {
            ToggleMagicUI(controller, 1);
        }
        else if (rightClickDown)
        {
            ToggleMagicUI(controller, 2);
        }

        _previousLeftMouseButton = currentLeftButton;
        _previousRightMouseButton = currentRightButton;

        if (controller.MagicActive)
        {
            UpdateMagicAnchorPosition(data, controller);
        }
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
        _activeMagicSlot = magicSlot;

        // 네트워크 동기화
        if (!controller.MagicActive)
        {
            controller.MagicActive = true;
            controller.SetMagicActivationTick(controller.Runner.Tick);
        }

        _idleFirstFloorRenderer.sprite = magicData.magicIdleSprite;
        _activeFloorRenderer.sprite = magicData.magicActiveSprite;

        _magicAnchor?.SetActive(true);
        _magicViewObj?.SetActive(true);
        _magicIdleFirstFloor?.SetActive(true);
        _magicActiveFloor?.SetActive(true);
    }

    private void DeactivateMagicUI(PlayerController controller)
    {
        _activeMagicSlot = 0;

        // 네트워크 동기화
        if (controller.MagicActive)
        {
            controller.MagicActive = false;
            controller.SetMagicActivationTick(0);
        }

        _magicViewObj.transform.localPosition = Vector3.zero;
        _magicViewObj.SetActive(false);
        _magicIdleFirstFloor?.SetActive(false);
        _magicActiveFloor?.SetActive(false);
        _magicIdleSecondFloor?.SetActive(false);
        _magicAnchor.SetActive(false);
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

    private void UpdateMagicViewObjPosition(Vector3 playerPos)
    {
        if (_magicViewObj == null || _magicAnchor == null) return;

        if (_isPlayerColliding && _collidingPlayer != null && _collidingPlayer.MagicActive)
        {
            // 결정론적 우선순위 판단: NetworkObject ID가 낮은 쪽이 우선
            bool myPriority = _controller.Object.Id.Raw < _collidingPlayer.Object.Id.Raw;

            // Tick 비교 로직은 SetMagicActivationTick 구현 후 적용 가능

            if (myPriority)
            {
                _magicViewObj.transform.position = _magicAnchor.transform.position;
            }
            else
            {
                _magicViewObj.transform.position = _collidingPlayer.MagicController.MagicViewObj.transform.position;
            }
        }
        else
        {
            _magicViewObj.transform.position = _magicAnchor.transform.position;
        }
    }

    private void UpdateIdleSecondFloor()
    {
        if (_magicIdleSecondFloor == null || _idleSecondFloorRenderer == null || !_isPlayerColliding || _collidingPlayer == null || _collidingPlayer.MagicController == null)
        {
            _magicIdleSecondFloor?.SetActive(false);
            return;
        }

        bool myPriority = false;

        if (_collidingPlayer.MagicActive)
        {
            // 결정론적 우선순위 판단 (UpdateMagicViewObjPosition 로직과 일치)
            myPriority = _controller.Object.Id.Raw < _collidingPlayer.Object.Id.Raw;
        }

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

        _magicIdleSecondFloor.SetActive(false);
    }

    public void UpdateAnchorPositionFromNetwork(Vector3 localPosition)
    {
        if (_magicAnchor != null && !Controller.Object.HasInputAuthority)
        {
            // Input Authority가 없는 경우에만 네트워크 값을 반영하여 UI 앵커 위치를 업데이트
            _magicAnchor.transform.localPosition = localPosition;
        }
    }

    // --- Collision Callbacks (MagicAnchorCollision에서 호출) ---

    public void OnPlayerCollisionEnter(PlayerController otherPlayer)
    {
        if (otherPlayer != _controller)
        {
            _isPlayerColliding = true;
            _collidingPlayer = otherPlayer;
        }
    }

    public void OnPlayerCollisionExit(PlayerController otherPlayer)
    {
        if (otherPlayer != _controller)
        {
            _isPlayerColliding = false;
            _collidingPlayer = null;
        }
    }
    #endregion
}