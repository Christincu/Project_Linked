using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어의 마법 공격을 담당하는 컨트롤러
/// 마법 UI 표시, 마법 발사, 쿨다운, 마나 관리 등을 처리합니다.
/// </summary>
public class PlayerMagicController : MonoBehaviour
{
    #region Private Fields - Prefabs
    // 프리팹은 MagicData와 BarrierMagicCombinationData에서 가져옴
    #endregion
    
    #region Private Fields - Handlers
    private Dictionary<int, ICombinedMagicHandler> _magicHandlers = new Dictionary<int, ICombinedMagicHandler>();
    private ICombinedMagicHandler _currentHandler = null;
    #endregion

    #region Private Fields
    private GameDataManager _gameDataManager;
    private PlayerController _controller;
    private CharacterData _characterData;
    private bool _isInitialized = false; // 초기화 완료 플래그

    // Magic UI References (from PlayerController)
    private GameObject _magicViewObj; // 실제로 움직이는 뷰 오브젝트 (IdleFirst, IdleSecond, Active의 부모)
    private GameObject _magicAnchor;
    private GameObject _magicIdleFirstFloor;
    private GameObject _magicIdleSecondFloor;
    private GameObject _magicInsideFloor;

    private SpriteRenderer _idleFirstFloorRenderer;
    private SpriteRenderer _idleSecondFloorRenderer;
    private SpriteRenderer _activeInsideRenderer;

    //private bool _isPlayerColliding = false; // 다른 플레이어와 충돌 중인지
    private PlayerController _collidingPlayer = null; // 충돌 중인 플레이어

    private bool _magicUIToggleActive = false; // 토글 상태: 마법 UI가 활성화되어 있는지
    private float _magicActivationTime = 0f; // 마법을 활성화한 시간 (Time.time)
    
    // 입력 상태 보관 (Click 감지용)
    private NetworkButtons _prevButtons;
    
    // 네트워크 변경 감지 (PlayerController의 ChangeDetector 사용)
    private NetworkBehaviour.ChangeDetector _changeDetector;

    #endregion

    #region Properties
    /// <summary>
    /// 연결된 PlayerController
    /// </summary>
    public PlayerController Controller => _controller;

    /// <summary>
    /// 마법을 시전할 수 있는지 확인
    /// </summary>
    public bool CanCastMagic
    {
        get
        {
            if (_controller == null) return false;
            if (_controller.IsDead) return false;
            return true;
        }
    }

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
    
    /// <summary>
    /// 이전 프레임의 좌클릭 상태 (핸들러에서 사용)
    /// </summary>
    public bool GetPreviousLeftMouseButton() => _prevButtons.IsSet(InputMouseButton.LEFT);
    
    /// <summary>
    /// 이전 프레임의 우클릭 상태 (핸들러에서 사용)
    /// </summary>
    public bool GetPreviousRightMouseButton() => _prevButtons.IsSet(InputMouseButton.RIGHT);
    
    /// <summary>
    /// 보호막 마법 오브젝트 프리팹 가져오기 (데이터에서 가져옴)
    /// </summary>
    public NetworkPrefabRef GetBarrierMagicObjectPrefab()
    {
        if (_gameDataManager == null || _gameDataManager.MagicService == null) return default;
        
        // 베리어 마법 코드는 10 (Air + Soil 조합)
        MagicCombinationData combinationData = _gameDataManager.MagicService.GetCombinationDataByResult(10);
        
        if (combinationData is BarrierMagicCombinationData barrierData)
        {
            return barrierData.barrierMagicObjectPrefab;
        }
        
        return default;
    }
    
    /// <summary>
    /// 마법 발사체 프리팹 가져오기 (데이터에서 가져옴)
    /// </summary>
    public GameObject GetMagicProjectilePrefab(int magicCode)
    {
        if (_gameDataManager == null || _gameDataManager.MagicService == null) return null;
        
        MagicData magicData = _gameDataManager.MagicService.GetMagic(magicCode);
        if (magicData != null)
        {
            return magicData.magicProjectilePrefab;
        }
        
        return null;
    }
    
