using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// EnemyService는 GameDataManager에서 적 데이터를 관리합니다.
/// </summary>
[System.Serializable]
public class EnemyService
{
    [Header("Enemy List")]
    [Tooltip("List of all available enemies - 자동으로 Assets/Datas/Enemy에서 로드됩니다")]
    [SerializeField] private List<EnemyData> _enemies = new List<EnemyData>();
    
    private Dictionary<string, EnemyData> _enemyDict = new Dictionary<string, EnemyData>();
    private bool _isInitialized = false;
    
    /// <summary>
    /// Assets/Datas/Enemy 폴더에서 모든 EnemyData를 자동으로 로드합니다. (에디터 전용)
    /// </summary>
    /// <param name="owner">데이터 저장을 위해 GameDataManager 인스턴스를 받습니다.</param>
    public void LoadDataFromAssets(MonoBehaviour owner)
    {
#if UNITY_EDITOR
        _enemies.Clear();
        
        // Assets/Datas/Enemy 폴더에서 모든 EnemyData 찾기
        string[] guids = AssetDatabase.FindAssets($"t:{nameof(EnemyData)}", new[] { "Assets/Datas/Enemy" });
        
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            EnemyData enemyData = AssetDatabase.LoadAssetAtPath<EnemyData>(assetPath);
            
            if (enemyData != null)
            {
                _enemies.Add(enemyData);
            }
        }
        
        // 이름순으로 정렬
        _enemies.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        
        // [중요] 변경 사항 저장 표시 (Dirty Flag)
        // 이 코드가 있어야 씬이나 프리팹 저장 시 리스트가 함께 저장됩니다.
        if (owner != null)
        {
            EditorUtility.SetDirty(owner);
        }
        
        Debug.Log($"[EnemyService] Loaded {_enemies.Count} enemies. Ready to save.");
#endif
    }
    
    /// <summary>
    /// 딕셔너리를 초기화합니다. (런타임용)
    /// </summary>
    public void InitializeDictionary()
    {
        if (_isInitialized) return;
        
        // 런타임에서는 LoadDataFromAssets를 자동 호출하지 않음 (이미 저장된 리스트 사용)
        if (_enemies == null || _enemies.Count == 0)
        {
            Debug.LogWarning("[EnemyService] Enemy list is empty! Please run 'Load All Data From Assets' in the editor and save the scene.");
        }
        
        _enemyDict.Clear();
        foreach (var enemy in _enemies)
        {
            if (enemy != null && !string.IsNullOrEmpty(enemy.code))
            {
                if (_enemyDict.ContainsKey(enemy.code))
                {
                    Debug.LogWarning($"Duplicate enemy code found: {enemy.code}. Skipping duplicate.");
                    continue;
                }
                _enemyDict[enemy.code] = enemy;
            }
        }
        _isInitialized = true;
    }
    
    /// <summary>
    /// Get enemy data by index
    /// </summary>
    /// <param name="index">Enemy index</param>
    /// <returns>EnemyData or null if invalid index</returns>
    public EnemyData GetEnemy(int index)
    {
        if (index >= 0 && index < _enemies.Count)
        {
            return _enemies[index];
        }
        
        Debug.LogWarning($"Invalid enemy index: {index}");
        return null;
    }
    
    /// <summary>
    /// Get enemy data by code
    /// </summary>
    /// <param name="code">Enemy code (GUID string)</param>
    /// <returns>EnemyData or null if not found</returns>
    public EnemyData GetEnemyByCode(string code)
    {
        if (!_isInitialized)
        {
            InitializeDictionary();
        }
        
        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning("Enemy code is null or empty");
            return null;
        }
        
        if (_enemyDict.TryGetValue(code, out EnemyData enemy))
        {
            return enemy;
        }
        
        Debug.LogWarning($"Enemy with code '{code}' not found");
        return null;
    }
    
    /// <summary>
    /// Get total number of enemies
    /// </summary>
    public int EnemyCount => _enemies.Count;
    
    /// <summary>
    /// Get all enemies
    /// </summary>
    public List<EnemyData> GetAllEnemies()
    {
        return _enemies;
    }
}

