using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// 씬 전환 도착지
/// 새로운 씬이 로드되면 이 위치 주변으로 플레이어들이 스폰됩니다.
/// MainGameManager가 자동으로 이 위치를 찾아 플레이어를 배치합니다.
/// </summary>
public class ScenSpawner : MonoBehaviour
{
    #region Serialized Fields
    [Header("Spawn Settings")]
    [Tooltip("스폰 위치 오프셋")]
    [SerializeField] private Vector2[] spawnOffsets = new Vector2[]
    {
        new Vector2(0, 2),
        new Vector2(0, -2)
    };
    
    [Header("Visual Settings")]
    [Tooltip("스폰 지점 표시 색상")]
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 1f, 0.5f);
    
    [Tooltip("스폰 지점 표시 크기")]
    [SerializeField] private float gizmoSize = 1f;
    #endregion
    
    #region Singleton
    public static ScenSpawner Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning($"[ScenSpawner] Multiple ScenSpawners exist in scene. Ignoring {gameObject.name}");
        }
    }
    
    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
    #endregion
    
    #region Public Methods
    /// <summary>
    /// 지정된 인덱스의 스폰 위치를 반환합니다.
    /// </summary>
    public Vector3 GetSpawnPosition(int index)
    {
        if (spawnOffsets != null && spawnOffsets.Length > 0)
        {
            int safeIndex = index % spawnOffsets.Length;
            return transform.position + (Vector3)spawnOffsets[safeIndex];
        }
        
        return transform.position;
    }
    
    /// <summary>
    /// 모든 스폰 위치를 배열로 반환합니다.
    /// </summary>
    public Vector3[] GetAllSpawnPositions()
    {
        if (spawnOffsets != null && spawnOffsets.Length > 0)
        {
            return spawnOffsets
                .Select(offset => transform.position + (Vector3)offset)
                .ToArray();
        }
        
        return new Vector3[] { transform.position };
    }
    
    /// <summary>
    /// 사용 가능한 스폰 위치 개수를 반환합니다.
    /// </summary>
    public int GetSpawnPointCount()
    {
        if (spawnOffsets != null && spawnOffsets.Length > 0)
        {
            return spawnOffsets.Length;
        }
        
        return 1;
    }
    #endregion
    
    #region Gizmos
#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Vector3[] positions = GetAllSpawnPositions();
        
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 spawnPos = positions[i];
            
            // 스폰 지점 표시
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(spawnPos, gizmoSize);
            Gizmos.DrawSphere(spawnPos, gizmoSize * 0.2f);
            
            // 번호 표시 (디버깅용)
            UnityEditor.Handles.Label(spawnPos + Vector3.up * gizmoSize * 0.5f, $"{i + 1}");
        }
        
        // 중앙 포털 효과
        Gizmos.color = Color.cyan;
        DrawPortalEffect(transform.position, gizmoSize * 2f);
    }
    
    void OnDrawGizmosSelected()
    {
        // 선택 시 스폰 지점들을 더 명확하게 표시
        Vector3[] positions = GetAllSpawnPositions();
        
        Gizmos.color = Color.yellow;
        foreach (var pos in positions)
        {
            Gizmos.DrawWireSphere(pos, gizmoSize * 1.5f);
        }
        
        // 중앙에서 각 스폰 지점으로 선 그리기
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        foreach (var pos in positions)
        {
            Gizmos.DrawLine(transform.position, pos);
        }
    }
    
    private void DrawPortalEffect(Vector3 center, float size)
    {
        // 포털 링 그리기
        int segments = 30;
        Vector3 prevPoint = center + new Vector3(Mathf.Cos(0) * size, Mathf.Sin(0) * size, 0);
        
        for (int i = 1; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 newPoint = center + new Vector3(Mathf.Cos(angle) * size, Mathf.Sin(angle) * size, 0);
            Gizmos.DrawLine(prevPoint, newPoint);
            prevPoint = newPoint;
        }
        
        // 내부 링
        prevPoint = center + new Vector3(Mathf.Cos(0) * size * 0.7f, Mathf.Sin(0) * size * 0.7f, 0);
        for (int i = 1; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 newPoint = center + new Vector3(Mathf.Cos(angle) * size * 0.7f, Mathf.Sin(angle) * size * 0.7f, 0);
            Gizmos.DrawLine(prevPoint, newPoint);
            prevPoint = newPoint;
        }
    }
#endif
    #endregion
}
