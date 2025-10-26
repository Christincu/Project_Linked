using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 메인 카메라를 플레이어를 따라다니도록 제어합니다.
/// 테스트 모드에서는 1/2번 키로 카메라 타겟을 전환할 수 있습니다.
/// </summary>
public class MainCameraController : MonoBehaviour
{
    #region Serialized Fields
    [Header("Camera Settings")]
    [Tooltip("카메라 이동 속도 (0-1, 낮을수록 부드럽게 따라감)")]
    [SerializeField] private float followSpeed = 0.125f;
    
    [Tooltip("카메라와 플레이어 사이의 Z축 거리")]
    [SerializeField] private float zOffset = -10f;
    
    [Tooltip("카메라 이동 데드존 (이 범위 내에서는 카메라가 움직이지 않음)")]
    [SerializeField] private float deadZone = 0.5f;
    
    [Header("Boundary Settings")]
    [Tooltip("카메라 이동 제한 활성화")]
    [SerializeField] private bool useBoundary = false;
    
    [Tooltip("카메라 이동 가능 범위 (Min X, Min Y, Max X, Max Y)")]
    [SerializeField] private Vector4 cameraBounds = new Vector4(-50f, -50f, 50f, 50f);
    #endregion
    
    #region Private Fields
    private PlayerController _targetPlayer;
    private Camera _camera;
    private bool _isInitialized = false;
    #endregion
    
    #region Properties
    public PlayerController TargetPlayer => _targetPlayer;
    #endregion
    
    #region Unity Lifecycle
    void Start()
    {
        _camera = GetComponent<Camera>();
        
        if (_camera == null)
        {
            Debug.LogError("[MainCameraController] Camera component not found!");
            enabled = false;
            return;
        }
        
        StartCoroutine(InitializeTarget());
    }
    
    void OnEnable()
    {
        if (_isInitialized)
        {
            ResetCamera();
        }
    }
    
    void LateUpdate()
    {
        if (!_isInitialized) return;
        
        if (MainGameManager.Instance != null && MainGameManager.Instance.IsTestMode)
        {
            HandleTestModeInput();
        }
        
        if (_targetPlayer != null)
        {
            if (_targetPlayer.DidTeleport)
            {
                TeleportToTarget(_targetPlayer.transform.position);
            }
            else
            {
                FollowTarget(_targetPlayer.transform.position);
            }
        }
    }
    #endregion
    
    #region Initialization
    private IEnumerator InitializeTarget()
    {
        // MainGameManager가 초기화될 때까지 대기
        while (MainGameManager.Instance == null)
        {
            yield return null;
        }
        
        // 플레이어가 스폰될 때까지 대기
        yield return new WaitForSeconds(0.5f);
        
        UpdateTargetPlayer();
        _isInitialized = true;
        
        Debug.Log($"[MainCameraController] Initialized - Target: {(_targetPlayer != null ? _targetPlayer.name : "None")}");
    }
    
    /// <summary>
    /// 타겟 플레이어를 업데이트합니다.
    /// 테스트 모드: 선택된 플레이어
    /// 일반 모드: 로컬 플레이어
    /// </summary>
    private void UpdateTargetPlayer()
    {
        if (MainGameManager.Instance == null) return;
        
        if (MainGameManager.Instance.IsTestMode)
        {
            _targetPlayer = MainGameManager.Instance.GetSelectedPlayer();
        }
        else
        {
            _targetPlayer = MainGameManager.Instance.GetLocalPlayer();
        }
        
        if (_targetPlayer != null)
        {
            Vector3 targetPos = _targetPlayer.transform.position;
            targetPos.z = zOffset;
            transform.position = targetPos;
        }
    }
    
    /// <summary>
    /// 카메라를 리셋합니다. (씬 전환 후 재초기화)
    /// </summary>
    private void ResetCamera()
    {
        StartCoroutine(ResetCameraCoroutine());
    }
    
