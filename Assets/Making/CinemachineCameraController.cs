using System.Collections;
using UnityEngine;
using Cinemachine;

/// <summary>
/// 메인 테스트 구조(MainCameraController)의 원리를 유지하면서
/// Cinemachine Virtual Camera를 통해 플레이어를 따라다니도록 만든다.
/// 
/// - 씬에는 CinemachineVirtualCamera 1개만 두면 된다.
/// - 테스트 모드에서는 1/2 키로 조작 플레이어를 바꾸면 카메라도 즉시 변경.
/// - 일반 모드에서는 MainGameManager.GetLocalPlayer()를 따라감.
/// 
/// </summary>
public class CinemachineCameraController : MonoBehaviour
{
    [Header("VCam Settings")]
    [Tooltip("씬 안에서 사용할 Virtual Camera 이름 (없으면 자동 찾기)")]
    public string vcamName = "VC_PlayerFollow";

    private CinemachineVirtualCamera _vcam;
    private Transform _currentTarget;     // 따라갈 플레이어 Transform
    private bool _initialized = false;
    private Camera _mainCamera;

    void Start()
    {
        _mainCamera = Camera.main;
        StartCoroutine(InitializeCoroutine());
    }

    IEnumerator InitializeCoroutine()
    {
        // MainGameManager가 존재할 때까지 대기
        while (MainGameManager.Instance == null)
            yield return null;

        // VCam 찾기
        FindOrAssignVCam();

        // 플레이어가 스폰되기를 조금 기다림
        yield return new WaitForSeconds(0.3f);

        UpdateCameraTarget();
        _initialized = true;
    }

    void LateUpdate()
    {
        if (!_initialized) return;

        // 테스트 모드 키 입력 체크
        if (MainGameManager.Instance.IsTestMode)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) ||
                Input.GetKeyDown(KeyCode.Alpha2))
            {
                UpdateCameraTarget();
            }
        }

        // 타겟이 사라졌으면 다시 찾기
        if (_currentTarget == null)
        {
            UpdateCameraTarget();
        }
    }

    // ──────────────────────────────────────────────
    // VCam 찾기
    void FindOrAssignVCam()
    {
        if (!string.IsNullOrEmpty(vcamName))
        {
            var obj = GameObject.Find(vcamName);
            if (obj != null)
            {
                _vcam = obj.GetComponent<CinemachineVirtualCamera>();
            }
        }

        if (_vcam == null)
        {
            // 이름으로 못 찾으면 아무 VCam이나 할당
            _vcam = FindObjectOfType<CinemachineVirtualCamera>();
        }

        if (_vcam == null)
        {
            Debug.LogError("[CinemachineCameraController] Virtual Camera를 찾을 수 없습니다.");
        }
    }

    // ──────────────────────────────────────────────
    // 카메라 타겟 갱신 (MainCameraController 방식 그대로)
    void UpdateCameraTarget()
    {
        if (MainGameManager.Instance == null)
            return;

        PlayerController target = null;

        if (MainGameManager.Instance.IsTestMode)
        {
            target = MainGameManager.Instance.GetSelectedPlayer();
        }
        else
        {
            target = MainGameManager.Instance.GetLocalPlayer();
        }

        if (target == null || !target.gameObject.activeInHierarchy)
            return;

        _currentTarget = target.transform;

        // Cinemachine의 Follow/LookAt 설정
        if (_vcam != null)
        {
            _vcam.Follow = _currentTarget;
            _vcam.LookAt = _currentTarget;
        }
    }
}
