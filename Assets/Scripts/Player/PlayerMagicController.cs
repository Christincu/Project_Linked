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

    private bool _isPlayerColliding = false; // 다른 플레이어와 충돌 중인지
    private PlayerController _collidingPlayer = null; // 충돌 중인 플레이어

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
    public void Initialize(PlayerController controller, GameDataManager gameDataManager)
    {
        _controller = controller;
        _gameDataManager = gameDataManager; 
        _characterData = gameDataManager.CharacterService.GetCharacter(_controller.CharacterIndex);
    }

    /// <summary>
    /// 매 프레임 호출되어 충돌 상태를 업데이트합니다.
    /// </summary>
    void Update()
    {
        if (!_isInitialized || _controller == null) return;
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
    /// </summary>
    /// <param name="targetPosition">목표 위치 (월드 좌표)</param>
    public void CastMagic(Vector3 targetPosition)
    {

        if (_controller == null) return;
        if (!_controller.Object.HasStateAuthority) return;

        // 시전 가능 여부 확인
        if (!CanCastMagic) return;
        if (_controller.State == null) return;

        // 발사 방향 계산
        Vector3 startPos = _controller.transform.position;
        Vector3 direction = (targetPosition - startPos).normalized;

        // 이벤트 발생
        OnMagicCast?.Invoke(startPos, direction);
        OnCooldownStarted?.Invoke();
    }
    #endregion


    #region Magic UI Control
    /// <summary>
    /// 마법 UI 상태를 업데이트합니다. (상태 변경 시에만 호출)
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

            // UI 활성화
            SetMagicUIActive(true);

            // 앵커 위치 업데이트 (제공된 경우)
            if (anchorPosition.HasValue)
                UpdateAnchorPosition(anchorPosition.Value);
        }
        else
        {
            // 활성화 시간 리셋
            _magicActivationTime = 0f;

            // UI 비활성화
            SetMagicUIActive(false);
        }

        
        UpdateMagicUiSprite();
    }

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
    /// 입력을 처리하여 마법 UI를 업데이트합니다.
    /// PlayerController에서 호출됩니다.
    /// </summary>
    public void ProcessInput(InputData inputData, PlayerController controller, bool isTestMode)
    {
        if (!_isInitialized || !controller.Object.HasInputAuthority) return;
        if (isTestMode && inputData.ControlledSlot != controller.PlayerSlot) return;

        // 클릭 감지
        bool leftClickDown = inputData.GetMouseButton(InputMouseButton.LEFT) && !_previousLeftMouseButton;
        bool rightClickDown = inputData.GetMouseButton(InputMouseButton.RIGHT) && !_previousRightMouseButton;

        if (leftClickDown || rightClickDown)
        {
            int magicCode = -1;

            if (leftClickDown)
            {
                magicCode = _characterData.magicData1.magicCode;
            }
            else if (rightClickDown)
            {
                magicCode = _characterData.magicData2.magicCode;
            }

            if (!controller.MagicActive)
            {
                controller.RPC_ActivateMagic(controller.ActiveMagicSlotNetworked, magicCode);
            }
            else if (controller.MagicActive)
            {
                controller.RPC_DeactivateMagic();
            }
        }

        // 마우스 위치 업데이트 (활성화된 경우에만)
        if (controller.MagicActive)
        {
            Vector3 mousePos = GetMouseWorldPosition(inputData);
            Vector3 anchorPos = CalculateAnchorPosition(mousePos, controller);
            UpdateAnchorPosition(anchorPos);
            _controller.RPC_UpdateMagicAnchorPosition(anchorPos);
        }

        _previousLeftMouseButton = inputData.GetMouseButton(InputMouseButton.LEFT);
        _previousRightMouseButton = inputData.GetMouseButton(InputMouseButton.RIGHT);
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
    /// 마우스 위치를 기반으로 앵커 위치를 계산합니다.
    /// </summary>
    private Vector3 CalculateAnchorPosition(Vector3 mouseWorldPos, PlayerController controller)
    {
        if (_magicAnchor == null) return Vector3.zero;

        Vector3 playerPos = controller.transform.position;
        float horizontalDirection = mouseWorldPos.x - playerPos.x;

        Vector3 anchorLocalPos = _magicAnchor.transform.localPosition;
        float distance = Mathf.Abs(anchorLocalPos.x); // 기본 거리 유지
        anchorLocalPos.x = horizontalDirection >= 0 ? distance : -distance;

        return anchorLocalPos;
    }


    /// <summary>
    /// IdleSecondFloor를 AbsorbedMagicCode에 따라 업데이트합니다.
    /// </summary>
    public void UpdateMagicUiSprite()
    {
        if(_controller.ActivatedMagicCode != -1)
        {
            MagicData activatedMagicData = _gameDataManager.MagicService.GetMagic(_controller.ActivatedMagicCode);
            _idleFirstFloorRenderer.sprite = activatedMagicData.magicIdleSprite;
            _activeInsideRenderer.sprite = activatedMagicData.magicInsideSprite;

            _magicIdleFirstFloor.SetActive(true);
            _magicInsideFloor.SetActive(true);
        }
        else
        {
            _idleFirstFloorRenderer.sprite = null;
            _activeInsideRenderer.sprite = null;
            _magicIdleFirstFloor.SetActive(false);
            _magicInsideFloor.SetActive(false);
        }

        if(_controller.AbsorbedMagicCode != -1)
        {
            MagicData absorbedMagicData = _gameDataManager.MagicService.GetMagic(_controller.AbsorbedMagicCode);
            _idleSecondFloorRenderer.sprite = absorbedMagicData.magicIdleSprite;
            
            _magicIdleSecondFloor.SetActive(true);
        }
        else
        {
            _idleSecondFloorRenderer.sprite = null;
            _magicIdleSecondFloor.SetActive(false);
        }
    }

    /// <summary>
    /// MagicAnchorCollision에서 호출: 다른 마법 앵커와의 충돌 시작
    /// 충돌 시 즉시 흡수 처리
    /// </summary>
    public void OnPlayerCollisionEnter(PlayerController otherPlayer)
    {
        // 자기 자신이 아닌 다른 플레이어와 충돌했을 때만
        if (otherPlayer == _controller || otherPlayer == null) return;
        if (otherPlayer.MagicController == null) return;

        // 둘 다 마법이 활성화되어 있어야 함
        if (!_controller.MagicActive || !otherPlayer.MagicActive) return;

        _isPlayerColliding = true;
        _collidingPlayer = otherPlayer;

        // 누가 먼저 마법을 활성화했는지 확인
        int myActivationTick = _controller.MagicActivationTick;
        int otherActivationTick = otherPlayer.MagicActivationTick;

        bool shouldBeAbsorbed = false;

        // 틱으로 비교 (더 정확함)
        if (otherActivationTick > 0 && myActivationTick > 0)
        {
            if (otherActivationTick < myActivationTick)
            {
                // 상대방이 먼저 활성화 -> 내가 흡수당함
                shouldBeAbsorbed = true;
            }
            else if (otherActivationTick == myActivationTick)
            {
                // 동시 활성화 -> 캐릭터 인덱스로 결정
                int myCharIndex = _controller.CharacterIndex;
                int otherCharIndex = otherPlayer.CharacterIndex;

                if (myCharIndex > otherCharIndex)
                {
                    shouldBeAbsorbed = true;
                }
                else if (myCharIndex == otherCharIndex)
                {
                    // 캐릭터도 같으면 Object ID로 결정 (결정론적)
                    if (_controller.Object.Id.Raw > otherPlayer.Object.Id.Raw)
                    {
                        shouldBeAbsorbed = true;
                    }
                }
            }
        }

        if (shouldBeAbsorbed)
        {
            // 내가 흡수당함 -> 상대방에게 흡수 요청
            if (_controller.Object.HasInputAuthority)
            {
                otherPlayer.RPC_AbsorbMagic(_controller.Id);
            }
        }
        else
        {
            // 내가 흡수함 -> 상대방의 마법 코드를 저장
            if (_controller.Object.HasInputAuthority)
            {
                _controller.RPC_AbsorbMagic(otherPlayer.Id);
            }
        }
    }


    /// <summary>
    /// 마법이 다른 플레이어에게 흡수당했을 때 호출됩니다.
    /// PlayerController의 RPC_NotifyAbsorbed에서 호출됩니다.
    /// </summary>
    public void OnAbsorbed()
    {
        // 마법 UI 강제 비활성화
        if (_magicUIToggleActive)
            UpdateMagicUIState(false);
    }
    #endregion
}
