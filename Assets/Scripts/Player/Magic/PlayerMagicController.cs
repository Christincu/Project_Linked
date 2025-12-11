using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어의 마법 시스템 전반을 관리하는 컨트롤러입니다.
/// 마법 선택(UI), 활성화/비활성화, 발사(Casting), 조합(Combination), 시각적 표현(Visuals)을 처리합니다.
/// </summary>
public class PlayerMagicController : MonoBehaviour
{
    #region [Dependencies] 외부 의존성
    private PlayerController _controller;
    private GameDataManager _gameDataManager;
    private CharacterData _characterData;
    private NetworkBehaviour.ChangeDetector _changeDetector;
    #endregion

    #region [State] 내부 상태 변수
    private bool _isInitialized = false;
    
    // 마법 핸들러 (특수 마법 로직 처리용)
    private Dictionary<int, ICombinedMagicHandler> _magicHandlers = new Dictionary<int, ICombinedMagicHandler>();
    private ICombinedMagicHandler _currentHandler = null;

    // 입력 처리용 상태
    private NetworkButtons _prevButtons;
    
    // 버그 수정용: 마법 선택 모드 상태 추적
    private bool _wasInSelectionMode = false; 
    #endregion

    #region [Visuals] UI 및 시각 효과 참조
    private GameObject _magicViewObj;          // 마법 UI 전체 부모
    private GameObject _magicAnchor;           // 조준점(Anchor) 오브젝트
    private GameObject _magicIdleFirstFloor;   // 1슬롯(활성) 마법 아이콘
    private GameObject _magicIdleSecondFloor;  // 2슬롯(흡수) 마법 아이콘
    private GameObject _magicInsideFloor;      // 내부 활성 효과

    private SpriteRenderer _idleFirstFloorRenderer;
    private SpriteRenderer _idleSecondFloorRenderer;
    private SpriteRenderer _activeInsideRenderer;
    #endregion

    #region [Properties]
    public PlayerController Controller => _controller;
    public bool IsMagicUIActive => _magicViewObj != null && _magicViewObj.activeSelf;
    public GameObject MagicViewObj => _magicViewObj;

    public bool CanCastMagic
    {
        get
        {
            if (_controller == null || _controller.IsDead) return false;
            if (_controller.HasDashSkill) return false;
            return true;
        }
    }
    #endregion

    #region [Events]
    public System.Action<Vector3, Vector3, int> OnMagicCast; 
    public System.Action OnCooldownStarted;
    #endregion

    #region [Initialization] 초기화

    public void Initialize(PlayerController controller, GameDataManager gameDataManager)
    {
        _controller = controller;
        _gameDataManager = gameDataManager;
        _characterData = gameDataManager.CharacterService.GetCharacter(_controller.CharacterIndex);
        
        if (_controller != null)
        {
            _changeDetector = _controller.MagicChangeDetector;
        }
        
        InitializeMagicHandlers();
    }

    private void InitializeMagicHandlers()
    {
        _magicHandlers.Clear();
        
        var barrierHandler = GetComponent<BarrierMagicHandler>() ?? gameObject.AddComponent<BarrierMagicHandler>();
        barrierHandler.Initialize(this, _gameDataManager);
        _magicHandlers[10] = barrierHandler;
        
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
        
        UpdateUIVisuals();
        UpdateAnchorVisual();
    }

    private void InitializeUIStructure()
    {
        if (_magicViewObj == null) return;

        _magicViewObj.transform.localPosition = Vector3.zero;
        
        Transform parent = _magicViewObj.transform;
        if (_magicIdleFirstFloor) _magicIdleFirstFloor.transform.SetParent(parent, false);
        if (_magicIdleSecondFloor) _magicIdleSecondFloor.transform.SetParent(parent, false);
        if (_magicInsideFloor) _magicInsideFloor.transform.SetParent(parent, false);

        if (_magicAnchor != null)
        {
            var collision = _magicAnchor.GetComponent<MagicAnchorCollision>() ?? _magicAnchor.AddComponent<MagicAnchorCollision>();
            collision.Initialize(this);
        }

        SetUIActiveRecursively(false);
    }

    #endregion

    #region [Core Logic] 입력 처리 (Input Processing)