    /// <summary>
    /// 베리어 마법 핸들러 가져오기
    /// </summary>
    public BarrierMagicHandler GetBarrierMagicHandler()
    {
        if (_magicHandlers.TryGetValue(10, out var handler) && handler is BarrierMagicHandler barrierHandler)
        {
            return barrierHandler;
        }
        return null;
    }
    
    /// <summary>
    /// 베리어 핸들러 가져오기 (헬퍼)
    /// </summary>
    public ICombinedMagicHandler GetBarrierHandler()
    {
        return _magicHandlers.TryGetValue(10, out var h) ? (BarrierMagicHandler)h : null;
    }
    #endregion

    #region Events
    public System.Action<Vector3, Vector3, int> OnMagicCast; // (position, direction, magicCode)
    public System.Action OnCooldownStarted;
    public System.Action OnCooldownEnded;
    #endregion

    #region Initialization
    /// <summary>
    /// PlayerController에서 호출하여 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController controller, GameDataManager gameDataManager)
    {
        _controller = controller;
        _gameDataManager = gameDataManager;
        _characterData = gameDataManager.CharacterService.GetCharacter(_controller.CharacterIndex);
        
        // ChangeDetector 초기화 (PlayerController의 ChangeDetector 사용)
        if (_controller != null)
        {
            _changeDetector = _controller.MagicChangeDetector;
        }
        
        // 합체 마법 핸들러 초기화
        InitializeMagicHandlers();
    }
    
    /// <summary>
    /// 합체 마법 핸들러를 초기화합니다.
    /// </summary>
    private void InitializeMagicHandlers()
    {
        _magicHandlers.Clear();
        
        // 기존에 붙어있는지 확인 후 없으면 추가 (중복 방지)
        var barrierHandler = GetComponent<BarrierMagicHandler>() ?? gameObject.AddComponent<BarrierMagicHandler>();
        barrierHandler.Initialize(this, _gameDataManager);
        _magicHandlers[10] = barrierHandler; // 마법 코드 10
        
        var dashHandler = GetComponent<DashMagicHandler>() ?? gameObject.AddComponent<DashMagicHandler>();
        dashHandler.Initialize(this, _gameDataManager);
        _magicHandlers[11] = dashHandler; // 마법 코드 11
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
        _magicInsideFloor = active;


        // SpriteRenderer 가져오기
        if (_magicIdleFirstFloor != null)
            _idleFirstFloorRenderer = _magicIdleFirstFloor.GetComponent<SpriteRenderer>();
        if (_magicIdleSecondFloor != null)
            _idleSecondFloorRenderer = _magicIdleSecondFloor.GetComponent<SpriteRenderer>();
        if (_magicInsideFloor != null)
            _activeInsideRenderer = _magicInsideFloor.GetComponent<SpriteRenderer>();

        // Magic UI 초기화
        InitializeMagicUI();

        _isInitialized = true; // 초기화 완료
    }

    /// <summary>
    /// Magic UI를 초기화합니다.
    /// </summary>
    private void InitializeMagicUI()
    {
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
            if (_magicInsideFloor != null && _magicInsideFloor.transform.parent != _magicViewObj.transform)
            {
                _magicInsideFloor.transform.SetParent(_magicViewObj.transform, false);
            }
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
        if (_magicInsideFloor != null)
            _magicInsideFloor.SetActive(false);
    }
    #endregion

