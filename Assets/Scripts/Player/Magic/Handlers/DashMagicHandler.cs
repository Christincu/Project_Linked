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
    
    public void Update() { }
    public void OnMagicActivated() { }
    public void OnMagicDeactivated() { }
    
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
}

