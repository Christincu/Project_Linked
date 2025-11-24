using System.Collections.Generic;
using UnityEngine;
using Fusion;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 보호막 마법 핸들러 (Air + Soil 조합)
/// 플레이어 선택, 보호막 적용, 베리어 시각화를 모두 처리합니다.
/// </summary>
public class BarrierMagicHandler : MonoBehaviour, ICombinedMagicHandler
{
    #region ICombinedMagicHandler Implementation
    public int MagicCode => 10; // 보호막 마법 코드
    #endregion
    
    #region Private Fields
    private PlayerMagicController _magicController;
    private PlayerController _controller;
    private GameDataManager _gameDataManager;
    
    // 선택 모드 관련 (하이라이트만 담당)
    private PlayerController _selectedPlayer = null;
    private Dictionary<PlayerController, GameObject> _highlightObjects = new Dictionary<PlayerController, GameObject>();
    private Color _highlightColor = new Color(0.2f, 1f, 0.2f, 1f); // 연두색
    private float _highlightThickness = 0.1f;
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
    public bool CanCast(Vector3 targetPosition)
    {
        // 보호막 마법은 선택 모드이므로 항상 시전 가능
        return true;
    }
    
    public bool CastMagic(Vector3 targetPosition)
    {
        // 보호막 마법은 CastMagic에서 처리하지 않고 ProcessInput에서 처리
        // 선택 모드로 유지
        return false; // 시전하지 않음 (선택 모드 유지)
    }
    
    public void ProcessInput(InputData inputData, Vector3 mouseWorldPos)
    {
        if (_controller == null || !_controller.Object.HasInputAuthority) return;
        
        // 먼저 플레이어 선택 업데이트 (클릭 감지 전에)
        UpdatePlayerSelection(mouseWorldPos);
        
        // 마우스 클릭 감지
        bool leftClickDown = inputData.GetMouseButton(InputMouseButton.LEFT) && 
                            !_magicController.GetPreviousLeftMouseButton();
        bool rightClickDown = inputData.GetMouseButton(InputMouseButton.RIGHT) && 
                             !_magicController.GetPreviousRightMouseButton();
        
        // 클릭 시 선택된 플레이어에게 보호막 적용
        if (leftClickDown || rightClickDown)
        {
            // 선택된 플레이어가 있으면 적용, 없으면 현재 마우스 위치에서 가장 가까운 플레이어 찾아서 적용
            PlayerController targetPlayer = _selectedPlayer;
            
            if (targetPlayer == null || targetPlayer.IsDead)
            {
                // 선택된 플레이어가 없으면 마우스 위치에서 가장 가까운 플레이어 찾기
                targetPlayer = FindClosestPlayer(mouseWorldPos);
            }
            
            if (targetPlayer != null && !targetPlayer.IsDead)
            {
                // RPC를 통해 보호막 적용 (클라이언트에서 호출 가능)
                _controller.RPC_ApplyBarrier(targetPlayer.Object.Id);
                
                // 마법 비활성화
                _controller.RPC_DeactivateMagic();
            }
        }
    }
    
    public void Update()
    {
        // 하이라이트 시각 효과 업데이트 (선택 모드용)
        // 베리어 시각화는 BarrierVisualizationManager에서 처리
        UpdateHighlightVisuals();
    }
    