    #region Magic Casting
    /// <summary>
    /// 마법을 시전합니다.
    /// ActivatedMagicCode와 AbsorbedMagicCode를 확인하여 조합 마법 또는 단일 마법을 시전합니다.
    /// </summary>
    /// <param name="targetPosition">목표 위치 (월드 좌표)</param>
    public void CastMagic(Vector3 targetPosition)
    {
        if (_controller == null) return;
        if (!_controller.Object.HasStateAuthority) return;
        
        // 시전 가능 여부 확인
        if (!CanCastMagic) return;
        if (_controller.State == null) return;

        // 시전할 마법 코드 결정 (마법 합체 후 바로 정보를 가져옴)
        int magicCodeToCast = GetCurrentCombinedCode();
        
        if (magicCodeToCast == -1)
        {
            // 시전할 마법이 없음 (조합 실패 또는 활성화된 마법 없음)
            Debug.LogWarning($"[CastMagic] {_controller.name} tried to cast magic but no valid magic code (activation / combination failed)");
            
            // 현재 마법 상태를 종료하여 다른 조합을 준비할 수 있게 함
            if (_controller.MagicActive)
            {
                _controller.RPC_DeactivateMagic();
            }
            return;
        }

        // 발사 방향 계산
        Vector3 startPos = _controller.transform.position;
        Vector3 direction = (targetPosition - startPos).normalized;

        // 합체 마법 핸들러 확인
        if (_magicHandlers.TryGetValue(magicCodeToCast, out var handler))
        {
            // 핸들러가 있으면 핸들러를 통해 처리
            _currentHandler = handler;
            bool castResult = handler.CastMagic(targetPosition);
            
            if (!castResult)
            {
                // 핸들러가 시전하지 않으면 (선택 모드 등) 여기서 종료
                return;
            }
        }
        else
        {
            // 일반 마법 시전
            OnMagicCast?.Invoke(startPos, direction, magicCodeToCast);
            OnCooldownStarted?.Invoke();
        }
    }
    
    /// <summary>
    /// 실제 시전될 마법 코드(조합 고려)를 반환합니다.
    /// </summary>
    private int GetCurrentCombinedCode()
    {
        if (_controller == null) return -1;
        
        if (_controller.AbsorbedMagicCode != -1 && _controller.ActivatedMagicCode != -1)
        {
            if (_gameDataManager?.MagicService == null) return _controller.ActivatedMagicCode;
            
            int combinedCode = _gameDataManager.MagicService.GetCombinedMagic(
                _controller.ActivatedMagicCode, 
                _controller.AbsorbedMagicCode
            );
            // 조합 마법이 없으면 -1 반환 (해당 순서의 조합이 없으므로 시전 무시)
            return combinedCode;
        }
        return _controller.ActivatedMagicCode;
    }
    
    #endregion

    #region Magic UI Control
    /// <summary>
    /// 마법 UI 상태를 업데이트합니다. (상태 변경 시에만 호출) - 레거시 호환
    /// </summary>
    public void UpdateMagicUIState(bool isActive = false, Vector3? anchorPosition = null)
    {
        if (!_isInitialized) return;

        // 네트워크 상태와 로컬 상태 동기화
        _magicUIToggleActive = isActive;

        if (isActive)
        {
            // 활성화 시간 기록
            if (_magicActivationTime == 0f)
                _magicActivationTime = Time.time;
        }
        else
        {
            // 활성화 시간 리셋
            _magicActivationTime = 0f;
        }

        // UI 상태 동기화 (내부적으로 SyncUIState 호출)
        SyncUIState();
        
        // 앵커 위치 업데이트 (제공된 경우)
        if (anchorPosition.HasValue)
        {
            UpdateAnchorPosition(anchorPosition.Value);
        }
    }
    
    #region Visuals & Synchronization (Called from Render)
    
    /// <summary>
    /// 렌더링 틱에서 호출되는 시각적 업데이트
    /// </summary>
    public void OnRender()
    {
        // 초기화 전이면 처리하지 않음
        if (!_isInitialized || _controller == null) return;
        
        // 1. 네트워크 상태 변화에 따라 핸들러/UI 동기화
        SyncHandler();
        SyncUIState();
        
        // 2. 앵커 위치 보간 (스르륵 이동 효과)
        if (_magicAnchor != null && _controller.MagicActive)
        {
            // 현재 위치(_magicAnchor.transform.localPosition)에서 
            // 네트워크로 동기화된 목표 위치(_controller.MagicAnchorLocalPosition)로
            // 부드럽게 이동 (Time.deltaTime * 속도)
            
            float lerpSpeed = 15f; // 숫자가 클수록 빠름 (15 정도면 적당히 빠르고 부드러움)

            _magicAnchor.transform.localPosition = Vector3.Lerp(
                _magicAnchor.transform.localPosition,
                _controller.MagicAnchorLocalPosition,
                Time.deltaTime * lerpSpeed
            );
        }
    }
    
