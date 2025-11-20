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
    private GameObject _magicProjectilePrefab;
    private NetworkPrefabRef _barrierMagicObjectPrefab; // 보호막 마법 오브젝트 프리팹
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
    private bool _previousLeftMouseButton = false; // 이전 프레임의 좌클릭 상태
    private bool _previousRightMouseButton = false; // 이전 프레임의 우클릭 상태
    private float _magicActivationTime = 0f; // 마법을 활성화한 시간 (Time.time)

    // 보호막 마법 선택 모드
    private PlayerController _selectedPlayerForBarrier = null; // 선택된 플레이어 (보호막용)
    private Dictionary<PlayerController, GameObject> _barrierHighlightObjects = new Dictionary<PlayerController, GameObject>(); // 하이라이트 오브젝트
    private Color _barrierHighlightColor = new Color(0.2f, 1f, 0.2f, 1f); // 연두색
    private float _barrierHighlightThickness = 0.1f;

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
    public System.Action<Vector3, Vector3, int> OnMagicCast; // (position, direction, magicCode)
    public System.Action OnCooldownStarted;
    public System.Action OnCooldownEnded;
    #endregion

    #region Initialization
    /// <summary>
    /// PlayerController에서 호출하여 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController controller, GameDataManager gameDataManager, GameObject magicProjectilePrefab, NetworkPrefabRef barrierMagicObjectPrefab)
    {
        _controller = controller;
        _gameDataManager = gameDataManager;
        _characterData = gameDataManager.CharacterService.GetCharacter(_controller.CharacterIndex);
        _magicProjectilePrefab = magicProjectilePrefab;
        _barrierMagicObjectPrefab = barrierMagicObjectPrefab;
    }

    /// <summary>
    /// 매 프레임 호출되어 충돌 상태를 업데이트합니다.
    /// </summary>
    void Update()
    {
        if (!_isInitialized || _controller == null) return;
        
        // 보호막 마법 선택 모드일 때 하이라이트 업데이트 (렌더링만)
        if (IsInBarrierSelectionMode())
        {
            UpdateBarrierHighlightVisuals();
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
        int magicCodeToCast = GetMagicCodeToCast();
        
        if (magicCodeToCast == -1)
        {
            // 시전할 마법이 없음
            Debug.LogWarning($"[CastMagic] {_controller.name} tried to cast magic but no magic is activated");
            return;
        }

        // 발사 방향 계산
        Vector3 startPos = _controller.transform.position;
        Vector3 direction = (targetPosition - startPos).normalized;

        // 조합 마법 특수 처리 (보호막 마법: Air + Soil)
        if (magicCodeToCast == 10) // 조합 마법 코드 10 (Air + Soil)
        {
            // 보호막 마법은 시전하지 않고 선택 모드로 진입
            // CastMagic을 호출하지 않음 (선택 모드 유지)
            return; // 시전하지 않고 선택 모드로 유지
        }
        else
        {
            // 일반 마법 시전
            OnMagicCast?.Invoke(startPos, direction, magicCodeToCast);
            OnCooldownStarted?.Invoke();
        }
    }
    
    /// <summary>
    /// 시전할 마법 코드를 결정합니다. (마법 합체 후 바로 정보를 가져옴)
    /// </summary>
    private int GetMagicCodeToCast()
    {
        // 흡수된 마법이 있는지 확인 (조합 마법)
        if (_controller.AbsorbedMagicCode != -1 && _controller.ActivatedMagicCode != -1)
        {
            // 두 마법을 조합 (마법 합체 후 바로 정보를 가져옴)
            int combinedMagicCode = _gameDataManager.MagicService.GetCombinedMagic(
                _controller.ActivatedMagicCode, 
                _controller.AbsorbedMagicCode
            );
            
            if (combinedMagicCode != -1)
            {
                // 조합 마법 시전
                return combinedMagicCode;
            }
            else
            {
                // 조합이 없으면 활성화된 마법만 시전
                return _controller.ActivatedMagicCode;
            }
        }
        else if (_controller.ActivatedMagicCode != -1)
        {
            // 단일 마법 시전
            return _controller.ActivatedMagicCode;
        }
        
        return -1;
    }
    
    /// <summary>
    /// 보호막 마법 선택 모드인지 확인합니다.
    /// </summary>
    private bool IsInBarrierSelectionMode()
    {
        if (_controller == null || !_controller.MagicActive) return false;
        
        int magicCodeToCast = GetMagicCodeToCast();
        return magicCodeToCast == 10; // 보호막 마법 코드
    }
    
    /// <summary>
    /// 보호막 마법 선택 모드에서 플레이어 선택을 업데이트합니다. (입력 데이터 사용)
    /// </summary>
    private void UpdateBarrierSelectionWithInput(Vector3 mouseWorldPos)
    {
        if (_controller == null || !_controller.Object.HasInputAuthority) return;
        
        // 가장 가까운 플레이어 찾기 (자신 포함)
        PlayerController closestPlayer = FindClosestPlayerForBarrier(mouseWorldPos);
        
        if (closestPlayer != null && !closestPlayer.IsDead)
        {
            _selectedPlayerForBarrier = closestPlayer;
        }
        else
        {
            _selectedPlayerForBarrier = null;
        }
    }
    
    /// <summary>
    /// 보호막 마법용 가장 가까운 플레이어를 찾습니다.
    /// </summary>
    private PlayerController FindClosestPlayerForBarrier(Vector3 mouseWorldPos)
    {
        List<PlayerController> allPlayers = new List<PlayerController>();
        
        if (MainGameManager.Instance != null)
        {
            allPlayers = MainGameManager.Instance.GetAllPlayers();
        }
        
        if (allPlayers == null || allPlayers.Count == 0)
        {
            allPlayers = new List<PlayerController>(FindObjectsOfType<PlayerController>());
        }

        PlayerController closestPlayer = null;
        float closestDistance = float.MaxValue;

        foreach (var player in allPlayers)
        {
            if (player == null || player.IsDead) continue;

            Vector3 playerPos = player.transform.position;
            if (player.ViewObj != null)
            {
                playerPos = player.ViewObj.transform.position;
            }
            
            float distance = Vector3.Distance(mouseWorldPos, playerPos);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPlayer = player;
            }
        }

        return closestPlayer;
    }
    
    /// <summary>
    /// 보호막 하이라이트 시각 효과를 업데이트합니다.
    /// </summary>
    private void UpdateBarrierHighlightVisuals()
    {
        // 이전 선택된 플레이어 하이라이트 제거
        foreach (var kvp in _barrierHighlightObjects.ToList())
        {
            if (kvp.Key != _selectedPlayerForBarrier)
            {
                RemoveBarrierHighlight(kvp.Key);
            }
        }

        // 현재 선택된 플레이어 하이라이트 추가
        if (_selectedPlayerForBarrier != null && !_barrierHighlightObjects.ContainsKey(_selectedPlayerForBarrier))
        {
            AddBarrierHighlight(_selectedPlayerForBarrier);
        }
    }
    
    /// <summary>
    /// 플레이어에게 보호막 하이라이트를 추가합니다.
    /// </summary>
    private void AddBarrierHighlight(PlayerController player)
    {
        if (player == null) return;
        
        GameObject viewObj = player.ViewObj;
        if (viewObj == null)
        {
            Transform viewObjParent = player.transform.Find("ViewObjParent");
            if (viewObjParent != null && viewObjParent.childCount > 0)
            {
                viewObj = viewObjParent.GetChild(0).gameObject;
            }
        }
        
        if (viewObj == null) return;

        GameObject highlightObj = new GameObject("BarrierHighlight");
        highlightObj.transform.SetParent(viewObj.transform, false);
        
        SpriteRenderer playerRenderer = viewObj.GetComponent<SpriteRenderer>();
        if (playerRenderer != null && playerRenderer.sprite != null)
        {
            SpriteRenderer highlightRenderer = highlightObj.AddComponent<SpriteRenderer>();
            highlightRenderer.sprite = playerRenderer.sprite;
            highlightRenderer.color = _barrierHighlightColor;
            highlightRenderer.sortingOrder = playerRenderer.sortingOrder + 1;
            highlightObj.transform.localScale = Vector3.one * (1f + _barrierHighlightThickness);
        }

        _barrierHighlightObjects[player] = highlightObj;
    }
    
    /// <summary>
    /// 플레이어의 보호막 하이라이트를 제거합니다.
    /// </summary>
    private void RemoveBarrierHighlight(PlayerController player)
    {
        if (player == null || !_barrierHighlightObjects.ContainsKey(player)) return;

        GameObject highlightObj = _barrierHighlightObjects[player];
        if (highlightObj != null)
        {
            Destroy(highlightObj);
        }

        _barrierHighlightObjects.Remove(player);
    }
    
    /// <summary>
    /// 플레이어에게 보호막을 적용합니다.
    /// </summary>
    private void ApplyBarrierToPlayer(PlayerController targetPlayer)
    {
        if (targetPlayer == null || _controller == null) return;
        if (!_controller.Object.HasStateAuthority) return;

        // 모든 플레이어 가져오기
        List<PlayerController> allPlayers = new List<PlayerController>();
        
        if (MainGameManager.Instance != null)
        {
            allPlayers = MainGameManager.Instance.GetAllPlayers();
        }
        
        if (allPlayers == null || allPlayers.Count == 0)
        {
            allPlayers = new List<PlayerController>(FindObjectsOfType<PlayerController>());
        }

        // 보호막을 받은 플레이어는 체력 3, 받지 못한 플레이어는 체력 1
        foreach (var player in allPlayers)
        {
            if (player == null || player.IsDead) continue;

            if (player == targetPlayer)
            {
                // 보호막 받은 플레이어: 체력 3 및 보호막 타이머 설정
                // PlayerState의 SetHealth를 사용하여 이벤트 발생
                if (player.State != null)
                {
                    player.State.SetHealth(3f);
                }
                else
                {
                    player.CurrentHealth = 3f;
                }
                // 보호막 타이머 시작
                if (_controller.Runner != null)
                {
                    player.BarrierTimer = TickTimer.CreateFromSeconds(_controller.Runner, 5f); // 기본 5초
                    player.HasBarrier = true;
                }
                Debug.Log($"[BarrierMagic] {targetPlayer.name} received barrier (HP: {player.CurrentHealth}/{player.MaxHealth})");
            }
            else
            {
                // 보호막 받지 못한 플레이어: 체력 1
                // PlayerState의 SetHealth를 사용하여 이벤트 발생
                if (player.State != null)
                {
                    player.State.SetHealth(1f);
                }
                else
                {
                    player.CurrentHealth = 1f;
                }
                // 기존 보호막 제거
                player.BarrierTimer = TickTimer.None;
                player.HasBarrier = false;
                Debug.Log($"[BarrierMagic] {player.name} did not receive barrier (HP: {player.CurrentHealth}/{player.MaxHealth})");
            }
        }
        
        // 모든 하이라이트 제거
        foreach (var kvp in _barrierHighlightObjects)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value);
            }
        }
        _barrierHighlightObjects.Clear();
        _selectedPlayerForBarrier = null;
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
            if (!controller.MagicActive)
            {
                // 마법이 비활성화되어 있으면 활성화
                int magicCode = -1;

                if (leftClickDown)
                {
                    magicCode = _characterData.magicData1.magicCode;
                }
                else if (rightClickDown)
                {
                    magicCode = _characterData.magicData2.magicCode;
                }

                controller.RPC_ActivateMagic(controller.ActiveMagicSlotNetworked, magicCode);
            }
            else
            {
                // 마법이 활성화되어 있으면 시전 또는 보호막 적용
                int magicCodeToCast = GetMagicCodeToCast();
                
                if (magicCodeToCast == 10) // 보호막 마법
                {
                    // 보호막 선택 모드: 선택된 플레이어가 있으면 보호막 적용
                    if (_selectedPlayerForBarrier != null && !_selectedPlayerForBarrier.IsDead)
                    {
                        // 선택된 플레이어에게 보호막 적용
                        ApplyBarrierToPlayer(_selectedPlayerForBarrier);
                        
                        // 마법 비활성화
                        controller.RPC_DeactivateMagic();
                    }
                    // 선택된 플레이어가 없으면 선택 모드 유지 (시전하지 않음)
                }
                else
                {
                    // 일반 마법 시전
                    Vector3 mousePos = GetMouseWorldPosition(inputData);
                    controller.RPC_CastMagic(mousePos);
                }
            }
        }

        // 마우스 위치 업데이트 (활성화된 경우에만)
        if (controller.MagicActive)
        {
            int magicCodeToCast = GetMagicCodeToCast();
            
            if (magicCodeToCast == 10) // 보호막 마법 선택 모드
            {
                // 보호막 선택 모드: 플레이어 선택 업데이트
                Vector3 mousePos = GetMouseWorldPosition(inputData);
                UpdateBarrierSelectionWithInput(mousePos);
            }
            else
            {
                // 일반 마법: 앵커 위치 업데이트
                Vector3 mousePos = GetMouseWorldPosition(inputData);
                Vector3 anchorPos = CalculateAnchorPosition(mousePos, controller);
                UpdateAnchorPosition(anchorPos);
                _controller.RPC_UpdateMagicAnchorPosition(anchorPos);
            }
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
        if (_controller.ActivatedMagicCode != -1)
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

        if (_controller.AbsorbedMagicCode != -1)
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
        // Early exit checks
        if (otherPlayer == _controller || otherPlayer == null) return;
        if (otherPlayer.MagicController == null) return;
        if (_controller.Object.Id.Raw > otherPlayer.Object.Id.Raw) return; // 중복 처리 방지
        if (!_controller.MagicActive || !otherPlayer.MagicActive) return;

        _collidingPlayer = otherPlayer;
        
        // 양쪽 플레이어 정보 캡처
        int myTick = _controller.MagicActivationTick;
        int otherTick = otherPlayer.MagicActivationTick;
        
        if (myTick <= 0 || otherTick <= 0) return;
        
        // 누가 흡수할지 결정 (먼저 활성화한 쪽이 흡수)
        bool iAmAbsorber = DetermineAbsorber(myTick, otherTick, 
            _controller.CharacterIndex, otherPlayer.CharacterIndex,
            _controller.Object.Id.Raw, otherPlayer.Object.Id.Raw);
        
        PlayerController absorber = iAmAbsorber ? _controller : otherPlayer;
        PlayerController absorbed = iAmAbsorber ? otherPlayer : _controller;
        int absorbedMagicCode = absorbed.ActivatedMagicCode;
        
        // 흡수 처리
        absorbed.MagicActive = false;
        absorbed.MagicActivationTick = 0;
        absorbed.ActivatedMagicCode = -1;
        absorbed.ActiveMagicSlotNetworked = 0;
        absorbed.MagicController?.OnAbsorbed();
        
        absorber.AbsorbedMagicCode = absorbedMagicCode;
        
        Debug.Log($"[Absorption] {absorber.name} absorbed {absorbed.name}'s magic (code: {absorbedMagicCode})");
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
