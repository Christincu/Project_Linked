using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어의 마법 공격 및 UI를 담당하는 컨트롤러 (리팩토링 및 호환성 수정됨)
/// </summary>
public class PlayerMagicController : MonoBehaviour
{
    #region Private Fields - Handlers
    private Dictionary<int, ICombinedMagicHandler> _magicHandlers = new Dictionary<int, ICombinedMagicHandler>();
    private ICombinedMagicHandler _currentHandler = null;
    #endregion

    #region Private Fields - References
    private GameDataManager _gameDataManager;
    private PlayerController _controller;
    private CharacterData _characterData;
    private NetworkBehaviour.ChangeDetector _changeDetector;
    private bool _isInitialized = false;

    // UI Objects
    private GameObject _magicViewObj;
    private GameObject _magicAnchor;
    private GameObject _magicIdleFirstFloor;
    private GameObject _magicIdleSecondFloor;
    private GameObject _magicInsideFloor;

    // Renderers
    private SpriteRenderer _idleFirstFloorRenderer;
    private SpriteRenderer _idleSecondFloorRenderer;
    private SpriteRenderer _activeInsideRenderer;

    // Input State
    private NetworkButtons _prevButtons;
    #endregion

    #region Properties
    public PlayerController Controller => _controller;
    public bool IsMagicUIActive => _magicViewObj != null && _magicViewObj.activeSelf;
    public GameObject MagicViewObj => _magicViewObj;

    public bool CanCastMagic
    {
        get
        {
            if (_controller == null) return false;
            if (_controller.IsDead) return false;
            // 돌진 중일 때는 마법 시전 불가
            if (_controller.HasDashSkill) return false; 
            return true;
        }
    }
    #endregion

    #region Events
    public System.Action<Vector3, Vector3, int> OnMagicCast;
    public System.Action OnCooldownStarted;
    #endregion

    #region Initialization

    public void Initialize(PlayerController controller, GameDataManager gameDataManager)
    {
        _controller = controller;
        _gameDataManager = gameDataManager;
        _characterData = gameDataManager.CharacterService.GetCharacter(_controller.CharacterIndex);
        
        // PlayerController의 ChangeDetector를 공유해서 사용
        if (_controller != null)
        {
            _changeDetector = _controller.MagicChangeDetector;
        }
        
        InitializeMagicHandlers();
    }

    private void InitializeMagicHandlers()
    {
        _magicHandlers.Clear();
        
        // 베리어 핸들러 (Code 10)
        var barrierHandler = GetComponent<BarrierMagicHandler>() ?? gameObject.AddComponent<BarrierMagicHandler>();
        barrierHandler.Initialize(this, _gameDataManager);
        _magicHandlers[10] = barrierHandler;
        
        // 대쉬 핸들러 (Code 11)
        var dashHandler = GetComponent<DashMagicHandler>() ?? gameObject.AddComponent<DashMagicHandler>();
        dashHandler.Initialize(this, _gameDataManager);
        _magicHandlers[11] = dashHandler;
    }

    public void SetMagicUIReferences(GameObject viewObj, GameObject anchor, GameObject idleFirst, GameObject idleSecond, GameObject active)
    {
        _magicViewObj = viewObj;
        _magicAnchor = anchor;
        _magicIdleFirstFloor = idleFirst;
        _magicIdleSecondFloor = idleSecond;
        _magicInsideFloor = active;

        if (_magicIdleFirstFloor) _idleFirstFloorRenderer = _magicIdleFirstFloor.GetComponent<SpriteRenderer>();
        if (_magicIdleSecondFloor) _idleSecondFloorRenderer = _magicIdleSecondFloor.GetComponent<SpriteRenderer>();
        if (_magicInsideFloor) _activeInsideRenderer = _magicInsideFloor.GetComponent<SpriteRenderer>();

        InitializeUIStructure();
        _isInitialized = true;
    }

    private void InitializeUIStructure()
    {
        if (_magicViewObj == null) return;

        _magicViewObj.transform.localPosition = Vector3.zero;
        
        // 계층 구조 정리
        Transform parent = _magicViewObj.transform;
        if (_magicIdleFirstFloor) _magicIdleFirstFloor.transform.SetParent(parent, false);
        if (_magicIdleSecondFloor) _magicIdleSecondFloor.transform.SetParent(parent, false);
        if (_magicInsideFloor) _magicInsideFloor.transform.SetParent(parent, false);

        // Anchor 충돌체 설정
        if (_magicAnchor != null)
        {
            var collision = _magicAnchor.GetComponent<MagicAnchorCollision>() ?? _magicAnchor.AddComponent<MagicAnchorCollision>();
            collision.Initialize(this);
        }

        // 초기 비활성화
        SetUIActiveRecursively(false);
    }