    /// <summary>
    /// 현재 마법 코드에 맞는 핸들러를 동기화합니다.
    /// FixedUpdateNetwork와 Render 양쪽에서 호출되어 상태 불일치를 방지합니다.
    /// </summary>
    private void SyncHandler()
    {
        // 초기화 전이거나 컨트롤러가 없으면 처리하지 않음
        if (_controller == null || !_isInitialized) return;
        
        if (!_controller.MagicActive)
        {
            if (_currentHandler != null)
            {
                _currentHandler.OnMagicDeactivated();
                _currentHandler = null;
            }
            return;
        }
        
        int targetCode = GetCurrentCombinedCode();
        
        // 핸들러가 없거나 코드가 변경되었다면 교체
        if (_currentHandler == null || _currentHandler.MagicCode != targetCode)
        {
            _currentHandler?.OnMagicDeactivated(); // 기존 종료
            
            if (_magicHandlers.TryGetValue(targetCode, out var newHandler))
            {
                _currentHandler = newHandler;
                _currentHandler.OnMagicActivated(); // 새 핸들러 시작
            }
            else
            {
                _currentHandler = null; // 일반 마법
            }
        }
    }
    
    /// <summary>
    /// UI 상태를 동기화합니다. (핵심 로직)
    /// UpdateMagicUIState와 OnRender에서 호출됩니다.
    /// </summary>
    private void SyncUIState()
    {
        if (_controller == null) return;
        
        bool isActive = _controller.MagicActive;
        
        // UI 오브젝트 활성화/비활성화
        if (_magicViewObj != null)
            _magicViewObj.SetActive(isActive);
        if (_magicAnchor != null)
            _magicAnchor.SetActive(isActive);
            
        if (isActive)
        {
            // 스프라이트 업데이트 (Activated / Absorbed 코드 기반)
            UpdateSprites();
        }
        else
        {
            // 비활성화 시 스프라이트 숨기기
            if (_magicIdleFirstFloor != null)
                _magicIdleFirstFloor.SetActive(false);
            if (_magicIdleSecondFloor != null)
                _magicIdleSecondFloor.SetActive(false);
            if (_magicInsideFloor != null)
                _magicInsideFloor.SetActive(false);
        }
    }
    
    /// <summary>
    /// 스프라이트를 업데이트합니다.
    /// </summary>
    private void UpdateSprites()
    {
        // 활성화된 마법 스프라이트 설정
        if (_controller.ActivatedMagicCode != -1)
        {
            var data = _gameDataManager.MagicService.GetMagic(_controller.ActivatedMagicCode);
            if (data != null)
            {
                if (_idleFirstFloorRenderer != null)
                    _idleFirstFloorRenderer.sprite = data.magicIdleSprite;
                if (_activeInsideRenderer != null)
                    _activeInsideRenderer.sprite = data.magicInsideSprite;
            }
            if (_magicIdleFirstFloor != null)
                _magicIdleFirstFloor.SetActive(true);
            if (_magicInsideFloor != null)
                _magicInsideFloor.SetActive(true);
        }
        else
        {
            if (_magicIdleFirstFloor != null)
                _magicIdleFirstFloor.SetActive(false);
            if (_magicInsideFloor != null)
                _magicInsideFloor.SetActive(false);
        }
        
        // 흡수된 마법 스프라이트 설정
        if (_controller.AbsorbedMagicCode != -1)
        {
            var data = _gameDataManager.MagicService.GetMagic(_controller.AbsorbedMagicCode);
            if (data != null && _idleSecondFloorRenderer != null)
            {
                _idleSecondFloorRenderer.sprite = data.magicIdleSprite;
            }
            if (_magicIdleSecondFloor != null)
                _magicIdleSecondFloor.SetActive(true);
        }
        else
        {
            if (_magicIdleSecondFloor != null)
                _magicIdleSecondFloor.SetActive(false);
        }
    }
    
