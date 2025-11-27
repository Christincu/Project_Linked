using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

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
    
    /// <summary>
    /// Assets/Datas/Stage 폴더에서 모든 StageData를 자동으로 로드합니다. (에디터 전용)
    /// </summary>
    /// <param name="owner">데이터 저장을 위해 GameDataManager 인스턴스를 받습니다.</param>
    public void LoadDataFromAssets(MonoBehaviour owner)
    {
#if UNITY_EDITOR
        _stages.Clear();
        
        // Assets/Datas/Stage 폴더에서 모든 StageData 찾기
        string[] guids = AssetDatabase.FindAssets($"t:{nameof(StageData)}", new[] { "Assets/Datas/Stage" });
        
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            StageData stageData = AssetDatabase.LoadAssetAtPath<StageData>(assetPath);
            
            if (stageData != null)
            {
                _stages.Add(stageData);
            }
        }
        
        // 이름순으로 정렬
        _stages.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        
        // [중요] 변경 사항 저장 표시 (Dirty Flag)
        // 이 코드가 있어야 씬이나 프리팹 저장 시 리스트가 함께 저장됩니다.
        if (owner != null)
        {
            EditorUtility.SetDirty(owner);
        }
        
        Debug.Log($"[StageService] Loaded {_stages.Count} stages. Ready to save.");
#endif
    }
    
    /// <summary>
    /// 딕셔너리를 초기화합니다. (런타임용)
    /// </summary>
    public void InitializeDictionary()
    {
        if (_isInitialized) return;
        
        // 런타임에서는 LoadDataFromAssets를 자동 호출하지 않음 (이미 저장된 리스트 사용)
        if (_stages == null || _stages.Count == 0)
        {
            Debug.LogWarning("[StageService] Stage list is empty! Please run 'Load All Data From Assets' in the editor and save the scene.");
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
    
    #region Scene Loading
    
    /// <summary>
    /// Loads a stage scene using StageData.
    /// Uses GameManager's LoadSceneWithLoading if available, otherwise loads directly.
    /// </summary>
    /// <param name="stageData">Stage data containing the scene to load</param>
    /// <returns>True if loading started successfully, false otherwise</returns>
    public bool LoadStage(StageData stageData)
    {
        if (stageData == null)
        {
            Debug.LogError("[StageService] Stage data is null!");
            return false;
        }

        string sceneToLoad = stageData.SceneName;
        
        if (string.IsNullOrEmpty(sceneToLoad))
        {
            Debug.LogError($"[StageService] Scene name is empty for stage: {stageData.stageName}");
            return false;
        }

        // Use GameManager's loading screen if available
        if (GameManager.Instance != null)
        {
            GameManager.Instance.LoadSceneWithLoading(sceneToLoad);
            Debug.Log($"[StageService] Loading stage '{stageData.stageName}' (Scene: {sceneToLoad}) with loading screen");
        }
        else
        {
            // Fallback: Direct scene load
            SceneManager.LoadScene(sceneToLoad);
            Debug.Log($"[StageService] Loading stage '{stageData.stageName}' (Scene: {sceneToLoad}) directly");
        }
        
        return true;
    }
    
    /// <summary>
    /// Loads a stage scene by stage code (GUID string).
    /// </summary>
    /// <param name="code">Stage code (GUID string)</param>
    /// <returns>True if loading started successfully, false otherwise</returns>
    public bool LoadStageByCode(string code)
    {
        StageData stageData = GetStageByCode(code);
        if (stageData == null)
        {
            Debug.LogError($"[StageService] Stage with code '{code}' not found!");
            return false;
        }
        
        return LoadStage(stageData);
    }
    
    /// <summary>
    /// Loads a stage scene by index.
    /// </summary>
    /// <param name="index">Stage index</param>
    /// <returns>True if loading started successfully, false otherwise</returns>
    public bool LoadStageByIndex(int index)
    {
        StageData stageData = GetStage(index);
        if (stageData == null)
        {
            Debug.LogError($"[StageService] Stage at index {index} not found!");
            return false;
        }
        
        return LoadStage(stageData);
    }
    
    #endregion
    
    #region Editor Methods
#if UNITY_EDITOR
    /// <summary>
    /// Validates that all stage scenes are added to Build Settings.
    /// Shows warnings for missing scenes.
    /// </summary>
    public void ValidateBuildSettings()
    {
        if (_stages == null || _stages.Count == 0)
        {
            Debug.LogWarning("[StageService] No stages to validate. Load data first.");
            return;
        }
        
        List<string> scenesInBuild = new List<string>();
        for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
        {
            scenesInBuild.Add(System.IO.Path.GetFileNameWithoutExtension(EditorBuildSettings.scenes[i].path));
        }
        
        int missingCount = 0;
        foreach (var stage in _stages)
        {
            if (stage == null) continue;
            
            string sceneName = stage.SceneName;
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogWarning($"[StageService] Stage '{stage.stageName}' has no scene assigned!");
                missingCount++;
                continue;
            }
            
            if (!scenesInBuild.Contains(sceneName))
            {
                Debug.LogWarning($"[StageService] Scene '{sceneName}' (Stage: {stage.stageName}) is NOT in Build Settings! " +
                    $"Please add it via File -> Build Settings -> Scenes In Build");
                missingCount++;
            }
        }
        
        if (missingCount == 0)
        {
            Debug.Log($"[StageService] All {_stages.Count} stage scenes are properly added to Build Settings.");
        }
        else
        {
            Debug.LogWarning($"[StageService] {missingCount} stage scene(s) are missing from Build Settings!");
        }
    }
#endif
    #endregion
}

