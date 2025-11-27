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
    [Tooltip("List of all available enemies - 자동으로 Assets/Datas/Enemy에서 로드됩니다")]
    [SerializeField] private List<EnemyData> _enemies = new List<EnemyData>();
    
    private Dictionary<string, EnemyData> _enemyDict = new Dictionary<string, EnemyData>();
    private bool _isInitialized = false;
    private bool _isDataLoaded = false;
    
    /// <summary>
    /// Assets/Datas/Enemy 폴더에서 모든 EnemyData를 자동으로 로드합니다.
    /// 에디터에서만 작동하며, 런타임에서는 Inspector에 할당된 리스트를 사용합니다.
    /// </summary>
    public void LoadDataFromAssets()
    {
        if (_isDataLoaded) return;
        
#if UNITY_EDITOR
        _enemies.Clear();
        
        // Assets/Datas/Enemy 폴더에서 모든 EnemyData 찾기
        string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(EnemyData)}", new[] { "Assets/Datas/Enemy" });
        
        foreach (string guid in guids)
        {
            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            EnemyData enemyData = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyData>(assetPath);
            
            if (enemyData != null)
            {
                _enemies.Add(enemyData);
            }
        }
        
        // 이름순으로 정렬
        _enemies.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        
        Debug.Log($"[EnemyService] {_enemies.Count}개의 적 데이터를 자동으로 로드했습니다.");
        _isDataLoaded = true;
#else
        // 런타임에서는 이미 Inspector에 할당된 리스트를 사용
        _isDataLoaded = true;
#endif
    }
    
    /// <summary>
    /// 딕셔너리를 초기화합니다. GameDataManager의 Initialize에서 호출됩니다.
    /// </summary>
    public void InitializeDictionary()
    {
        if (_isInitialized) return;
        
        // 데이터가 로드되지 않았다면 먼저 로드
        if (!_isDataLoaded)
        {
            LoadDataFromAssets();
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

