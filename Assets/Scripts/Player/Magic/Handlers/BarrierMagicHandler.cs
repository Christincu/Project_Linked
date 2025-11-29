using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 보호막 마법 핸들러 (Air + Soil 조합)
/// 마법 활성화 시 플레이어 선택 모드를 제공하고, 클릭 시 보호막을 적용합니다.
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
    
    // 선택 모드 관련
    private PlayerController _selectedPlayer = null;
    private PlayerController _currentHighlightedPlayer = null; // 현재 하이라이트된 플레이어
    
    // [최적화] 하이라이트 객체 재사용을 위한 단일 인스턴스
    private GameObject _sharedHighlightInstance = null;
    private SpriteRenderer _highlightRenderer = null;
    private Color _highlightColor = new Color(0.2f, 1f, 0.2f, 1f); // 연두색
    private float _highlightThickness = 0.1f;
    #endregion
    
    #region Initialization
    public void Initialize(PlayerMagicController magicController, GameDataManager gameDataManager)
    {
        _magicController = magicController;
        _controller = magicController.Controller;
        _gameDataManager = gameDataManager;
        
        // [최적화] 초기화 시 하이라이트 객체 미리 생성 (비활성화 상태)
        CreateSharedHighlightObject();
    }
    
    /// <summary>
    /// [최적화] 공유 하이라이트 객체를 미리 생성합니다.
    /// </summary>
    private void CreateSharedHighlightObject()
    {
        if (_sharedHighlightInstance != null) return;
        
        _sharedHighlightInstance = new GameObject("SharedBarrierHighlight");
        _highlightRenderer = _sharedHighlightInstance.AddComponent<SpriteRenderer>();
        _highlightRenderer.color = _highlightColor;
        _sharedHighlightInstance.SetActive(false);
    }
    #endregion
    
    #region ICombinedMagicHandler Methods
    public bool CanCast(Vector3 targetPosition) => true;
    
    /// <summary>
    /// [핵심 수정] 클릭 시 호출되는 메서드입니다.
    /// 여기서 실제 보호막 적용 로직을 수행합니다.
    /// </summary>
    public bool CastMagic(Vector3 targetPosition)
    {
        if (_controller == null || !_controller.Object.HasInputAuthority) return false;
        
        // 1. 현재 마우스 위치(targetPosition)에 있는 플레이어를 찾습니다.
        // ProcessInput이 직전 프레임에 _selectedPlayer를 갱신했겠지만, 
        // 확실하게 하기 위해 여기서 다시 찾거나 _selectedPlayer를 사용합니다.
        PlayerController target = _selectedPlayer;
        
        // 만약 선택된 플레이어가 없다면 현재 위치 기준으로 다시 찾기 (빠른 클릭 대응)
        if (target == null || target.IsDead)
        {
            target = FindClosestPlayer(targetPosition);
        }
        
        // 2. 유효한 타겟이면 보호막 적용
        if (target != null && !target.IsDead)
        {
            // RPC 호출
            _controller.RPC_ApplyBarrier(target.Object.Id);
            
            // 마법 비활성화 (성공했으므로)
            _controller.RPC_DeactivateMagic();
            
            return true; // 시전 성공
        }
        
        return false; // 시전 실패 (허공 클릭 등)
    }
    
    /// <summary>
    /// [핵심 수정] 클릭하지 않았을 때(호버링 중) 호출됩니다.
    /// 여기서는 타겟팅 업데이트와 하이라이트만 처리합니다.
    /// </summary>
    public void ProcessInput(InputData inputData, Vector3 mouseWorldPos)
    {
        if (_controller == null || !_controller.Object.HasInputAuthority) return;
        
        // 타겟팅 업데이트 (하이라이트 갱신용)
        UpdatePlayerSelection(mouseWorldPos);
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
        // 하이라이트 비활성화
        if (_sharedHighlightInstance != null)
        {
            _sharedHighlightInstance.SetActive(false);
            _sharedHighlightInstance.transform.SetParent(null); // 부모 연결 해제
        }
        _selectedPlayer = null;
        _currentHighlightedPlayer = null;
    }
    
    /// <summary>
    /// 마법이 현재 시전 중인지 확인합니다.
    /// 보호막이 적용되어 있으면 시전 중으로 간주합니다.
    /// </summary>
    public bool IsCasting()
    {
        return _controller != null && _controller.HasBarrier;
    }
    #endregion
    
    #region Player Selection
    /// <summary>
    /// 마우스 위치에 따라 플레이어 선택을 업데이트합니다.
    /// </summary>
    private void UpdatePlayerSelection(Vector3 mouseWorldPos)
    {
        _selectedPlayer = FindClosestPlayer(mouseWorldPos);
        if (_selectedPlayer != null && _selectedPlayer.IsDead)
        {
            _selectedPlayer = null;
        }
    }
    
    /// <summary>
    /// 마우스 위치에 가장 가까운 플레이어를 찾습니다.
    /// </summary>
    private PlayerController FindClosestPlayer(Vector3 mouseWorldPos)
    {
        List<PlayerController> allPlayers = GetAllPlayers();
        if (allPlayers == null || allPlayers.Count == 0) return null;

        PlayerController closestPlayer = null;
        float closestDistance = float.MaxValue;

        foreach (var player in allPlayers)
        {
            if (player == null || player.IsDead) continue;

            Vector3 playerPos = player.ViewObj != null ? player.ViewObj.transform.position : player.transform.position;
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
    #endregion
    
    #region Highlight Visuals
    /// <summary>
    /// 하이라이트 시각 효과를 업데이트합니다.
    /// [최적화] 단일 하이라이트 객체를 재사용하여 GC 스파이크 방지
    /// </summary>
    private void UpdateHighlightVisuals()
    {
        // 타겟이 변경되었을 때만 시각 효과 갱신
        if (_selectedPlayer != _currentHighlightedPlayer)
        {
            _currentHighlightedPlayer = _selectedPlayer;
            UpdateHighlightVisuals(_selectedPlayer);
        }
    }
    
    /// <summary>
    /// [최적화] 하이라이트 시각 효과를 업데이트합니다.
    /// Destroy/Instantiate 대신 SetActive와 SetParent만 사용
    /// </summary>
    private void UpdateHighlightVisuals(PlayerController target)
    {
        if (_sharedHighlightInstance == null || _highlightRenderer == null)
        {
            CreateSharedHighlightObject();
            if (_sharedHighlightInstance == null) return;
        }
        
        if (target == null || target.IsDead)
        {
            _sharedHighlightInstance.SetActive(false);
            return;
        }
        
        // ViewObj 찾기
        GameObject viewObj = target.ViewObj;
        if (viewObj == null)
        {
            Transform viewObjParent = target.transform.Find("ViewObjParent");
            if (viewObjParent != null && viewObjParent.childCount > 0)
            {
                viewObj = viewObjParent.GetChild(0).gameObject;
            }
        }
        
        if (viewObj == null)
        {
            _sharedHighlightInstance.SetActive(false);
            return;
        }
        
        SpriteRenderer targetRenderer = viewObj.GetComponent<SpriteRenderer>();
        if (targetRenderer == null || targetRenderer.sprite == null)
        {
            _sharedHighlightInstance.SetActive(false);
            return;
        }
        
        // [최적화] 부모 변경 및 활성화만 수행
        _sharedHighlightInstance.transform.SetParent(viewObj.transform, false);
        _sharedHighlightInstance.transform.localPosition = Vector3.zero;
        _sharedHighlightInstance.transform.localScale = Vector3.one * (1f + _highlightThickness);
        
        _highlightRenderer.sprite = targetRenderer.sprite;
        _highlightRenderer.sortingOrder = targetRenderer.sortingOrder + 1;
        
        _sharedHighlightInstance.SetActive(true);
    }
    #endregion
    
    #region Barrier Application
    /// <summary>
    /// 플레이어에게 보호막을 적용합니다.
    /// </summary>
    public void ApplyBarrierToPlayer(PlayerController targetPlayer)
    {
        if (targetPlayer == null || _controller == null || !_controller.Object.HasStateAuthority) return;
        if (_gameDataManager == null) return;

        BarrierMagicCombinationData barrierData = GetBarrierData();
        if (barrierData == null)
        {
            Debug.LogError("[BarrierMagicHandler] BarrierMagicCombinationData not found!");
            return;
        }

        List<PlayerController> allPlayers = GetAllPlayers();

        // 각 플레이어의 State Authority에서만 체력 변경 가능
        foreach (var player in allPlayers)
        {
            if (player == null || player.IsDead || !player.Object.HasStateAuthority) continue;

            if (player == targetPlayer)
            {
                // 보호막 받은 플레이어
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
                
                player.BarrierTimer = TickTimer.CreateFromSeconds(_controller.Runner, barrierData.barrierDuration);
                player.HasBarrier = true;
                Debug.Log($"[BarrierMagicHandler] {targetPlayer.name} received barrier (HP: {barrierData.barrierReceiverHealth}, Duration: {barrierData.barrierDuration}s)");
            }
            else
            {
                // 보호막 받지 못한 플레이어
                if (player.State != null)
                {
                    player.State.SetHealth(barrierData.nonReceiverHealth);
                }
                else
                {
                    player.CurrentHealth = barrierData.nonReceiverHealth;
                }
                
                player.BarrierTimer = TickTimer.None;
                player.HasBarrier = false;
            }
        }
        
        // 하이라이트 비활성화
        if (_sharedHighlightInstance != null)
        {
            _sharedHighlightInstance.SetActive(false);
            _sharedHighlightInstance.transform.SetParent(null);
        }
        _currentHighlightedPlayer = null;
    }
    #endregion
    
    #region Barrier Explosion
    /// <summary>
    /// 자폭 베리어 폭발을 처리합니다.
    /// </summary>
    public void HandleBarrierExplosion(PlayerController playerWithBarrier)
    {
        // 권한 체크 및 유효성 검사
        if (playerWithBarrier == null || !playerWithBarrier.Object.HasStateAuthority || playerWithBarrier.Runner == null) return;
        
        float remainingTime = playerWithBarrier.BarrierTimer.RemainingTime(playerWithBarrier.Runner) ?? 0f;
        Vector3 explosionPosition = playerWithBarrier.transform.position;
        
        BarrierMagicCombinationData barrierData = GetBarrierData();
        if (barrierData == null) return;
        
        // 폭발 데이터 계산
        barrierData.GetExplosionData(remainingTime, out float explosionRadius, out float explosionDamage);
        
        Debug.Log($"[BarrierExplosion] Server Calculated Boom! {playerWithBarrier.name} exploded! Radius: {explosionRadius}m, Damage: {explosionDamage}");

        // ===============================================================
        // [핵심 수정] 시각 효과는 RPC를 통해 모든 클라이언트에게 전파
        // ===============================================================
        playerWithBarrier.RPC_TriggerExplosionVfx(explosionPosition, explosionRadius);

        // --- 아래는 물리/데미지 로직 (서버에서만 실행됨) ---
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(explosionPosition, explosionRadius);
        
        foreach (var collider in hitColliders)
        {
            if (collider == null) continue;
            
            EnemyController enemy = collider.GetComponent<EnemyController>() 
                ?? collider.GetComponentInParent<EnemyController>()
                ?? (collider.attachedRigidbody != null ? collider.attachedRigidbody.GetComponent<EnemyController>() : null)
                ?? (collider.transform.root != null ? collider.transform.root.GetComponent<EnemyController>() : null);
            
            // Networked 프로퍼티(IsDead 등)에 직접 접근하기 전에
            // EnemyController와 그 NetworkObject의 유효성을 먼저 확인
            if (enemy == null) continue;
            if (enemy.Object == null || !enemy.Object.IsValid) continue;
            
            float distance = Vector3.Distance(explosionPosition, enemy.transform.position);
            if (distance <= explosionRadius && enemy.State != null)
            {
                // EnemyState 내부에서 Networked 프로퍼티 접근을 try/catch로 보호하므로
                // 여기서는 단순히 데미지만 위임
                enemy.State.TakeDamage(explosionDamage);
            }
        }
    }
    
    /// <summary>
    /// 베리어 조합 데이터를 가져옵니다.
    /// </summary>
    private BarrierMagicCombinationData GetBarrierData()
    {
        if (_gameDataManager?.MagicService == null) return null;
        
        MagicCombinationData combinationData = _gameDataManager.MagicService.GetCombinationDataByResult(10);
        return combinationData as BarrierMagicCombinationData;
    }
    #endregion
    
    #region Cleanup
    private void OnDestroy()
    {
        // 하이라이트 객체 정리
        if (_sharedHighlightInstance != null)
        {
            Destroy(_sharedHighlightInstance);
            _sharedHighlightInstance = null;
            _highlightRenderer = null;
        }
    }
    #endregion
}