    public void OnMagicActivated()
    {
        // 마법 활성화 시 초기 플레이어 선택 (마우스 위치 기반)
        if (_controller != null && _controller.Object.HasInputAuthority)
        {
            // 현재 마우스 위치 가져오기
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
                mouseWorldPos.z = 0;
                UpdatePlayerSelection(mouseWorldPos);
            }
        }
    }
    
    public void OnMagicDeactivated()
    {
        // 모든 하이라이트 제거
        ClearAllHighlights();
        _selectedPlayer = null;
    }
    #endregion
    
    #region Player Selection
    /// <summary>
    /// 마우스 위치에 따라 플레이어 선택을 업데이트합니다.
    /// </summary>
    private void UpdatePlayerSelection(Vector3 mouseWorldPos)
    {
        // 가장 가까운 플레이어 찾기
        PlayerController closestPlayer = FindClosestPlayer(mouseWorldPos);
        
        if (closestPlayer != null && !closestPlayer.IsDead)
        {
            _selectedPlayer = closestPlayer;
        }
        else
        {
            _selectedPlayer = null;
        }
    }
    
    /// <summary>
    /// 마우스 위치에 가장 가까운 플레이어를 찾습니다.
    /// </summary>
    private PlayerController FindClosestPlayer(Vector3 mouseWorldPos)
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
    #endregion
    
    #region Highlight Visuals
    /// <summary>
    /// 하이라이트 시각 효과를 업데이트합니다.
    /// </summary>
    private void UpdateHighlightVisuals()
    {
        // 이전 선택된 플레이어 하이라이트 제거
        var keysToRemove = new List<PlayerController>();
        foreach (var kvp in _highlightObjects)
        {
            if (kvp.Key != _selectedPlayer)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            RemoveHighlight(key);
        }

        // 현재 선택된 플레이어 하이라이트 추가
        if (_selectedPlayer != null && !_highlightObjects.ContainsKey(_selectedPlayer))
        {
            AddHighlight(_selectedPlayer);
        }
    }
    
    /// <summary>
    /// 플레이어에게 하이라이트를 추가합니다.
    /// </summary>
    private void AddHighlight(PlayerController player)
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
            highlightRenderer.color = _highlightColor;
            highlightRenderer.sortingOrder = playerRenderer.sortingOrder + 1;
            highlightObj.transform.localScale = Vector3.one * (1f + _highlightThickness);
        }

        _highlightObjects[player] = highlightObj;
    }
    
    /// <summary>
    /// 플레이어의 하이라이트를 제거합니다.
    /// </summary>
    private void RemoveHighlight(PlayerController player)
    {
        if (player == null || !_highlightObjects.ContainsKey(player)) return;

        GameObject highlightObj = _highlightObjects[player];
        if (highlightObj != null)
        {
            Destroy(highlightObj);
        }

        _highlightObjects.Remove(player);
    }
    
    /// <summary>
    /// 모든 하이라이트를 제거합니다.
    /// </summary>
    private void ClearAllHighlights()
    {
        foreach (var kvp in _highlightObjects)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value);
            }
        }
        _highlightObjects.Clear();
    }
    #endregion
    
    #region Barrier Application
    /// <summary>
    /// 플레이어에게 보호막을 적용합니다.
    /// 직접 보호막을 적용합니다 (BarrierMagic 오브젝트 스폰 없이).
    /// </summary>
    public void ApplyBarrierToPlayer(PlayerController targetPlayer)
    {
        if (targetPlayer == null || _controller == null) return;
        if (!_controller.Object.HasStateAuthority) return;
        if (_gameDataManager == null) return;

        // 베리어 조합 데이터 가져오기
        BarrierMagicCombinationData barrierData = GetBarrierData();
        if (barrierData == null)
        {
            Debug.LogError("[BarrierMagicHandler] BarrierMagicCombinationData not found!");
            return;
        }

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

        // 보호막을 받은 플레이어와 받지 못한 플레이어의 체력 설정
        // 각 플레이어의 State Authority에서만 체력을 변경할 수 있으므로, 각 플레이어의 State Authority를 확인
        foreach (var player in allPlayers)
        {
            if (player == null || player.IsDead) continue;
            
            // 각 플레이어의 State Authority에서만 체력 변경 가능
            if (!player.Object.HasStateAuthority) continue;

            if (player == targetPlayer)
            {
                // 보호막 받은 플레이어: 체력 및 보호막 타이머 설정
                // MaxHealth를 먼저 업데이트 (SetHealth가 MaxHealth로 클램프하므로)
                if (barrierData.barrierReceiverHealth > player.MaxHealth)
                {
                    player.MaxHealth = barrierData.barrierReceiverHealth;
                }
                
                if (player.State != null)
                {
                    player.State.SetHealth(barrierData.barrierReceiverHealth);
                }
                else
                {
                    player.CurrentHealth = barrierData.barrierReceiverHealth;
                }
                
                // 자폭 베리어 타이머 시작
                player.BarrierTimer = TickTimer.CreateFromSeconds(_controller.Runner, barrierData.barrierDuration);
                player.HasBarrier = true;
                // 위협점수와 이동속도 효과는 PlayerController의 FixedUpdateNetwork에서 자동 업데이트됨
                Debug.Log($"[BarrierMagicHandler] {targetPlayer.name} received self-destruct barrier (HP: {barrierData.barrierReceiverHealth}, Duration: {barrierData.barrierDuration}s, ThreatScore: {barrierData.threatScore})");
            }
            else
            {
                // 보호막 받지 못한 플레이어: 체력 설정
                // nonReceiverHealth는 항상 MaxHealth보다 작으므로 MaxHealth 변경 불필요
                if (player.State != null)
                {
                    player.State.SetHealth(barrierData.nonReceiverHealth);
                }
                else
                {
                    player.CurrentHealth = barrierData.nonReceiverHealth;
                }
                
                // 기존 보호막 제거
                player.BarrierTimer = TickTimer.None;
                player.HasBarrier = false;
                // 위협점수는 PlayerController의 FixedUpdateNetwork에서 자동 업데이트됨
                Debug.Log($"[BarrierMagicHandler] {player.name} did not receive barrier (HP: {barrierData.nonReceiverHealth}, ThreatScore: {player.ThreatScore})");
            }
        }
        
        ClearAllHighlights();
    }
    #endregion
    
    #region Barrier Explosion
    /// <summary>
    /// 자폭 베리어 폭발을 처리합니다.
    /// 베리어 타이머에 따라 다른 범위와 데미지를 적용합니다.
    /// </summary>
    public void HandleBarrierExplosion(PlayerController playerWithBarrier)
    {
        if (playerWithBarrier == null || playerWithBarrier.Object == null || !playerWithBarrier.Object.HasStateAuthority) return;
        if (playerWithBarrier.Runner == null) return;
        
        float remainingTime = playerWithBarrier.BarrierTimer.RemainingTime(playerWithBarrier.Runner) ?? 0f;
        Vector3 explosionPosition = playerWithBarrier.transform.position;
        
        // 베리어 조합 데이터 가져오기
        BarrierMagicCombinationData barrierData = GetBarrierData();
        if (barrierData == null) return;
        
        // 데이터에서 폭발 정보 가져오기
        barrierData.GetExplosionData(remainingTime, out float explosionRadius, out float explosionDamage);
        
        Debug.Log($"[BarrierExplosion] {playerWithBarrier.name} exploded! Radius: {explosionRadius}m, Damage: {explosionDamage}, RemainingTime: {remainingTime:F2}s");
        
        // Physics2D.OverlapCircle을 사용하여 범위 내 적만 효율적으로 가져오기
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(explosionPosition, explosionRadius);
        
        // 범위 내 적에게 데미지 적용
        foreach (var collider in hitColliders)
        {
            if (collider == null) continue;
            
            // EnemyController 찾기 (콜리더가 직접 가지고 있거나 부모/루트에 있을 수 있음)
            EnemyController enemy = collider.GetComponent<EnemyController>();
            if (enemy == null)
            {
                enemy = collider.GetComponentInParent<EnemyController>();
            }
            if (enemy == null && collider.attachedRigidbody != null)
            {
                enemy = collider.attachedRigidbody.GetComponent<EnemyController>();
            }
            if (enemy == null && collider.transform.root != null)
            {
                enemy = collider.transform.root.GetComponent<EnemyController>();
            }
            
            // 적이 없거나 이미 사망했으면 스킵
            if (enemy == null || enemy.IsDead) continue;
            
            // 정확한 거리 계산 (콜리더 중심이 아닌 transform 위치 기준)
            float distance = Vector3.Distance(explosionPosition, enemy.transform.position);
            
            // 거리 재확인 (OverlapCircle은 콜리더 범위를 기준으로 하므로 정확한 거리 체크 필요)
            if (distance <= explosionRadius)
            {
                // 데미지 적용
                if (enemy.State != null)
                {
                    enemy.State.TakeDamage(explosionDamage);
                    Debug.Log($"[BarrierExplosion] {enemy.name} took {explosionDamage} damage (Distance: {distance:F2}m)");
                }
            }
        }
    }
    
    /// <summary>
    /// 베리어 조합 데이터를 가져옵니다.
    /// </summary>
    private BarrierMagicCombinationData GetBarrierData()
    {
        if (_gameDataManager == null || _gameDataManager.MagicService == null) return null;
        
        // 베리어 마법 코드는 10 (Air + Soil 조합)
        // MagicService에서 조합 데이터 찾기
        MagicCombinationData combinationData = _gameDataManager.MagicService.GetCombinationDataByResult(10);
        
        // BarrierMagicCombinationData로 캐스팅
        if (combinationData is BarrierMagicCombinationData barrierData)
        {
            return barrierData;
        }
        
        return null;
    }
    #endregion
    
    #region Helper Methods (for Highlight)
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
    #endregion
    
    #region Cleanup
    private void OnDestroy()
    {
        // 하이라이트만 정리 (베리어 시각화는 BarrierVisualizationManager에서 처리)
        ClearAllHighlights();
    }
    #endregion
}