    /// <summary>
    /// 매 프레임(FixedUpdateNetwork) 호출되어 플레이어의 입력을 처리합니다.
    /// </summary>
    public void ProcessInput(InputData inputData, PlayerController controller, bool isTestMode)
    {
        if (!_isInitialized || !controller.Object.HasInputAuthority) return;
        if (isTestMode && inputData.ControlledSlot != controller.PlayerSlot) return;

        // 1. 마법 선택 모드 상태 확인
        bool isSelectionMode = IsMagicSelectionModeActive();

        // 2. [선택 모드 종료 처리] 선택 모드에서 막 빠져나온 프레임
        // (선택을 위해 눌렀던 클릭이 마법 발동으로 이어지는 것을 방지)
        if (_wasInSelectionMode && !isSelectionMode)
        {
            _prevButtons = inputData.MouseButtons; // 버튼 상태만 갱신하고 입력 무시
            _wasInSelectionMode = false;
            return;
        }

        // 3. [선택 모드 유지 처리] 현재 선택 모드인 경우
        if (isSelectionMode)
        {
            // [중요 수정] 선택 모드라면 무조건 마법을 비활성화(Deactivate)
            // 활성화된 상태로 진입했거나, 진입 중에 켜져있다면 즉시 끕니다.
            if (controller.MagicActive)
            {
                controller.RPC_DeactivateMagic();
            }

            // 입력 소비 및 상태 업데이트
            _prevButtons = inputData.MouseButtons;
            _wasInSelectionMode = true;
            _currentHandler?.Update(); // 핸들러 로직만 유지 (필요하다면)
            return; // 마법 발사 로직 진입 차단
        }

        _wasInSelectionMode = false; // 선택 모드 아님

        // 4. 입력 버튼 상태 갱신
        NetworkButtons currentButtons = inputData.MouseButtons;
        NetworkButtons pressedButtons = inputData.GetMouseButtonPressed(_prevButtons);
        _prevButtons = currentButtons;

        // 5. 핸들러 동기화
        SyncHandlerChoice();

        // 6. 기타 차단 조건 (돌진 스킬 사용 중 등)
        if (controller.HasDashSkill)
        {
            _currentHandler?.Update();
            return;
        }

        // 7. 입력 데이터 추출
        bool leftClick = pressedButtons.IsSet(InputMouseButton.LEFT);
        bool rightClick = pressedButtons.IsSet(InputMouseButton.RIGHT);
        Vector3 mousePos = GetMouseWorldPosition(inputData);

        // 8. 상태별 로직 실행
        if (!controller.MagicActive)
        {
            HandleInactiveState(leftClick, rightClick, controller);
        }
        else
        {
            HandleActiveState(inputData, leftClick, rightClick, mousePos, controller);
        }

        // 9. 핸들러 업데이트
        _currentHandler?.Update();
    }

    /// <summary>
    /// 마법이 활성화되지 않았을 때의 입력 처리 (슬롯 선택 및 활성화)
    /// </summary>
    private void HandleInactiveState(bool leftClick, bool rightClick, PlayerController controller)
    {
        if (leftClick)
        {
            controller.RPC_ActivateMagic(1, controller.Magic1Code);
        }
        else if (rightClick)
        {
            controller.RPC_ActivateMagic(2, controller.Magic2Code);
        }
    }

    /// <summary>
    /// 마법이 이미 활성화된 상태에서의 입력 처리 (교체, 발사, 조준)
    /// </summary>
    private void HandleActiveState(InputData inputData, bool leftClick, bool rightClick, Vector3 mousePos, PlayerController controller)
    {
        // 1. 교체 로직 (Swap)
        if (leftClick && controller.ActiveMagicSlotNetworked == 2)
        {
            controller.RPC_ActivateMagic(1, controller.Magic1Code);
            return;
        }
        if (rightClick && controller.ActiveMagicSlotNetworked == 1)
        {
            controller.RPC_ActivateMagic(2, controller.Magic2Code);
            return;
        }

        // 2. 발사 로직 (Cast)
        if (leftClick || rightClick)
        {
            TryCastMagic(mousePos, controller);
        }
        // 3. 조준 로직 (Aim)
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

    #region [Core Logic] 마법 시전 및 핸들러 (Casting & Handler)

    private void TryCastMagic(Vector3 targetPos, PlayerController controller)
    {
        if (_currentHandler != null && _currentHandler.IsCasting()) return;

        if (!_controller.Object.HasStateAuthority)
        {
            _controller.RPC_CastMagic(targetPos);
            return;
        }

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

        if (_magicHandlers.TryGetValue(magicCode, out var handler))
        {
            handler.CastMagic(targetPosition);
            return;
        }

        Vector3 startPos = _controller.transform.position;
        Vector3 direction = (targetPosition - startPos).normalized;
        
        OnMagicCast?.Invoke(startPos, direction, magicCode);
        OnCooldownStarted?.Invoke();
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

    #endregion

    #region [Visuals & Updates] 렌더링 및 UI 업데이트

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

    #region [Interaction & Utils] 상호작용 및 유틸리티

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

    public void UpdateMagicUIState(bool isActive) => UpdateUIVisuals();
    public bool GetPreviousLeftMouseButton() => _prevButtons.IsSet(InputMouseButton.LEFT);
    public bool GetPreviousRightMouseButton() => _prevButtons.IsSet(InputMouseButton.RIGHT);
    
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

    private bool IsMagicSelectionModeActive()
    {
        if (GameManager.Instance == null) return false;
        if (GameManager.Instance.Canvas is MainCanvas mainCanvas)
        {
            return mainCanvas.IsMagicSelectionMode;
        }
        return false;
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

    /// <summary>
    /// 모든 등록된 합체 마법 핸들러의 StopMagic을 호출합니다.
    /// </summary>
    public void StopAllMagics()
    {
        foreach (var handler in _magicHandlers.Values)
        {
            handler.StopMagic();
        }
    }

    #endregion
}