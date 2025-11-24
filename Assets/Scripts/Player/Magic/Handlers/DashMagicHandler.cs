using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 화염 돌진 마법 핸들러
/// 두 플레이어의 마법 합체 발동 시 적용되는 특수 돌진 스킬
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
    /// <summary>
    /// 시전 가능 여부를 확인합니다.
    /// </summary>
    public bool CanCast(Vector3 targetPosition) => true;
    
    /// <summary>
    /// 돌진 스킬을 시전합니다.
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
    /// 입력을 처리합니다.
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
    
    public void Update() { }
    public void OnMagicActivated() { }
    public void OnMagicDeactivated() { }
    #endregion
    
    #region Skill Application
    /// <summary>
    /// 합체 마법에 참여한 다른 플레이어를 찾습니다.
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
        
        List<PlayerController> allPlayers = GetAllPlayers();
        
        foreach (var player in allPlayers)
        {
            if (player == null || player == _controller || player.IsDead) continue;
            return player;
        }
        
        return null;
    }
    
    /// <summary>
    /// 플레이어에게 돌진 스킬을 적용합니다.
    /// </summary>
    private void ApplyDashSkillToPlayer(PlayerController player)
    {
        if (player == null || !player.Object.HasStateAuthority) return;
        
        DashMagicCombinationData dashData = GetDashData();
        if (dashData == null || dashData.dashBarrierPrefab == null) return;
        
        GameObject dashBarrier = Instantiate(dashData.dashBarrierPrefab, player.transform.position, Quaternion.identity);
        dashBarrier.transform.SetParent(player.transform);
        dashBarrier.transform.localPosition = Vector3.zero;
        
        var dashMagicObj = dashBarrier.GetComponent<DashMagicObject>();
        if (dashMagicObj == null)
        {
            dashMagicObj = dashBarrier.AddComponent<DashMagicObject>();
        }
        dashMagicObj.Initialize(player, dashData);
        
        player.DashMagicObject = dashMagicObj;
    }
    
    /// <summary>
    /// 모든 플레이어를 가져옵니다.
    /// </summary>
    private List<PlayerController> GetAllPlayers()
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
        
        return allPlayers;
    }
    
    /// <summary>
    /// 돌진 마법 조합 데이터를 가져옵니다.
    /// </summary>
    private DashMagicCombinationData GetDashData()
    {
        if (_gameDataManager == null || _gameDataManager.MagicService == null) return null;
        
        MagicCombinationData combinationData = _gameDataManager.MagicService.GetCombinationDataByResult(MagicCode);
        return combinationData as DashMagicCombinationData;
    }
    #endregion
}