    #endregion

    #region Core Logic: Input Processing (FixedUpdateNetwork)

    public void ProcessInput(InputData inputData, PlayerController controller, bool isTestMode)
    {
        if (!_isInitialized || !controller.Object.HasInputAuthority) return;
        if (isTestMode && inputData.ControlledSlot != controller.PlayerSlot) return;

        // 돌진 중이면 마법 조작 차단 (핸들러 업데이트만 수행)
        if (controller.HasDashSkill)
        {
            _prevButtons = inputData.MouseButtons;
            SyncHandlerChoice();
            _currentHandler?.Update();
            Debug.Log($"[PlayerMagicController] Update: {_currentHandler?.MagicCode}");
            return;
        }

        // 1. 핸들러 동기화
        SyncHandlerChoice();

        // 2. 버튼 입력 감지
        NetworkButtons pressed = inputData.GetMouseButtonPressed(_prevButtons);
        bool leftClick = pressed.IsSet(InputMouseButton.LEFT);
        bool rightClick = pressed.IsSet(InputMouseButton.RIGHT);

        // 3. 상태별 로직
        if (!controller.MagicActive)
        {
            ProcessInactiveInput(leftClick, rightClick);
        }
        else
        {
            ProcessActiveInput(inputData, leftClick, rightClick, controller);
        }

        _prevButtons = inputData.MouseButtons;
        _currentHandler?.Update();
    }

    private void ProcessInactiveInput(bool leftClick, bool rightClick)
    {
        if (leftClick)
            _controller.RPC_ActivateMagic(1, _characterData.magicData1.magicCode);
        else if (rightClick)
            _controller.RPC_ActivateMagic(2, _characterData.magicData2.magicCode);
    }

    private void ProcessActiveInput(InputData inputData, bool leftClick, bool rightClick, PlayerController controller)
    {
        Vector3 mousePos = GetMouseWorldPosition(inputData);

        // 1. 마법 교체
        if (leftClick && controller.ActiveMagicSlotNetworked == 2)
        {
            _controller.RPC_ActivateMagic(1, _characterData.magicData1.magicCode);
            return;
        }
        if (rightClick && controller.ActiveMagicSlotNetworked == 1)
        {
            _controller.RPC_ActivateMagic(2, _characterData.magicData2.magicCode);
            return;
        }

        // 2. 마법 시전
        if (leftClick || rightClick)
        {
            TryCastMagic(mousePos);
        }
        // 3. 조준 (Anchor 이동)
        else
        {
            CalculateAndSetAnchorPosition(mousePos);
            _currentHandler?.ProcessInput(inputData, mousePos);
        }
    }

    private void CalculateAndSetAnchorPosition(Vector3 mousePos)
    {
        Vector3 playerPos = _controller.transform.position;
        float directionX = mousePos.x - playerPos.x;

        float currentDistX = Mathf.Abs(_controller.MagicAnchorLocalPosition.x);
        if (currentDistX < 0.1f) currentDistX = 1f;

        float targetX = directionX >= 0 ? currentDistX : -currentDistX;
        Vector3 targetLocal = new Vector3(targetX, _controller.MagicAnchorLocalPosition.y, _controller.MagicAnchorLocalPosition.z);

        if (Mathf.Abs(targetLocal.x - _controller.MagicAnchorLocalPosition.x) > 0.01f)
        {
            _controller.RPC_UpdateMagicAnchorPosition(targetLocal);
        }
    }

    #endregion

    #region Core Logic: Magic Casting

    private void TryCastMagic(Vector3 targetPos)
    {
        if (_currentHandler != null && _currentHandler.IsCasting()) return;

        int code = GetCurrentCombinedCode();
        if (code == -1)
        {
            _controller.RPC_DeactivateMagic();
            return;
        }

        if (_magicHandlers.TryGetValue(code, out var handler))
        {
            if (handler.CastMagic(targetPos))
            {
                _controller.RPC_DeactivateMagic();
            }
        }
        else
        {
            _controller.RPC_CastMagic(targetPos);
            _controller.RPC_DeactivateMagic();
        }
    }

