using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어의 마법 시스템(선택, 시전, 조합, UI)을 총괄하는 컨트롤러입니다.
/// </summary>
public class PlayerMagicController : MonoBehaviour
{
    // 상수 및 타이머 변수 모두 삭제됨 (불필요)

    #region Private Fields - Dependencies
    private PlayerController _controller;
    private GameDataManager _gameDataManager;
    private CharacterData _characterData;
    private NetworkBehaviour.ChangeDetector _changeDetector;
    #endregion

    #region Private Fields - State
    private bool _isInitialized = false;
    
    // Magic Handlers
    private Dictionary<int, ICombinedMagicHandler> _magicHandlers = new Dictionary<int, ICombinedMagicHandler>();
    private ICombinedMagicHandler _currentHandler = null;

    // Input Logic
    private NetworkButtons _prevButtons;
    #endregion

    #region Private Fields - UI & Visuals
    private GameObject _magicViewObj;
    private GameObject _magicAnchor;
    private GameObject _magicIdleFirstFloor;
    private GameObject _magicIdleSecondFloor;
    private GameObject _magicInsideFloor;

    private SpriteRenderer _idleFirstFloorRenderer;
    private SpriteRenderer _idleSecondFloorRenderer;
    private SpriteRenderer _activeInsideRenderer;
    #endregion

    #region Properties
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

    #region Core Logic: Input Processing

    public void ProcessInput(InputData inputData, PlayerController controller, bool isTestMode)
    {
        if (!_isInitialized || !controller.Object.HasInputAuthority) return;
        if (isTestMode && inputData.ControlledSlot != controller.PlayerSlot) return;

        // 1. 버튼 상태 갱신 (InputData로부터 현재 프레임의 클릭 여부 판단)
        NetworkButtons currentButtons = inputData.MouseButtons;
        NetworkButtons pressedButtons = inputData.GetMouseButtonPressed(_prevButtons);
        _prevButtons = currentButtons;

        // 2. 핸들러 동기화
        SyncHandlerChoice();

        // 3. 차단 로직 (돌진 중, UI 모드)
        if (controller.HasDashSkill || IsMagicSelectionModeActive())
        {
            _currentHandler?.Update();
            return;
        }

        // 4. 입력 플래그 추출 (pressedButtons는 이번 프레임에 눌린 순간만 True)
        bool leftClick = pressedButtons.IsSet(InputMouseButton.LEFT);
        bool rightClick = pressedButtons.IsSet(InputMouseButton.RIGHT);
        Vector3 mousePos = GetMouseWorldPosition(inputData);

        // 5. 로직 분기
        if (!controller.MagicActive)
        {
            // 비활성 상태: 활성화만 수행하고 종료
            HandleInactiveState(leftClick, rightClick, controller);
        }
        else
        {
            // 활성 상태: 교체 혹은 발사
            HandleActiveState(inputData, leftClick, rightClick, mousePos, controller);
        }

        // 6. 핸들러 업데이트
        _currentHandler?.Update();
    }

    private void HandleInactiveState(bool leftClick, bool rightClick, PlayerController controller)
    {
        // 여기서 활성화를 시키면 controller.MagicActive가 다음 프레임부터 True가 됨.
        // GetMouseButtonPressed 특성상 다음 프레임에는 click이 False이므로 발사되지 않음.
        if (leftClick)
        {
            controller.RPC_ActivateMagic(1, controller.Magic1Code);
        }
        else if (rightClick)
        {
            controller.RPC_ActivateMagic(2, controller.Magic2Code);
        }
    }

    private void HandleActiveState(InputData inputData, bool leftClick, bool rightClick, Vector3 mousePos, PlayerController controller)
    {
        // 1. 교체 로직 (Return으로 확실하게 프레임 종료)
        if (leftClick && controller.ActiveMagicSlotNetworked == 2)
        {
            controller.RPC_ActivateMagic(1, controller.Magic1Code);
            return; // [핵심] 교체했으면 이번 프레임엔 발사하지 않고 끝냄
        }
        if (rightClick && controller.ActiveMagicSlotNetworked == 1)
        {
            controller.RPC_ActivateMagic(2, controller.Magic2Code);
            return; // [핵심] 교체했으면 이번 프레임엔 발사하지 않고 끝냄
        }

        // 2. 발사 로직 (교체가 아닐 때만 도달)
        if (leftClick || rightClick)
        {
            TryCastMagic(mousePos, controller);
        }
        // 3. 조준 로직
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

    #region Core Logic: Magic Casting & Handler

    private void TryCastMagic(Vector3 targetPos, PlayerController controller)
    {
        // 불필요한 타이머 체크 로직 삭제됨

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

    #region Visuals & Updates

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

    #region Interaction & Legacy Support

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

    #endregion
}