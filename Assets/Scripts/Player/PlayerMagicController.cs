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
    private GameObject _magicAnchor;
    private GameObject _magicIdleFirstFloor;
    private GameObject _magicIdleSecondFloor;
    private GameObject _magicActiveFloor;
    
    private SpriteRenderer _idleFirstFloorRenderer;
    private SpriteRenderer _idleSecondFloorRenderer;
    private SpriteRenderer _activeFloorRenderer;
    
    private bool _isPlayerColliding = false; // 다른 플레이어와 충돌 중인지
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
            if (_controller.CurrentMana < _currentMagicData.manaCost) return false;
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
    /// Magic UI 오브젝트 레퍼런스를 설정합니다. (PlayerController에서 호출)
    /// </summary>
    public void SetMagicUIReferences(GameObject anchor, GameObject idleFirst, GameObject idleSecond, GameObject active)
    {
        _magicAnchor = anchor;
        _magicIdleFirstFloor = idleFirst;
        _magicIdleSecondFloor = idleSecond;
        _magicActiveFloor = active;
        
        // 디버깅: 레퍼런스 확인
        Debug.Log($"[PlayerMagicController] Magic UI References Set - Anchor: {_magicAnchor != null}, " +
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

        // 마나 소모 (PlayerState 사용)
        if (!_controller.State.ConsumeMana(_currentMagicData.manaCost)) return;

        // 쿨다운 시작
        _controller.MagicCooldownTimer = TickTimer.CreateFromSeconds(_controller.Runner, _currentMagicData.cooldown);

        // 발사 방향 계산
        Vector3 startPos = _controller.transform.position;
        Vector3 direction = (targetPosition - startPos).normalized;

        // 마법 발사체 생성
        SpawnMagicProjectile(startPos, direction);

        Debug.Log($"[PlayerMagicController] Magic cast! Mana: {_controller.CurrentMana}/{_controller.MaxMana}");

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

    #region Mana Management (PlayerController로 위임)
    /// <summary>
    /// 마나를 회복합니다.
    /// </summary>
    public void RestoreMana(float amount)
    {
        if (_controller != null && _controller.State != null)
        {
            _controller.State.RestoreMana(amount);
        }
    }

    /// <summary>
    /// 마나를 완전히 회복합니다.
    /// </summary>
    public void FullRestoreMana()
    {
        if (_controller != null && _controller.State != null)
        {
            _controller.State.FullRestoreMana();
        }
    }

    /// <summary>
    /// 최대 마나를 변경합니다.
    /// </summary>
    public void SetMaxMana(float newMaxMana)
    {
        if (_controller != null && _controller.State != null)
        {
            _controller.State.SetMaxMana(newMaxMana);
        }
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
        
        if (isActive)
        {
            // Magic UI 활성화 (위치 업데이트 없이)
            if (_magicAnchor != null)
                _magicAnchor.SetActive(true);
            if (_magicIdleFirstFloor != null)
                _magicIdleFirstFloor.SetActive(true);
            if (_magicActiveFloor != null)
                _magicActiveFloor.SetActive(true);
        }
        else
        {
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
        
        // 좌클릭 또는 우클릭 감지
        bool leftClick = inputData.GetMouseButton(InputMouseButton.LEFT);
        bool rightClick = inputData.GetMouseButton(InputMouseButton.RIGHT);
        
        if (leftClick || rightClick)
        {
            ActivateMagicUI(controller);
            UpdateMagicAnchorPosition(inputData, controller);
        }
        else
        {
            DeactivateMagicUI(controller);
        }
        
        // 충돌 상태에 따라 IdleSecondFloor 활성화
        UpdateIdleSecondFloor();
    }
    
    /// <summary>
    /// MagicAnchor의 위치를 플레이어가 바라보는 방향(좌/우)으로 업데이트합니다.
    /// </summary>
    private void UpdateMagicAnchorPosition(InputData inputData, PlayerController controller)
    {
        if (_magicAnchor == null) return;
        
        // 마우스 위치를 월드 좌표로 변환
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;
        
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(inputData.MousePosition);
        mouseWorldPos.z = 0; // 2D 게임이므로 z는 0
        
        // 플레이어 위치
        Vector3 playerPos = controller.transform.position;
        
        // 마우스가 플레이어 기준 좌우 어디에 있는지 확인
        float horizontalDirection = mouseWorldPos.x - playerPos.x;
        
        // MagicAnchor의 로컬 위치 가져오기
        Vector3 anchorLocalPos = _magicAnchor.transform.localPosition;
        
        // 좌우 방향만 업데이트 (위아래는 고정)
        // 플레이어가 바라보는 방향(ScaleX)에 따라 거리 조정
        float distance = Mathf.Abs(anchorLocalPos.x); // 기본 거리 유지
        anchorLocalPos.x = horizontalDirection >= 0 ? distance : -distance;
        
        _magicAnchor.transform.localPosition = anchorLocalPos;
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
        
        // 네트워크 동기화 (InputAuthority가 있는 경우 네트워크 변수 업데이트)
        if (controller.Object.HasInputAuthority && !controller.MagicActive)
        {
            controller.MagicActive = true;
            Debug.Log($"[PlayerMagicController] MagicActive set to true (InputAuth: {controller.Object.HasInputAuthority}, StateAuth: {controller.Object.HasStateAuthority})");
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
    /// </summary>
    private void DeactivateMagicUI(PlayerController controller)
    {
        if (controller == null) return;
        
        // 네트워크 동기화 (InputAuthority가 있는 경우 네트워크 변수 업데이트)
        if (controller.Object.HasInputAuthority && controller.MagicActive)
        {
            controller.MagicActive = false;
            Debug.Log($"[PlayerMagicController] MagicActive set to false (InputAuth: {controller.Object.HasInputAuthority}, StateAuth: {controller.Object.HasStateAuthority})");
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
    /// </summary>
    private void UpdateIdleSecondFloor()
    {
        if (_magicIdleSecondFloor == null) return;
        
        // 다른 플레이어와 충돌 중이고, MagicAnchor가 활성화되어 있으면 표시
        bool shouldShow = _isPlayerColliding && _magicAnchor != null && _magicAnchor.activeSelf;
        _magicIdleSecondFloor.SetActive(shouldShow);
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
            Debug.Log($"[PlayerMagicController] Player collision detected");
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
            Debug.Log($"[PlayerMagicController] Player collision ended");
        }
    }
    #endregion
}
