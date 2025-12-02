using System.Collections;
using UnityEngine;
using Cinemachine;


public class CinemachineCameraController : MonoBehaviour
{
    [Header("VCam Settings")]
    [Tooltip("씬 안에서 사용할 Virtual Camera 이름 (없으면 자동 찾기)")]
    public string vcamName = "VC_PlayerFollow";

    private CinemachineVirtualCamera _vcam;
    private Transform _currentTarget;    
    private bool _initialized = false;
    private Camera _mainCamera;

    void Start()
    {
        _mainCamera = Camera.main;
        StartCoroutine(InitializeCoroutine());
    }

    IEnumerator InitializeCoroutine()
    {
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
            _vcam = FindObjectOfType<CinemachineVirtualCamera>();
        }

        if (_vcam == null)
        {
            Debug.LogError("[CinemachineCameraController] Virtual Camera를 찾을 수 없습니다.");
        }
    }

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
