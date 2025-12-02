using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 화염 돌진 마법 핸들러
/// 마법 시전 시 두 플레이어에게 돌진 스킬을 적용합니다.
/// </summary>
public class DashMagicHandler : MonoBehaviour, ICombinedMagicHandler
{
    #region ICombinedMagicHandler Implementation
    public int MagicCode => 11;
    #endregion
    
    #region Private Fields
    private PlayerMagicController _magicController;
    private PlayerController _controller;
    private GameDataManager _gameDataManager;
    
    // 카메라 제어 관련
    private MainCameraController _cameraController;
    private Camera _mainCamera;
    private bool _cameraLocked = false;
    private float _originalCameraSize = 0f;
    #endregion
    
    #region Initialization
    public void Initialize(PlayerMagicController magicController, GameDataManager gameDataManager)
    {
        _magicController = magicController;
        _controller = magicController.Controller;
        _gameDataManager = gameDataManager;
    }
    #endregion
    
    #region ICombinedMagicHandler Methods
    public bool CanCast(Vector3 targetPosition) => true;
    
    /// <summary>
    /// 돌진 스킬을 시전합니다. 두 플레이어 모두에게 적용합니다.
    /// </summary>
    public bool CastMagic(Vector3 targetPosition)
    {
        if (_controller == null || !_controller.Object.HasStateAuthority || _gameDataManager == null) 
            return false;
        
        PlayerController otherPlayer = FindOtherPlayerInCombination();
        if (otherPlayer == null) return false;
        
        ApplyDashSkillToPlayer(_controller);
        ApplyDashSkillToPlayer(otherPlayer);
        return true;
    }
    
    /// <summary>
    /// 입력 처리: 클릭 시 마법 시전 RPC 호출
    /// </summary>
    public void ProcessInput(InputData inputData, Vector3 mouseWorldPos)
    {
        if (_controller == null || !_controller.Object.HasInputAuthority) return;
        
        bool leftClickDown = inputData.GetMouseButton(InputMouseButton.LEFT) && 
                            !_magicController.GetPreviousLeftMouseButton();
        bool rightClickDown = inputData.GetMouseButton(InputMouseButton.RIGHT) && 
                             !_magicController.GetPreviousRightMouseButton();
        
        if (leftClickDown || rightClickDown)
        {
            _controller.RPC_CastMagic(mouseWorldPos);
        }
    }
    
    public void Update()
    {
        // 카메라 제어 (Input Authority만)
        if (_controller == null || !_controller.Object.HasInputAuthority) return;
        
        // Dash 스킬이 종료되었는데 카메라가 잠겨있으면 복원
        if (!_controller.HasDashSkill && _cameraLocked)
        {
            RestoreCamera();
            return;
        }
        
        // Dash 스킬이 활성화되어 있고, 카메라가 아직 잠기지 않았으면 설정
        if (_controller.HasDashSkill && !_cameraLocked)
        {
            SetupCamera();
        }
        
        // 카메라 위치 업데이트 (카메라가 잠겨있고 Dash 스킬이 활성화되어 있을 때)
        if (_cameraLocked && _controller.HasDashSkill)
        {
            UpdateCameraPosition();
        }
    }
    
    public void OnMagicActivated() { }
    
    public void OnMagicDeactivated()
    {
        // Dash 스킬이 완전히 종료되었을 때만 카메라 복원 (Input Authority만)
        if (_controller != null && _controller.Object.HasInputAuthority && !_controller.HasDashSkill)
        {
            RestoreCamera();
        }
    }
    
    /// <summary>
    /// 마법이 현재 시전 중인지 확인합니다.
    /// 돌진 스킬이 활성화되어 있으면 시전 중으로 간주합니다.
    /// </summary>
    public bool IsCasting()
    {
        return _controller != null && _controller.HasDashSkill;
    }
    #endregion
    
    #region Skill Application
    /// <summary>
    /// 합체 마법에 참여한 다른 플레이어를 찾습니다.
    /// [최적화] 합체 상태인 플레이어만 찾도록 개선
    /// </summary>
    private PlayerController FindOtherPlayerInCombination()
    {
        if (_controller == null) return null;
        
        if (_controller.ActivatedMagicCode == -1 || _controller.AbsorbedMagicCode == -1)
            return null;
        
        int myCombinedCode = _gameDataManager.MagicService.GetCombinedMagic(
            _controller.ActivatedMagicCode,
            _controller.AbsorbedMagicCode
        );
        
        if (myCombinedCode != MagicCode) return null;
        
        // [최적화] 합체 상태인 플레이어만 찾기
        // 두 플레이어가 서로의 마법을 흡수한 상태이므로, 
        // AbsorbedMagicCode가 서로의 ActivatedMagicCode와 일치하는 플레이어를 찾음
        List<PlayerController> allPlayers = GetAllPlayers();
        foreach (var player in allPlayers)
        {
            if (player == null || player == _controller || player.IsDead) continue;
            
            // 합체 상태 확인: 상대방의 ActivatedMagicCode가 내 AbsorbedMagicCode와 일치하거나,
            // 상대방의 AbsorbedMagicCode가 내 ActivatedMagicCode와 일치하는 경우
            bool isInCombination = (player.ActivatedMagicCode == _controller.AbsorbedMagicCode) ||
                                  (player.AbsorbedMagicCode == _controller.ActivatedMagicCode);
            
            if (isInCombination)
            {
                return player;
            }
        }
        
        // 폴백: 합체 상태 확인이 실패하면 첫 번째 다른 플레이어 반환
        foreach (var player in allPlayers)
        {
            if (player != null && player != _controller && !player.IsDead)
                return player;
        }
        
        return null;
    }
    