    /// <summary>
    /// 마법 UI 시각적 업데이트 (부드러운 보간) - 레거시 호환
    /// </summary>
    public void UpdateMagicVisuals()
    {
        OnRender();
    }
    
    #endregion

    /// <summary>
    /// UI 활성화/비활성화를 처리합니다.
    /// </summary>
    private void SetMagicUIActive(bool active)
    {
        _magicAnchor.SetActive(active);
        _magicViewObj.SetActive(active);
        _magicIdleFirstFloor.SetActive(active);
        _magicInsideFloor.SetActive(active);

        if (!active)
        {
            _magicViewObj.transform.localPosition = Vector3.zero;
            _magicViewObj.SetActive(false);
            _magicIdleSecondFloor.SetActive(false);
        }
    }

    /// <summary>
    /// 앵커 위치를 업데이트합니다.
    /// </summary>
    public void UpdateAnchorPosition(Vector3 localPosition)
    {
        if (_magicAnchor != null)
        {
            _magicAnchor.transform.localPosition = localPosition;
            if (_controller.Object.HasInputAuthority)
            {
                _controller.MagicAnchorLocalPosition = localPosition;
            }
        }
    }
    
    /// <summary>
    /// 마우스 위치를 기반으로 앵커의 목표 위치(좌/우)를 계산하고 업데이트합니다.
    /// </summary>
    private void UpdateAnchorPosition(Vector3 mousePos, PlayerController controller)
    {
        Vector3 playerPos = controller.transform.position;
        float directionX = mousePos.x - playerPos.x;
        
        // 현재 앵커의 로컬 위치 설정값 가져오기
        // (주의: transform.localPosition이 아니라 설정된 거리값을 기준으로 해야 함)
        // 여기서는 단순히 X축 거리의 절댓값을 사용
        Vector3 currentLocal = _magicAnchor != null ? _magicAnchor.transform.localPosition : _controller.MagicAnchorLocalPosition;
        float dist = Mathf.Abs(currentLocal.x); 
        if (dist < 0.1f) dist = 1f; // 기본 거리 안전장치
        
        // 마우스 방향에 따라 목표 X 좌표 결정 (좌 or 우)
        float targetX = directionX >= 0 ? dist : -dist;
        
        // 목표 로컬 위치 생성 (Y, Z는 유지)
        Vector3 targetLocal = new Vector3(targetX, currentLocal.y, currentLocal.z);
        
        // 목표값이 변경되었을 때만 RPC 전송 (네트워크 대역폭 절약)
        if (Mathf.Abs(targetLocal.x - _controller.MagicAnchorLocalPosition.x) > 0.01f)
        {
            _controller.RPC_UpdateMagicAnchorPosition(targetLocal);
        }
    }

    /// <summary>
    /// 네트워크 틱마다 호출되는 메인 로직
    /// </summary>
    public void ProcessInput(InputData inputData, PlayerController controller, bool isTestMode)
    {
        if (!_isInitialized || !controller.Object.HasInputAuthority) return;
        if (isTestMode && inputData.ControlledSlot != controller.PlayerSlot) return;

        // [핵심 수정] 돌진 스킬 사용 중이면 모든 마법 입력 차단
        // 이미 DashMagicObject가 생성되어 이동 중이라면 마법 조작을 막아야 함
        if (controller.HasDashSkill)
        {
            // 클릭 상태만 업데이트하고 리턴 (다음 프레임을 위해)
            _prevButtons = inputData.MouseButtons;

            // 현재 실행 중인 핸들러(돌진) 업데이트는 계속 허용 (카메라 제어 등을 위해)
            SyncHandler();
            _currentHandler?.Update();
            return;
        }

        // 1. 핸들러 상태 동기화 (입력 처리 전 필수)
        SyncHandler();

        // 2. 클릭 감지
        NetworkButtons pressed = inputData.GetMouseButtonPressed(_prevButtons);
        bool leftClick = pressed.IsSet(InputMouseButton.LEFT);
        bool rightClick = pressed.IsSet(InputMouseButton.RIGHT);

        // 3. 상태별 분기 처리
        if (!controller.MagicActive)
        {
            HandleInactiveState(leftClick, rightClick);
        }
        else
        {
            HandleActiveState(inputData, leftClick, rightClick, controller);
        }

        // 4. 이전 버튼 상태 저장
        _prevButtons = inputData.MouseButtons;
        
        // 5. 핸들러 업데이트 (시각효과 등)
        _currentHandler?.Update();
    }
    
