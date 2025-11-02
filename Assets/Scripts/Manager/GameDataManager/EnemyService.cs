using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EnemyService는 GameDataManager에서 적 데이터를 관리합니다.
/// </summary>
[System.Serializable]
public class EnemyService
{
    [Header("Enemy List")]
    [Tooltip("List of all available enemies")]
    [SerializeField] private List<EnemyData> enemies = new List<EnemyData>();
    
    /// <summary>
    /// Get enemy data by index
    /// </summary>
    /// <param name="index">Enemy index</param>
    /// <returns>EnemyData or null if invalid index</returns>
    public EnemyData GetEnemy(int index)
    {
        if (index >= 0 && index < enemies.Count)
        {
            return enemies[index];
        }
        
        Debug.LogWarning($"Invalid enemy index: {index}");
        return null;
    }
    
    /// <summary>
    /// Get total number of enemies
    /// </summary>
    public int EnemyCount => enemies.Count;
    
    /// <summary>
    /// Get all enemies
    /// </summary>
    public List<EnemyData> GetAllEnemies()
    {
        return enemies;
    }
}