    public void CastMagic(Vector3 targetPosition)
    {
        if (_controller == null || !_controller.Object.HasStateAuthority) return;
        if (!CanCastMagic) return;

        int magicCode = GetCurrentCombinedCode();
        if (magicCode == -1) return;

        if (!_magicHandlers.ContainsKey(magicCode))
        {
            Vector3 startPos = _controller.transform.position;
            Vector3 direction = (targetPosition - startPos).normalized;
            
            OnMagicCast?.Invoke(startPos, direction, magicCode);
            OnCooldownStarted?.Invoke();
        }
    }

    private int GetCurrentCombinedCode()
    {
        if (_controller == null) return -1;

        if (_controller.AbsorbedMagicCode != -1 && _controller.ActivatedMagicCode != -1)
        {
            return _gameDataManager.MagicService.GetCombinedMagic(
                _controller.ActivatedMagicCode, 
                _controller.AbsorbedMagicCode
            );
        }
        return _controller.ActivatedMagicCode;
    }

    #endregion

    #region Visuals: Rendering (Update)

    public void OnRender()
    {
        if (!_isInitialized || _controller == null) return;

        SyncHandlerChoice();

        foreach (var change in _changeDetector.DetectChanges(_controller))
        {
            switch (change)
            {
                case nameof(PlayerController.MagicActive):
                case nameof(PlayerController.ActivatedMagicCode):
                case nameof(PlayerController.AbsorbedMagicCode):
                    UpdateUIVisuals();
                    break;
                case nameof(PlayerController.MagicAnchorLocalPosition):
                    UpdateAnchorVisual();
                    break;
            }
        }
    }

    public void UpdateUIVisuals()
    {
        bool isActive = _controller.MagicActive;
        
        SetUIActiveRecursively(isActive);

        if (!isActive) return;

        if (_controller.ActivatedMagicCode != -1)
        {
            var data = _gameDataManager.MagicService.GetMagic(_controller.ActivatedMagicCode);
            if (data != null)
            {
                if (_idleFirstFloorRenderer) _idleFirstFloorRenderer.sprite = data.magicIdleSprite;
                if (_activeInsideRenderer) _activeInsideRenderer.sprite = data.magicInsideSprite;
            }
            if (_magicIdleFirstFloor) _magicIdleFirstFloor.SetActive(true);
            if (_magicInsideFloor) _magicInsideFloor.SetActive(true);
        }

        if (_controller.AbsorbedMagicCode != -1)
        {
            var data = _gameDataManager.MagicService.GetMagic(_controller.AbsorbedMagicCode);
            if (data != null && _idleSecondFloorRenderer)
            {
                _idleSecondFloorRenderer.sprite = data.magicIdleSprite;
            }
            if (_magicIdleSecondFloor) _magicIdleSecondFloor.SetActive(true);
        }
        else
        {
            if (_magicIdleSecondFloor) _magicIdleSecondFloor.SetActive(false);
        }
    }

    public void UpdateAnchorVisual()
    {
        if (_magicAnchor != null)
        {
            _magicAnchor.transform.localPosition = _controller.MagicAnchorLocalPosition;
        }
    }

    private void SyncHandlerChoice()
    {
        if (!_controller.MagicActive)
        {
            ChangeHandler(null);
            return;
        }

        int targetCode = GetCurrentCombinedCode();
        if (_currentHandler == null || _currentHandler.MagicCode != targetCode)
        {
            if (_magicHandlers.TryGetValue(targetCode, out var newHandler))
            {
                ChangeHandler(newHandler);
            }
            else
            {
                ChangeHandler(null);
            }
        }
    }

    private void ChangeHandler(ICombinedMagicHandler newHandler)
    {
        if (_currentHandler != newHandler)
        {
            _currentHandler?.OnMagicDeactivated();
            _currentHandler = newHandler;
            _currentHandler?.OnMagicActivated();
        }
    }

    private void SetUIActiveRecursively(bool active)
    {
        if (_magicViewObj) _magicViewObj.SetActive(active);
        if (_magicAnchor) _magicAnchor.SetActive(active);
        
        if (!active)
        {
            if (_magicIdleFirstFloor) _magicIdleFirstFloor.SetActive(false);
            if (_magicIdleSecondFloor) _magicIdleSecondFloor.SetActive(false);
            if (_magicInsideFloor) _magicInsideFloor.SetActive(false);
        }
    }