    /// <summary>
    /// 마법이 비활성화 상태일 때의 입력 처리 (활성화 요청)
    /// </summary>
    private void HandleInactiveState(bool leftClick, bool rightClick)
    {
        if (leftClick)
        {
            _controller.RPC_ActivateMagic(1, _characterData.magicData1.magicCode);
        }
        else if (rightClick)
        {
            _controller.RPC_ActivateMagic(2, _characterData.magicData2.magicCode);
        }
    }
    
    /// <summary>
    /// 마법이 활성화 상태일 때의 입력 처리 (교체 또는 시전)
    /// </summary>
    private void HandleActiveState(InputData inputData, bool leftClick, bool rightClick, PlayerController controller)
    {
        // 마우스 위치 계산
        Vector3 mousePos = GetMouseWorldPosition(inputData);
        
        // A. 다른 슬롯의 마법을 눌렀다면 -> 교체
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
        
        // B. 같은 슬롯을 눌렀다면 -> 시전 (또는 핸들러 위임)
        if (leftClick || rightClick)
        {
            TryCastMagic(mousePos);
        }
        // C. 클릭이 아니라면 -> 조준/준비 동작
        else
        {
            // 앵커 위치 업데이트
            UpdateAnchorPosition(mousePos, controller);
            
            // 핸들러가 있다면 입력 위임 (예: 범위 표시 업데이트)
            _currentHandler?.ProcessInput(inputData, mousePos);
        }
    }
    
    /// <summary>
    /// 마법 시전을 시도합니다.
    /// </summary>
    private void TryCastMagic(Vector3 targetPos)
    {
        // 현재 핸들러가 시전 중이면 입력 무시 (예: 돌진 중)
        if (_currentHandler != null && _currentHandler.IsCasting()) return;
        
        int code = GetCurrentCombinedCode();

        // 유효한 마법 코드가 없으면 시전하지 않음
        // (조합 실패 또는 활성화된 마법 없음)
        if (code == -1)
        {
            // 조합 실패 시 현재 활성화된 마법을 종료하여
            // 플레이어가 새로운 조합을 시도할 수 있게 함
            if (_controller.MagicActive)
            {
                _controller.RPC_DeactivateMagic();
            }
            return;
        }
        
        // 1. 특수 핸들러(합체 마법) 처리
        if (_magicHandlers.TryGetValue(code, out var handler))
        {
            // 핸들러 내부 로직 실행
            // handler.CastMagic이 true를 반환하면 시전 성공으로 간주하고 마법 비활성화
            if (handler.CastMagic(targetPos))
            {
                _controller.RPC_DeactivateMagic();
            }
            // false 반환 시 (조건 불충족 등) 마법 상태 유지 (예: BarrierMagicHandler의 타겟팅 모드)
        }
        // 2. 일반 투사체 마법 처리
        else
        {
            _controller.RPC_CastMagic(targetPos);
            _controller.RPC_DeactivateMagic(); // 일반 마법은 시전 후 즉시 비활성화
        }
    }