    /// <summary>
    /// 플레이어에게 돌진 스킬을 적용합니다. (Runner.Spawn 사용)
    /// </summary>
    private void ApplyDashSkillToPlayer(PlayerController player)
    {
        // 1. 권한 및 데이터 체크
        if (player == null || !player.Object.HasStateAuthority) return;
        
        DashMagicCombinationData dashData = GetDashData();
        if (dashData == null || dashData.dashBarrierPrefab == null) 
        {
            Debug.LogError("[DashHandler] DashData or Prefab is missing!");
            return;
        }

        // 2. Runner 체크
        var runner = player.Runner;
        if (runner == null) return;

        // 3. Prefab에 NetworkObject가 있는지 확인 (필수!)
        var prefabNO = dashData.dashBarrierPrefab.GetComponent<NetworkObject>();
        if (prefabNO == null)
        {
            Debug.LogError($"[DashHandler] Prefab {dashData.dashBarrierPrefab.name} is missing a NetworkObject component!");
            return;
        }

        Debug.Log($"[DashHandler] Spawning Dash Object for {player.name}");

        // 4. Runner.Spawn으로 네트워크 오브젝트 생성
        NetworkObject spawnedObj = runner.Spawn(
            prefabNO, 
            player.transform.position, 
            Quaternion.identity, 
            player.Object.InputAuthority,
            (runnerInner, obj) =>
            {
                // 필요시 스폰 직전 초기화 가능
            }
        );

        // 5. 계층 구조 설정 및 초기화
        if (spawnedObj != null)
        {
            spawnedObj.transform.SetParent(player.transform);
            spawnedObj.transform.localPosition = Vector3.zero;

            var dashMagicObj = spawnedObj.GetComponent<DashMagicObject>();
            if (dashMagicObj != null)
            {
                dashMagicObj.Initialize(player, dashData);
                player.DashMagicObject = dashMagicObj;
            }
        }
    }
    
    /// <summary>
    /// 모든 플레이어를 가져옵니다.
    /// </summary>
    private List<PlayerController> GetAllPlayers()
    {
        if (MainGameManager.Instance != null)
        {
            var players = MainGameManager.Instance.GetAllPlayers();
            if (players != null && players.Count > 0) return players;
        }
        
        return new List<PlayerController>(FindObjectsOfType<PlayerController>());
    }
    
    /// <summary>
    /// 돌진 마법 조합 데이터를 가져옵니다.
    /// </summary>
    private DashMagicCombinationData GetDashData()
    {
        if (_gameDataManager?.MagicService == null) return null;
        
        MagicCombinationData combinationData = _gameDataManager.MagicService.GetCombinationDataByResult(MagicCode);
        return combinationData as DashMagicCombinationData;
    }
    #endregion
    
    #region Camera Control
    /// <summary>
    /// 카메라를 두 플레이어의 중앙으로 설정하고 줌 아웃합니다.
    /// </summary>
    private void SetupCamera()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }
        if (_mainCamera == null) return;
        
        if (_cameraController == null)
        {
            _cameraController = _mainCamera.GetComponent<MainCameraController>();
        }
        if (_cameraController == null) return;
        
        // 다른 플레이어 찾기
        PlayerController otherPlayer = FindOtherDashPlayer();
        if (otherPlayer == null) return;
        
        // 카메라 크기 저장 (아직 저장되지 않았을 때만)
        if (_originalCameraSize <= 0f)
        {
            _originalCameraSize = _mainCamera.orthographicSize;
        }
        
        // 카메라를 최대 사이즈로 설정 (MainCameraController에서 직접 처리)
        if (_cameraController != null)
        {
            _cameraController.SetMaxSize();
        }
        
        // 카메라 고정 (MainCameraController 비활성화)
        _cameraController.enabled = false;
        _cameraLocked = true;
        
        // 초기 카메라 위치 설정
        UpdateCameraPosition();
    }
    
    /// <summary>
    /// 카메라 위치를 두 플레이어의 중앙으로 업데이트합니다.
    /// </summary>
    private void UpdateCameraPosition()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }
        if (_mainCamera == null) return;
        
        // 다른 플레이어 찾기
        PlayerController otherPlayer = FindOtherDashPlayer();
        if (otherPlayer == null) return;
        
        // 두 플레이어의 중앙 계산
        Vector3 center = (_controller.transform.position + otherPlayer.transform.position) * 0.5f;
        
        // 카메라를 중앙으로 이동
        _mainCamera.transform.position = new Vector3(center.x, center.y, _mainCamera.transform.position.z);
    }
    
    /// <summary>
    /// 카메라를 원래 상태로 복원합니다.
    /// </summary>
    private void RestoreCamera()
    {
        if (!_cameraLocked) return;
        
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }
        if (_mainCamera == null) return;
        
        // 카메라 크기 복원 (MainCameraController의 ResetCameraSize 사용)
        if (_cameraController != null)
        {
            _cameraController.ResetCameraSize();
        }
        else if (_originalCameraSize > 0f)
        {
            // 폴백: 저장된 크기로 복원
            _mainCamera.orthographicSize = _originalCameraSize;
        }
        
        // 카메라 제어 복원
        if (_cameraController != null)
        {
            _cameraController.enabled = true;
        }
        
        _cameraLocked = false;
        _originalCameraSize = 0f;
    }
    
    /// <summary>
    /// 다른 돌진 스킬 사용 플레이어를 찾습니다.
    /// </summary>
    private PlayerController FindOtherDashPlayer()
    {
        List<PlayerController> allPlayers = GetAllPlayers();
        
        foreach (var player in allPlayers)
        {
            if (player != null && player != _controller && player.HasDashSkill && !player.IsDead)
            {
                return player;
            }
        }
        
        return null;
    }
    #endregion
}

