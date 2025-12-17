using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 메인 카메라를 플레이어를 따라다니도록 제어합니다.
/// 테스트 모드에서는 1/2번 키로 카메라 타겟을 전환할 수 있습니다.
/// </summary>
public class MainCameraController : MonoBehaviour
{
    [Header("Camera Settings")]
    [SerializeField] private float _followSpeed = 0.125f;
    [SerializeField] private float _zOffset = -10f;
    [SerializeField] private float _deadZone = 0.5f;
    
    [Header("Boundary Settings")]
    [SerializeField] private bool _useBoundary = false;
    [SerializeField] private Vector4 _cameraBounds = new Vector4(-50f, -50f, 50f, 50f);

    private PlayerController _targetPlayer;
    private Camera _camera;
    private bool _isInitialized = false;
    private Vector3 _lastTargetPosition;

    public PlayerController TargetPlayer => _targetPlayer;
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
        if (!_isInitialized)
        {
            // 초기화가 안 되어 있으면 타겟 플레이어 찾기 시도
            if (MainGameManager.Instance != null)
            {
                UpdateTargetPlayer();
                if (_targetPlayer != null)
                {
                    _isInitialized = true;
                }
            }
            return;
        }
        
        // 타겟이 없거나 파괴되었으면 다시 찾기 시도
        if (_targetPlayer == null || _targetPlayer.gameObject == null || !_targetPlayer.gameObject.activeInHierarchy)
        {
            _targetPlayer = null;
            UpdateTargetPlayer();
            return;
        }
        
        // Interpolation Target이 있으면 그것을 따라가기 (더 부드러운 움직임)
        Vector3 targetPosition = GetTargetPosition();
        FollowTarget(targetPosition);

        if (MainGameManager.Instance.IsTestMode)
        {
            HandleTestModeInput();
        }
    }
    
    /// <summary>
    /// 타겟의 실제 위치를 가져옵니다. (Interpolation Target이 있으면 우선)
    /// </summary>
    private Vector3 GetTargetPosition()
    {
        if (_targetPlayer == null) return transform.position;
        
        // NetworkRigidbody2D의 Interpolation Target 확인
        var networkRb = _targetPlayer.GetComponent<Fusion.Addons.Physics.NetworkRigidbody2D>();
        if (networkRb != null && networkRb.InterpolationTarget != null)
        {
            // Interpolation Target이 있으면 그 위치 사용 (클라이언트에서 보간된 부드러운 위치)
            return networkRb.InterpolationTarget.position;
        }
        
        // Interpolation Target이 없으면 루트 Transform 위치 사용
        return _targetPlayer.transform.position;
    }

    private IEnumerator InitializeTarget()
    {
        while (MainGameManager.Instance == null)
        {
            yield return null;
        }

        yield return new WaitForSeconds(0.5f);
        
        UpdateTargetPlayer();
        _isInitialized = true;
    }
    
    /// <summary>
    /// 타겟 플레이어를 업데이트합니다.
    /// 테스트 모드: 선택된 플레이어
    /// 일반 모드: 로컬 플레이어
    /// </summary>
    private void UpdateTargetPlayer()
    {
        if (MainGameManager.Instance == null)
        {
            return;
        }
        
        PlayerController newTarget = null;
        
        if (MainGameManager.Instance.IsTestMode)
        {
            newTarget = MainGameManager.Instance.GetSelectedPlayer();
        }
        else
        {
            newTarget = MainGameManager.Instance.GetLocalPlayer();
        }
        
        if (newTarget != null && newTarget != _targetPlayer)
        {
            // PlayerController가 유효한지 확인
            if (newTarget.gameObject != null && newTarget.gameObject.activeInHierarchy)
            {
                _targetPlayer = newTarget;
                Vector3 targetPos = _targetPlayer.transform.position;
                targetPos.z = _zOffset;
                transform.position = targetPos;
                _lastTargetPosition = _targetPlayer.transform.position;
            }
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
    }

    /// <summary>
    /// 타겟 위치를 부드럽게 따라갑니다.
    /// 큰 위치 변화(텔레포트)가 감지되면 즉시 따라갑니다.
    /// </summary>
    private void FollowTarget(Vector3 targetPosition)
    {
        Vector2 currentPos2D = new Vector2(transform.position.x, transform.position.y);
        Vector2 targetPos2D = new Vector2(targetPosition.x, targetPosition.y);
        float distance = Vector2.Distance(currentPos2D, targetPos2D);
        
        if (distance < _deadZone)
        {
            return;
        }
        
        Vector3 desiredPosition = new Vector3(targetPosition.x, targetPosition.y, _zOffset);
        
        if (_useBoundary)
        {
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, _cameraBounds.x, _cameraBounds.z);
            desiredPosition.y = Mathf.Clamp(desiredPosition.y, _cameraBounds.y, _cameraBounds.w);
        }
        
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, _followSpeed);
        transform.position = smoothedPosition;
    }

    /// <summary>
    /// 테스트 모드에서 1/2번 키로 카메라 타겟 전환
    /// </summary>
    private void HandleTestModeInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            UpdateTargetPlayer();
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            UpdateTargetPlayer();
        }
    }

    /// <summary>
    /// 카메라 타겟을 수동으로 설정합니다.
    /// </summary>
    public void SetTarget(PlayerController player)
    {
        if (player != null)
        {
            _targetPlayer = player;
            Vector3 targetPos = _targetPlayer.transform.position;
            targetPos.z = _zOffset;
            transform.position = targetPos;
            _lastTargetPosition = _targetPlayer.transform.position;
        }
    }
}
