using System.Collections.Generic;
using UnityEngine;
using Fusion;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 베리어 시각화를 전역적으로 관리하는 싱글톤 매니저
/// 모든 플레이어의 베리어 및 폭발 범위 시각화를 담당합니다.
/// </summary>
public class BarrierVisualizationManager : MonoBehaviour
{
    #region Singleton (Scene-based)
    private static BarrierVisualizationManager _instance;
    public static BarrierVisualizationManager Instance
    {
        get
        {
            // MainGameManager가 있는 경우에만 생성 (게임 씬에서만)
            if (_instance == null && MainGameManager.Instance != null)
            {
                GameObject managerObj = new GameObject("BarrierVisualizationManager");
                _instance = managerObj.AddComponent<BarrierVisualizationManager>();
            }
            return _instance;
        }
    }
    #endregion
    
    #region Private Fields
    private GameDataManager _gameDataManager;
    
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
        public bool isRangeVisible;
        public float lastExplosionRadius;
        public Vector3 lastPosition;
    }
    #endregion
    
    #region Initialization
    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        // GameDataManager 찾기
        _gameDataManager = FindObjectOfType<GameDataManager>();
        if (_gameDataManager == null)
        {
            Debug.LogWarning("[BarrierVisualizationManager] GameDataManager not found. Visualizations may not work correctly.");
        }
    }
    #endregion
    
    #region Update
    void Update()
    {
        // MainGameManager가 없으면 (씬 전환 중 등) 업데이트하지 않음
        if (MainGameManager.Instance == null) return;
        
        // 플레이어가 없으면 업데이트하지 않음 (최적화)
        List<PlayerController> allPlayers = GetAllPlayers();
        if (allPlayers == null || allPlayers.Count == 0) return;
        
        // 베리어가 하나라도 있는지 확인 (최적화)
        bool hasAnyBarrier = false;
        foreach (var player in allPlayers)
        {
            if (player != null && player.HasBarrier)
            {
                hasAnyBarrier = true;
                break;
            }
        }
        
        // 베리어가 하나라도 있거나 추적 중인 시각화가 있으면 업데이트
        if (hasAnyBarrier || _barrierVisuals.Count > 0 || _explosionRangeVisuals.Count > 0)
        {
            // 모든 플레이어의 베리어 시각화 업데이트
            UpdateAllBarrierVisuals();
            
            // 모든 플레이어의 폭발 범위 시각화 업데이트
            UpdateAllExplosionRangeVisuals();
        }
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
                    isRangeVisible = false,
                    lastExplosionRadius = -1f,
                    lastPosition = player.transform.position
                };
            }
            
            var visualData = _explosionRangeVisuals[player];
            
            // 필요 시 폭발 범위 오브젝트 생성
            if (visualData.explosionRangeObj == null)
            {
                InitializeExplosionRangeVisual(player);
                // 여전히 null이면 이 플레이어는 스킵
                if (visualData.explosionRangeObj == null)
                    continue;
            }
            
            // 사망 시 범위 표시 제거
            if (player.IsDead)
            {
                // 역할 종료: 폭발 범위 오브젝트 완전히 제거
                DestroyExplosionRangeVisual(player);
                visualData.isRangeVisible = false;
                continue;
            }
            
            // 베리어가 있고 타이머가 실행 중일 때만 표시
            bool shouldShow = player.HasBarrier && player.BarrierTimer.IsRunning;
            
            // 범위 표시 상태 업데이트
            if (shouldShow != visualData.isRangeVisible)
            {
                if (shouldShow)
                {
                    // 새로 보여줘야 하는 경우: 필요 시 재생성 후 활성화
                    if (visualData.explosionRangeObj == null)
                    {
                        InitializeExplosionRangeVisual(player);
                        if (visualData.explosionRangeObj == null)
                            continue;
                    }

                    UpdateExplosionRangeVisibility(player, true);
                    visualData.isRangeVisible = true;

                    // 표시 상태가 켜질 때 즉시 1회 업데이트
                    if (player.Runner != null)
                    {
                        UpdateExplosionRangeVisualization(player);
                        visualData.lastPosition = player.transform.position;
                        float remainingTime = player.BarrierTimer.RemainingTime(player.Runner) ?? 0f;
                        visualData.lastExplosionRadius = GetExplosionRadius(remainingTime);
                    }
                }
                else
                {
                    // 역할 종료: 폭발 범위 오브젝트 완전히 제거
                    DestroyExplosionRangeVisual(player);
                    visualData.isRangeVisible = false;
                    continue;
                }
            }
            
            // 범위 시각화 업데이트 (범위가 보일 때만)
            if (visualData.isRangeVisible && player.Runner != null)
            {
                Vector3 currentPosition = player.transform.position;
                bool hasMoved = Vector3.Distance(currentPosition, visualData.lastPosition) > 0.01f;
                
                float remainingTime = player.BarrierTimer.RemainingTime(player.Runner) ?? 0f;
                float currentRadius = GetExplosionRadius(remainingTime);
                
                // 위치가 변경되었거나 범위가 변경되었을 때 업데이트
                // 또는 초기 업데이트 (lastExplosionRadius가 -1일 때)
                if (hasMoved || Mathf.Abs(currentRadius - visualData.lastExplosionRadius) > 0.01f || visualData.lastExplosionRadius < 0f)
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
        
        // VisualManager에서 프리팹을 가져와서 사용
        GameObject prefab = null;
        if (VisualManager.Instance != null)
        {
            prefab = VisualManager.Instance.ExplosionRangePrefab;
        }

        if (prefab != null)
        {
            // 프리팹 인스턴스 생성 (레이어/소팅 오더는 프리팹 설정을 그대로 사용)
            visualData.explosionRangeObj = Object.Instantiate(prefab, player.transform);
            visualData.explosionRangeObj.name = "ExplosionRange";
        }
        else
        {
            // 프리팹이 없으면 최소한의 fallback 오브젝트 생성 (SpriteRenderer + 런타임 원형)
            visualData.explosionRangeObj = new GameObject("ExplosionRangeFallback");
            visualData.explosionRangeObj.transform.SetParent(player.transform);
            visualData.explosionRangeObj.transform.localPosition = Vector3.zero;
            visualData.explosionRangeObj.transform.localRotation = Quaternion.identity;

            var rangeRenderer = visualData.explosionRangeObj.AddComponent<SpriteRenderer>();
            Texture2D circleTexture = CreateCircleTexture(128, Color.white);
            Sprite circleSprite = Sprite.Create(circleTexture, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 128);
            rangeRenderer.sprite = circleSprite;
        }

        // 초기 크기 0으로 시작 (Update에서 반지름에 맞게 스케일 조정)
        visualData.explosionRangeObj.transform.localScale = Vector3.zero;

        // 초기화 상태 설정
        visualData.lastExplosionRadius = -1f;
        visualData.lastPosition = player.transform.position;

        // 초기에는 숨김 (SpriteRenderer가 있을 때만)
        var initialRenderer = visualData.explosionRangeObj.GetComponent<SpriteRenderer>();
        if (initialRenderer != null)
        {
            initialRenderer.enabled = false;
        }
    }
    
    /// <summary>
    /// 폭발 범위 표시 여부를 업데이트합니다.
    /// </summary>
    private void UpdateExplosionRangeVisibility(PlayerController player, bool visible)
    {
        if (player == null || !_explosionRangeVisuals.ContainsKey(player)) return;
        
        var visualData = _explosionRangeVisuals[player];
        if (visualData.explosionRangeObj != null)
        {
            var renderer = visualData.explosionRangeObj.GetComponent<SpriteRenderer>();
            if (renderer != null) renderer.enabled = visible;
        }
    }
    
    /// <summary>
    /// 폭발 범위 시각화를 업데이트합니다 (SpriteRenderer + Scale 기반).
    /// </summary>
    private void UpdateExplosionRangeVisualization(PlayerController player)
    {
        if (player == null || player.Runner == null) return;
        if (!_explosionRangeVisuals.ContainsKey(player)) return;
        
        var visualData = _explosionRangeVisuals[player];
        if (visualData.explosionRangeObj == null) return;
        
        float remainingTime = player.BarrierTimer.RemainingTime(player.Runner) ?? 0f;
        float radius = GetExplosionRadius(remainingTime);

        // 1. 크기 업데이트 (반지름 * 2 = 지름)
        if (radius < 0f) radius = 0f;
        float diameter = radius * 2f;
        visualData.explosionRangeObj.transform.localScale = new Vector3(diameter, diameter, 1f);

        // 2. 색상 업데이트
        Color color = GetExplosionColor(remainingTime);
        var renderer = visualData.explosionRangeObj.GetComponent<SpriteRenderer>();
        if (renderer != null)
        {
            renderer.color = color;
        }
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
            visualData.explosionRangeObj = null;
        }
    }
    #endregion
    
    #region Helper Methods
    /// <summary>
    /// 남은 시간에 따른 폭발 반지름을 가져옵니다.
    /// </summary>
    private float GetExplosionRadius(float remainingTime)
    {
        if (_gameDataManager == null || _gameDataManager.MagicService == null) return 0f;
        
        // 베리어 마법 코드는 10 (Air + Soil 조합)
        MagicCombinationData combinationData = _gameDataManager.MagicService.GetCombinationDataByResult(10);
        
        if (combinationData is BarrierMagicCombinationData barrierData)
        {
            return barrierData.GetExplosionRadius(remainingTime);
        }
        
        return 0f;
    }
    
    /// <summary>
    /// 남은 시간에 따른 폭발 색상을 가져옵니다.
    /// </summary>
    private Color GetExplosionColor(float remainingTime)
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
            if (_gameDataManager != null && _gameDataManager.MagicService != null)
            {
                MagicCombinationData combinationData = _gameDataManager.MagicService.GetCombinationDataByResult(10);
                if (combinationData is BarrierMagicCombinationData barrierData)
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
    }
#endif
    #endregion
    
    #region Cleanup
    void OnDestroy()
    {
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
        
        // 인스턴스 정리
        if (_instance == this)
        {
            _instance = null;
        }
    }
    #endregion
}