    /// <summary>
    /// 마우스 월드 위치를 가져옵니다.
    /// </summary>
    private Vector3 GetMouseWorldPosition(InputData inputData)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return Vector3.zero;

        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(inputData.MousePosition);
        mouseWorldPos.z = 0; // 2D 게임이므로 z는 0
        return mouseWorldPos;
    }



    /// <summary>
    /// IdleSecondFloor를 AbsorbedMagicCode에 따라 업데이트합니다. - 레거시 호환
    /// </summary>
    public void UpdateMagicUiSprite()
    {
        UpdateSprites();
    }

    /// <summary>
    /// MagicAnchorCollision에서 호출: 다른 마법 앵커와의 충돌 시작
    /// 충돌 시 즉시 흡수 처리
    /// [중요] State Authority 검사를 강화하여 데이터 불일치 방지
    /// </summary>
    public void OnPlayerCollisionEnter(PlayerController otherPlayer)
    {
        // 1. 기본 유효성 검사
        if (otherPlayer == null || otherPlayer.IsDead) return;
        if (otherPlayer == _controller) return;
        if (otherPlayer.MagicController == null) return;
        if (!_controller.MagicActive || !otherPlayer.MagicActive) return;
        
        // [중요] 네트워크 상태 변경 권한이 있는 쪽(State Authority)에서만 로직 수행
        // Fusion에서 상태 변경은 서버(호스트)가 주도하는 것이 가장 안전합니다.
        if (!_controller.Object.HasStateAuthority) return;
        
        // 2. 중복 처리 방지 (ID 비교)
        // 두 플레이어 모두 State Authority를 가질 수 있는 상황(예: Shared Mode)이나
        // Host Mode에서 서버가 충돌을 처리할 때 중복 계산을 막기 위함
        if (_controller.Object.Id.Raw > otherPlayer.Object.Id.Raw) return;

        _collidingPlayer = otherPlayer;
        
        // 3. 양쪽 플레이어 정보 캡처
        int myTick = _controller.MagicActivationTick;
        int otherTick = otherPlayer.MagicActivationTick;
        
        if (myTick <= 0 || otherTick <= 0) return;
        
        // 4. 흡수자 판별 (결정론적 로직)
        bool iAmAbsorber = DetermineAbsorber(myTick, otherTick, 
            _controller.CharacterIndex, otherPlayer.CharacterIndex,
            _controller.Object.Id.Raw, otherPlayer.Object.Id.Raw);
        
        PlayerController absorber = iAmAbsorber ? _controller : otherPlayer;
        PlayerController absorbed = iAmAbsorber ? otherPlayer : _controller;
        int absorbedMagicCode = absorbed.ActivatedMagicCode;
        
        // 5. 상태 변경 적용 (직접 수정)
        // State Authority를 가지고 있으므로 RPC 없이 바로 Networked 변수 수정 가능
        // 피흡수자 처리
        absorbed.MagicActive = false;
        absorbed.MagicActivationTick = 0;
        absorbed.ActivatedMagicCode = -1;
        absorbed.ActiveMagicSlotNetworked = 0;
        absorbed.MagicController?.OnAbsorbed();
        
        // 흡수자 처리
        absorber.AbsorbedMagicCode = absorbedMagicCode;
        
        Debug.Log($"[Absorption] Server handled: {absorber.name} absorbed {absorbed.name}'s magic (code: {absorbedMagicCode})");
    }
    
    /// <summary>
    /// 누가 흡수할지 결정합니다.
    /// </summary>
    private bool DetermineAbsorber(int myTick, int otherTick, int myCharIndex, int otherCharIndex, uint myObjectId, uint otherObjectId)
    {
        // 1. 활성화 틱으로 비교 (먼저 활성화한 쪽이 흡수)
        if (myTick != otherTick)
            return myTick < otherTick;
        
        // 2. 동시 활성화: 캐릭터 인덱스로 결정
        if (myCharIndex != otherCharIndex)
            return myCharIndex < otherCharIndex;
        
        // 3. 캐릭터도 같으면: Object ID로 결정 (결정론적)
        return myObjectId < otherObjectId;
    }


    /// <summary>
    /// 마법이 다른 플레이어에게 흡수당했을 때 호출됩니다.
    /// OnPlayerCollisionEnter에서 직접 호출됩니다.
    /// </summary>
    public void OnAbsorbed()
    {
        // 마법 UI 강제 비활성화
        if (_magicUIToggleActive)
            UpdateMagicUIState(false);
    }
    #endregion
}