    #endregion

    #region Legacy & Helper Methods (Public API)

    // =================================================================
    // [호환성 수정] PlayerController 및 DashMagicHandler가 사용하는 메서드 복구
    // =================================================================

    /// <summary>
    /// PlayerController와의 호환성을 위한 래퍼 메서드.
    /// 네트워크 변수가 변경되었을 때 PlayerController에서 호출하여 UI를 즉시 갱신합니다.
    /// </summary>
    public void UpdateMagicUIState(bool isActive)
    {
        UpdateUIVisuals();
    }

    /// <summary>
    /// DashMagicHandler 등에서 이전 프레임의 입력 상태를 확인하기 위해 사용합니다.
    /// </summary>
    public bool GetPreviousLeftMouseButton()
    {
        return _prevButtons.IsSet(InputMouseButton.LEFT);
    }
    
    /// <summary>
    /// DashMagicHandler 등에서 이전 프레임의 입력 상태를 확인하기 위해 사용합니다.
    /// </summary>
    public bool GetPreviousRightMouseButton()
    {
        return _prevButtons.IsSet(InputMouseButton.RIGHT);
    }

    public ICombinedMagicHandler GetBarrierHandler() => _magicHandlers.TryGetValue(10, out var h) ? h : null;
    public BarrierMagicHandler GetBarrierMagicHandler() => GetBarrierHandler() as BarrierMagicHandler;

    public NetworkPrefabRef GetBarrierMagicObjectPrefab()
    {
        if (_gameDataManager?.MagicService == null) return default;
        var data = _gameDataManager.MagicService.GetCombinationDataByResult(10) as BarrierMagicCombinationData;
        return data != null ? data.barrierMagicObjectPrefab : default;
    }

    public GameObject GetMagicProjectilePrefab(int magicCode)
    {
        if (_gameDataManager?.MagicService == null) return null;
        return _gameDataManager.MagicService.GetMagic(magicCode)?.magicProjectilePrefab;
    }

    private Vector3 GetMouseWorldPosition(InputData inputData)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return Vector3.zero;
        Vector3 pos = mainCamera.ScreenToWorldPoint(inputData.MousePosition);
        pos.z = 0;
        return pos;
    }

    public void UpdateAnchorPosition(Vector3 localPosition)
    {
        if (_magicAnchor != null)
        {
            _magicAnchor.transform.localPosition = localPosition;
        }
    }

    // Interaction (Collision / Absorption)
    public void OnPlayerCollisionEnter(PlayerController otherPlayer)
    {
        if (!_controller.Object.HasStateAuthority) return;
        if (otherPlayer == null || otherPlayer.IsDead) return;
        if (!_controller.MagicActive || !otherPlayer.MagicActive) return;
        if (_controller.Object.Id.Raw > otherPlayer.Object.Id.Raw) return;

        int myTick = _controller.MagicActivationTick;
        int otherTick = otherPlayer.MagicActivationTick;
        if (myTick <= 0 || otherTick <= 0) return;

        bool iAmAbsorber = DetermineAbsorber(myTick, otherTick, 
            _controller.CharacterIndex, otherPlayer.CharacterIndex,
            _controller.Object.Id.Raw, otherPlayer.Object.Id.Raw);

        PlayerController absorber = iAmAbsorber ? _controller : otherPlayer;
        PlayerController absorbed = iAmAbsorber ? otherPlayer : _controller;
        int absorbedCode = absorbed.ActivatedMagicCode;

        absorbed.MagicActive = false;
        absorbed.MagicActivationTick = 0;
        absorbed.ActivatedMagicCode = -1;
        absorbed.ActiveMagicSlotNetworked = 0;
        absorbed.MagicController?.OnAbsorbed();

        absorber.AbsorbedMagicCode = absorbedCode;
        Debug.Log($"[Absorption] {absorber.name} absorbed {absorbed.name}'s magic ({absorbedCode})");
    }

    private bool DetermineAbsorber(int myTick, int otherTick, int myCharIndex, int otherCharIndex, uint myId, uint otherId)
    {
        if (myTick != otherTick) return myTick < otherTick;
        if (myCharIndex != otherCharIndex) return myCharIndex < otherCharIndex;
        return myId < otherId;
    }

    public void OnAbsorbed()
    {
        if (IsMagicUIActive) UpdateUIVisuals();
    }

    #endregion
}