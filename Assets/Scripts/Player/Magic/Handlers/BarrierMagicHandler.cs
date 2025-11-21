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
    
    // 선택 모드 관련
    private PlayerController _selectedPlayer = null;
    private Dictionary<PlayerController, GameObject> _highlightObjects = new Dictionary<PlayerController, GameObject>();
    private Color _highlightColor = new Color(0.2f, 1f, 0.2f, 1f); // 연두색
    private float _highlightThickness = 0.1f;
    
    // 베리어 시각화 관련 (모든 플레이어 추적)
    private Dictionary<PlayerController, BarrierVisualData> _barrierVisuals = new Dictionary<PlayerController, BarrierVisualData>();
    
    // 폭발 범위 시각화 관련 (모든 플레이어 추적)
    private Dictionary<PlayerController, ExplosionRangeVisualData> _explosionRangeVisuals = new Dictionary<PlayerController, ExplosionRangeVisualData>();
    #endregion
    
    #region Visual Data Structures
    /// <summary>
    /// 베리어 시각화 데이터
    /// </summary>
    private class BarrierVisualData
    {
        public GameObject barrierVisualObject;
        public bool previousHasBarrier;
    }
    
    /// <summary>
    /// 폭발 범위 시각화 데이터
    /// </summary>
    private class ExplosionRangeVisualData
    {
        public GameObject explosionRangeObj;
        public MeshFilter explosionRangeMeshFilter;
        public MeshRenderer explosionRangeMeshRenderer;
        public Mesh explosionRangeMesh;
        public bool isRangeVisible;
        public float lastExplosionRadius;
        public Vector3 lastPosition;
    }
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
                ApplyBarrierToPlayer(targetPlayer);
                
                // 마법 비활성화
                _controller.RPC_DeactivateMagic();
            }
        }
    }
    
    public void Update()
    {
        // 하이라이트 시각 효과 업데이트
        UpdateHighlightVisuals();
        
        // 모든 플레이어의 베리어 시각화 업데이트
        UpdateAllBarrierVisuals();
        
        // 모든 플레이어의 폭발 범위 시각화 업데이트
        UpdateAllExplosionRangeVisuals();
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
    /// BarrierMagicObject를 스폰하여 처리합니다.
    /// </summary>
    public void ApplyBarrierToPlayer(PlayerController targetPlayer)
    {
        if (targetPlayer == null || _controller == null) return;
        if (!_controller.Object.HasStateAuthority) return;
        if (_gameDataManager == null) return;

        // BarrierMagicObject 프리팹 가져오기
        NetworkPrefabRef barrierPrefab = _magicController.GetBarrierMagicObjectPrefab();
        if (!barrierPrefab.IsValid) return;

        // BarrierMagicObject 스폰 (Input Authority를 Owner로 설정)
        var barrierObj = _controller.Runner.Spawn(
            barrierPrefab, 
            _controller.transform.position, 
            Quaternion.identity, 
            _controller.Object.InputAuthority, // Input Authority를 Owner로 설정
            (runner, obj) =>
            {
                var barrierMagic = obj.GetComponent<BarrierMagicObject>();
                if (barrierMagic != null)
                {
                    barrierMagic.Initialize(_controller, 10); // 마법 코드 10
                }
            }
        );
        
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
    /// 남은 시간에 따른 폭발 반지름을 가져옵니다.
    /// </summary>
    public float GetExplosionRadius(float remainingTime)
    {
        BarrierMagicCombinationData barrierData = GetBarrierData();
        if (barrierData == null) return 0f;
        
        return barrierData.GetExplosionRadius(remainingTime);
    }
    
    /// <summary>
    /// 남은 시간에 따른 폭발 색상을 가져옵니다.
    /// </summary>
    public Color GetExplosionColor(float remainingTime)
    {
        if (remainingTime > 7f)
        {
            return new Color(1f, 0f, 0f, 0.3f); // 빨간색
        }
        else if (remainingTime > 3f)
        {
            return new Color(1f, 0.5f, 0f, 0.3f); // 주황색
        }
        else
        {
            return new Color(1f, 0.8f, 0f, 0.3f); // 노란색
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
    
    #region Barrier Visuals
    /// <summary>
    /// 모든 플레이어의 베리어 시각화를 업데이트합니다.
    /// </summary>
    private void UpdateAllBarrierVisuals()
    {
        List<PlayerController> allPlayers = GetAllPlayers();
        
        foreach (var player in allPlayers)
        {
            if (player == null) continue;
            
            // 베리어 시각화 데이터 가져오기 또는 생성
            if (!_barrierVisuals.ContainsKey(player))
            {
                _barrierVisuals[player] = new BarrierVisualData
                {
                    barrierVisualObject = null,
                    previousHasBarrier = false
                };
            }
            
            var visualData = _barrierVisuals[player];
            
            // 사망 시 베리어 시각화 제거
            if (player.IsDead && visualData.barrierVisualObject != null)
            {
                DestroyBarrierVisual(player);
                visualData.previousHasBarrier = false;
                continue;
            }
            
            // 베리어 상태에 따라 시각화 업데이트
            if (player.HasBarrier && visualData.barrierVisualObject == null)
            {
                CreateBarrierVisual(player);
            }
            else if (!player.HasBarrier && visualData.barrierVisualObject != null)
            {
                DestroyBarrierVisual(player);
            }
            
            visualData.previousHasBarrier = player.HasBarrier;
        }
        
        // 사라진 플레이어 정리
        var keysToRemove = new List<PlayerController>();
        foreach (var kvp in _barrierVisuals)
        {
            if (kvp.Key == null || !allPlayers.Contains(kvp.Key))
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        foreach (var key in keysToRemove)
        {
            if (_barrierVisuals.ContainsKey(key))
            {
                DestroyBarrierVisual(key);
                _barrierVisuals.Remove(key);
            }
        }
    }
    
    /// <summary>
    /// 플레이어에게 베리어 시각 효과를 생성합니다.
    /// </summary>
    private void CreateBarrierVisual(PlayerController player)
    {
        if (player == null) return;
        
        GameObject viewObj = player.ViewObj;
        if (viewObj == null) return;
        
        if (!_barrierVisuals.ContainsKey(player))
        {
            _barrierVisuals[player] = new BarrierVisualData();
        }
        
        var visualData = _barrierVisuals[player];
        if (visualData.barrierVisualObject != null) return;
        
        // 보호막 오브젝트 생성
        visualData.barrierVisualObject = new GameObject("BarrierVisual");
        visualData.barrierVisualObject.transform.SetParent(viewObj.transform, false);
        visualData.barrierVisualObject.transform.localPosition = Vector3.zero;
        
        // SpriteRenderer 추가
        SpriteRenderer barrierRenderer = visualData.barrierVisualObject.AddComponent<SpriteRenderer>();
        
        // 기본 원형 스프라이트 생성
        Texture2D barrierTexture = CreateCircleTexture(64, new Color(0.2f, 0.8f, 1f, 0.5f)); // 반투명 청록색
        Sprite barrierSprite = Sprite.Create(barrierTexture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64);
        barrierRenderer.sprite = barrierSprite;
        
        // 플레이어보다 약간 크게 설정
        visualData.barrierVisualObject.transform.localScale = Vector3.one * 2.0f;
        SpriteRenderer playerRenderer = viewObj.GetComponent<SpriteRenderer>();
        if (playerRenderer != null)
        {
            barrierRenderer.sortingOrder = playerRenderer.sortingOrder + 1;
        }
        else
        {
            barrierRenderer.sortingOrder = 1;
        }
    }
    
    /// <summary>
    /// 플레이어의 베리어 시각 효과를 제거합니다.
    /// </summary>
    private void DestroyBarrierVisual(PlayerController player)
    {
        if (player == null || !_barrierVisuals.ContainsKey(player)) return;
        
        var visualData = _barrierVisuals[player];
        if (visualData.barrierVisualObject != null)
        {
            Destroy(visualData.barrierVisualObject);
            visualData.barrierVisualObject = null;
        }
    }
    
    /// <summary>
    /// 원형 텍스처를 생성합니다.
    /// </summary>
    private Texture2D CreateCircleTexture(int size, Color color)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.4f;
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 pos = new Vector2(x, y);
                float distance = Vector2.Distance(pos, center);
                
                if (distance <= radius)
                {
                    // 거리에 따라 알파값 조정 (외곽은 더 투명하게)
                    float alpha = 1f - (distance / radius) * 0.5f;
                    texture.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
                else
                {
                    texture.SetPixel(x, y, Color.clear);
                }
            }
        }
        
        texture.Apply();
        return texture;
    }
    #endregion
    
    #region Explosion Range Visuals
    /// <summary>
    /// 모든 플레이어의 폭발 범위 시각화를 업데이트합니다.
    /// </summary>
    private void UpdateAllExplosionRangeVisuals()
    {
        List<PlayerController> allPlayers = GetAllPlayers();
        
        foreach (var player in allPlayers)
        {
            if (player == null) continue;
            
            // 폭발 범위 시각화 데이터 가져오기 또는 생성
            if (!_explosionRangeVisuals.ContainsKey(player))
            {
                _explosionRangeVisuals[player] = new ExplosionRangeVisualData
                {
                    explosionRangeObj = null,
                    explosionRangeMeshFilter = null,
                    explosionRangeMeshRenderer = null,
                    explosionRangeMesh = null,
                    isRangeVisible = false,
                    lastExplosionRadius = -1f,
                    lastPosition = player.transform.position
                };
                InitializeExplosionRangeVisual(player);
            }
            
            var visualData = _explosionRangeVisuals[player];
            if (visualData.explosionRangeMesh == null) continue;
            
            // 사망 시 범위 표시 제거
            if (player.IsDead)
            {
                if (visualData.isRangeVisible)
                {
                    UpdateExplosionRangeVisibility(player, false);
                    visualData.isRangeVisible = false;
                }
                continue;
            }
            
            // 베리어가 있고 타이머가 실행 중일 때만 표시
            bool shouldShow = player.HasBarrier && player.BarrierTimer.IsRunning;
            
            // 범위 표시 상태 업데이트
            if (shouldShow != visualData.isRangeVisible)
            {
                UpdateExplosionRangeVisibility(player, shouldShow);
                visualData.isRangeVisible = shouldShow;
            }
            
            // 범위 시각화 업데이트 (범위가 보일 때만)
            if (visualData.isRangeVisible && player.Runner != null)
            {
                Vector3 currentPosition = player.transform.position;
                bool hasMoved = Vector3.Distance(currentPosition, visualData.lastPosition) > 0.01f;
                
                float remainingTime = player.BarrierTimer.RemainingTime(player.Runner) ?? 0f;
                float currentRadius = GetExplosionRadius(remainingTime);
                
                // 위치가 변경되었거나 범위가 변경되었을 때 업데이트
                if (hasMoved || Mathf.Abs(currentRadius - visualData.lastExplosionRadius) > 0.01f)
                {
                    UpdateExplosionRangeVisualization(player);
                    visualData.lastPosition = currentPosition;
                    visualData.lastExplosionRadius = currentRadius;
                }
            }
        }
        
        // 사라진 플레이어 정리
        var keysToRemove = new List<PlayerController>();
        foreach (var kvp in _explosionRangeVisuals)
        {
            if (kvp.Key == null || !allPlayers.Contains(kvp.Key))
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        foreach (var key in keysToRemove)
        {
            if (_explosionRangeVisuals.ContainsKey(key))
            {
                DestroyExplosionRangeVisual(key);
                _explosionRangeVisuals.Remove(key);
            }
        }
    }
    
    /// <summary>
    /// 플레이어의 폭발 범위 시각화를 초기화합니다.
    /// </summary>
    private void InitializeExplosionRangeVisual(PlayerController player)
    {
        if (player == null) return;
        if (!_explosionRangeVisuals.ContainsKey(player)) return;
        
        var visualData = _explosionRangeVisuals[player];
        
        // 폭발 범위 Mesh 생성
        visualData.explosionRangeObj = new GameObject("ExplosionRangeMesh");
        visualData.explosionRangeObj.transform.SetParent(player.transform);
        visualData.explosionRangeObj.transform.localPosition = Vector3.zero;
        
        visualData.explosionRangeMeshFilter = visualData.explosionRangeObj.AddComponent<MeshFilter>();
        visualData.explosionRangeMeshRenderer = visualData.explosionRangeObj.AddComponent<MeshRenderer>();
        
        // 메시 생성
        visualData.explosionRangeMesh = new Mesh();
        visualData.explosionRangeMesh.name = "ExplosionRangeMesh";
        visualData.explosionRangeMeshFilter.mesh = visualData.explosionRangeMesh;
        
        // 머티리얼 설정 (반투명)
        Material material = new Material(Shader.Find("Sprites/Default"));
        material.color = new Color(1f, 0f, 0f, 0.3f); // 기본 빨간색
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000; // Transparent
        visualData.explosionRangeMeshRenderer.material = material;
        visualData.explosionRangeMeshRenderer.sortingOrder = 0;
        
        // 초기화
        visualData.lastExplosionRadius = -1f;
        visualData.lastPosition = player.transform.position;
        UpdateExplosionRangeVisualization(player);
        UpdateExplosionRangeVisibility(player, false); // 초기에는 숨김
    }
    
    /// <summary>
    /// 폭발 범위 표시 여부를 업데이트합니다.
    /// </summary>
    private void UpdateExplosionRangeVisibility(PlayerController player, bool visible)
    {
        if (player == null || !_explosionRangeVisuals.ContainsKey(player)) return;
        
        var visualData = _explosionRangeVisuals[player];
        if (visualData.explosionRangeMeshRenderer != null)
        {
            visualData.explosionRangeMeshRenderer.enabled = visible;
        }
    }
    
    /// <summary>
    /// 폭발 범위 시각화를 업데이트합니다 (원형 Mesh 생성).
    /// </summary>
    private void UpdateExplosionRangeVisualization(PlayerController player)
    {
        if (player == null || player.Runner == null) return;
        if (!_explosionRangeVisuals.ContainsKey(player)) return;
        
        var visualData = _explosionRangeVisuals[player];
        if (visualData.explosionRangeMesh == null) return;
        
        float remainingTime = player.BarrierTimer.RemainingTime(player.Runner) ?? 0f;
        float radius = GetExplosionRadius(remainingTime);
        if (radius <= 0f) return; // 데이터가 없으면 시각화하지 않음
        
        Color color = GetExplosionColor(remainingTime);
        
        // 머티리얼 색상 업데이트
        if (visualData.explosionRangeMeshRenderer != null && visualData.explosionRangeMeshRenderer.material != null)
        {
            visualData.explosionRangeMeshRenderer.material.color = color;
        }
        
        // 원형 Mesh 생성 (벽 감지 없이)
        RangeVisualizationUtils.UpdateCircleMesh(visualData.explosionRangeMesh, radius, 64);
    }
    
    /// <summary>
    /// 플레이어의 폭발 범위 시각화를 제거합니다.
    /// </summary>
    private void DestroyExplosionRangeVisual(PlayerController player)
    {
        if (player == null || !_explosionRangeVisuals.ContainsKey(player)) return;
        
        var visualData = _explosionRangeVisuals[player];
        if (visualData.explosionRangeObj != null)
        {
            Destroy(visualData.explosionRangeObj);
        }
    }
    #endregion
    
    #region Gizmos
#if UNITY_EDITOR
    /// <summary>
    /// Unity 에디터에서 자폭 베리어 폭발 범위를 시각화합니다.
    /// </summary>
    void OnDrawGizmos()
    {
        List<PlayerController> allPlayers = GetAllPlayers();
        
        foreach (var player in allPlayers)
        {
            if (player == null) continue;
            if (!player.HasBarrier || !player.BarrierTimer.IsRunning) continue;
            if (player.Runner == null) continue;
            
            float remainingTime = player.BarrierTimer.RemainingTime(player.Runner) ?? 0f;
            Vector3 center = player.transform.position;
            
            float explosionRadius = GetExplosionRadius(remainingTime);
            if (explosionRadius <= 0f) continue;
            
            Color explosionColor = GetExplosionColor(remainingTime);
            
            // 폭발 범위 표시 (반투명 원)
            Gizmos.color = explosionColor;
            RangeVisualizationUtils.DrawWireCircle(center, explosionRadius, 32);
            
            // 내부 원 채우기
            Gizmos.color = new Color(explosionColor.r, explosionColor.g, explosionColor.b, 0.1f);
            RangeVisualizationUtils.DrawSolidCircle(center, explosionRadius, 32);
        }
    }
    
    /// <summary>
    /// 오브젝트가 선택되었을 때 더 명확하게 표시합니다.
    /// </summary>
    void OnDrawGizmosSelected()
    {
        List<PlayerController> allPlayers = GetAllPlayers();
        
        foreach (var player in allPlayers)
        {
            if (player == null) continue;
            if (!player.HasBarrier || !player.BarrierTimer.IsRunning) continue;
            if (player.Runner == null) continue;
            
            float remainingTime = player.BarrierTimer.RemainingTime(player.Runner) ?? 0f;
            Vector3 center = player.transform.position;
            
            float explosionRadius = GetExplosionRadius(remainingTime);
            if (explosionRadius <= 0f) continue;
            
            Color explosionColor;
            string phaseText;
            if (remainingTime > 7f)
            {
                explosionColor = Color.red;
                phaseText = "Phase 1 (10s~7s)";
            }
            else if (remainingTime > 3f)
            {
                explosionColor = new Color(1f, 0.5f, 0f); // 주황색
                phaseText = "Phase 2 (7s~3s)";
            }
            else
            {
                explosionColor = Color.yellow;
                phaseText = "Phase 3 (3s~0s)";
            }
            
            // 폭발 데이터 가져오기
            BarrierMagicCombinationData barrierData = GetBarrierData();
            if (barrierData != null)
            {
                barrierData.GetExplosionData(remainingTime, out float explosionRadius2, out float explosionDamage);
                
                // 외곽선 강조
                Gizmos.color = explosionColor;
                RangeVisualizationUtils.DrawWireCircle(center, explosionRadius2, 32);
                
                // 내부 원 추가 (범위 강조)
                Gizmos.color = new Color(explosionColor.r, explosionColor.g, explosionColor.b, 0.1f);
                RangeVisualizationUtils.DrawSolidCircle(center, explosionRadius2, 32);
                
                // 중심점 표시
                Gizmos.color = explosionColor;
                Gizmos.DrawWireSphere(center, 0.2f);
                
                // 폭발 범위 텍스트 표시
                Handles.Label(
                    center + Vector3.up * (explosionRadius2 + 0.5f),
                    $"Explosion Range: {explosionRadius2:F1}m\nDamage: {explosionDamage}\n{phaseText}\nRemaining: {remainingTime:F2}s"
                );
            }
        }
    }
#endif
    #endregion
    
    #region Helper Methods
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
        ClearAllHighlights();
        
        // 모든 베리어 시각화 정리
        foreach (var kvp in _barrierVisuals)
        {
            if (kvp.Value.barrierVisualObject != null)
            {
                Destroy(kvp.Value.barrierVisualObject);
            }
        }
        _barrierVisuals.Clear();
        
        // 모든 폭발 범위 시각화 정리
        foreach (var kvp in _explosionRangeVisuals)
        {
            if (kvp.Value.explosionRangeObj != null)
            {
                Destroy(kvp.Value.explosionRangeObj);
            }
        }
        _explosionRangeVisuals.Clear();
    }
    #endregion
}

