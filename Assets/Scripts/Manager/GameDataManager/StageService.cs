using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StageService는 GameDataManager에서 스테이지 데이터를 관리합니다.
/// </summary>
[System.Serializable]
public class StageService
{
    [Header("Stage List")]
    [Tooltip("List of all available stages - 자동으로 Assets/Datas/Stage에서 로드됩니다")]
    [SerializeField] private List<StageData> _stages = new List<StageData>();
    private Dictionary<string, StageData> _stageDict = new Dictionary<string, StageData>();
    private bool _isInitialized = false;
    private bool _isDataLoaded = false;
    
    /// <summary>
    /// Assets/Datas/Stage 폴더에서 모든 StageData를 자동으로 로드합니다.
    /// 에디터에서만 작동하며, 런타임에서는 Inspector에 할당된 리스트를 사용합니다.
    /// </summary>
    public void LoadDataFromAssets()
    {
        if (_isDataLoaded) return;
        
#if UNITY_EDITOR
        _stages.Clear();
        
        // Assets/Datas/Stage 폴더에서 모든 StageData 찾기
        string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(StageData)}", new[] { "Assets/Datas/Stage" });
        
        foreach (string guid in guids)
        {
            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            StageData stageData = UnityEditor.AssetDatabase.LoadAssetAtPath<StageData>(assetPath);
            
            if (stageData != null)
            {
                _stages.Add(stageData);
            }
        }
        
        // 이름순으로 정렬
        _stages.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        
        Debug.Log($"[StageService] {_stages.Count}개의 스테이지 데이터를 자동으로 로드했습니다.");
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
        
        _stageDict.Clear();
        foreach (var stage in _stages)
        {
            if (stage != null && !string.IsNullOrEmpty(stage.code))
            {
                if (_stageDict.ContainsKey(stage.code))
                {
                    Debug.LogWarning($"Duplicate stage code found: {stage.code}. Skipping duplicate.");
                    continue;
                }
                _stageDict[stage.code] = stage;
            }
        }
        _isInitialized = true;
    }
    
    /// <summary>
    /// Get stage data by index
    /// </summary>
    /// <param name="index">Stage index</param>
    /// <returns>StageData or null if invalid index</returns>
    public StageData GetStage(int index)
    {
        if (index >= 0 && index < _stages.Count)
        {
            return _stages[index];
        }
        
        Debug.LogWarning($"Invalid stage index: {index}");
        return null;
    }
    
    /// <summary>
    /// Get stage data by code (GUID string)
    /// </summary>
    /// <param name="code">Stage code (GUID string)</param>
    /// <returns>StageData or null if not found</returns>
    public StageData GetStageByCode(string code)
    {
        if (!_isInitialized)
        {
            InitializeDictionary();
        }
        
        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning("Stage code is null or empty");
            return null;
        }
        
        if (_stageDict.TryGetValue(code, out StageData stage))
        {
            return stage;
        }
        
        Debug.LogWarning($"Stage with code '{code}' not found");
        return null;
    }
    
    /// <summary>
    /// Get total number of stages
    /// </summary>
    public int StageCount => _stages.Count;
    
    /// <summary>
    /// Get all stages
    /// </summary>
    public List<StageData> GetAllStages()
    {
        return _stages;
    }
}