    private IEnumerator ResetCameraCoroutine()
    {
        _isInitialized = false;
        
        while (MainGameManager.Instance == null)
        {
            yield return null;
        }
        
        yield return new WaitForSeconds(0.5f);
        
        UpdateTargetPlayer();
        _isInitialized = true;
        
        Debug.Log($"[MainCameraController] Camera reset - Target: {(_targetPlayer != null ? _targetPlayer.name : "None")}");
    }
    #endregion
    
    #region Camera Movement
    /// <summary>
    /// 타겟 위치를 부드럽게 따라갑니다.
    /// </summary>
    private void FollowTarget(Vector3 targetPosition)
    {
        Vector2 currentPos2D = new Vector2(transform.position.x, transform.position.y);
        Vector2 targetPos2D = new Vector2(targetPosition.x, targetPosition.y);
        float distance = Vector2.Distance(currentPos2D, targetPos2D);
        
        if (distance < deadZone)
        {
            return;
        }
        
        Vector3 desiredPosition = new Vector3(targetPosition.x, targetPosition.y, zOffset);
        
        if (useBoundary)
        {
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, cameraBounds.x, cameraBounds.z);
            desiredPosition.y = Mathf.Clamp(desiredPosition.y, cameraBounds.y, cameraBounds.w);
        }
        
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, followSpeed);
        transform.position = smoothedPosition;
    }
    
    /// <summary>
    /// 타겟이 텔레포트 했을 때 카메라도 즉시 따라갑니다.
    /// </summary>
    private void TeleportToTarget(Vector3 targetPosition)
    {
        Vector3 newPos = new Vector3(targetPosition.x, targetPosition.y, zOffset);
        
        if (useBoundary)
        {
            newPos.x = Mathf.Clamp(newPos.x, cameraBounds.x, cameraBounds.z);
            newPos.y = Mathf.Clamp(newPos.y, cameraBounds.y, cameraBounds.w);
        }
        
        transform.position = newPos;
    }
    #endregion
    
    #region Input Handling
    /// <summary>
    /// 테스트 모드에서 1/2번 키로 카메라 타겟 전환
    /// </summary>
    private void HandleTestModeInput()
    {
        // 1번 키: 플레이어 1 선택
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            UpdateTargetPlayer();
            Debug.Log($"[MainCameraController] Camera switched to Player 1");
        }
        
        // 2번 키: 플레이어 2 선택
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            UpdateTargetPlayer();
            Debug.Log($"[MainCameraController] Camera switched to Player 2");
        }
    }
    #endregion
    
    #region Public Methods
    /// <summary>
    /// 카메라 타겟을 수동으로 설정합니다.
    /// </summary>
    public void SetTarget(PlayerController player)
    {
        if (player != null)
        {
            _targetPlayer = player;
            
            // 즉시 위치 동기화
            Vector3 targetPos = _targetPlayer.transform.position;
            targetPos.z = zOffset;
            transform.position = targetPos;
            
            Debug.Log($"[MainCameraController] Target manually set to: {player.name}");
        }
    }
    
    /// <summary>
    /// 카메라를 특정 위치로 즉시 이동시킵니다.
    /// </summary>
    public void TeleportToPosition(Vector3 position)
    {
        Vector3 newPos = position;
        newPos.z = zOffset;
        
        if (useBoundary)
        {
            newPos.x = Mathf.Clamp(newPos.x, cameraBounds.x, cameraBounds.z);
            newPos.y = Mathf.Clamp(newPos.y, cameraBounds.y, cameraBounds.w);
        }
        
        transform.position = newPos;
    }
    
    /// <summary>
    /// 카메라 경계를 설정합니다.
    /// </summary>
    public void SetBounds(float minX, float minY, float maxX, float maxY)
    {
        cameraBounds = new Vector4(minX, minY, maxX, maxY);
        useBoundary = true;
    }
    #endregion
}
