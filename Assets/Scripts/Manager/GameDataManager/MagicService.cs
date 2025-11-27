using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class MagicService
{
    [Header("Magic List")]
    [Tooltip("List of all available magics - 자동으로 Assets/Datas/Magic에서 로드됩니다")]
    [SerializeField] private List<MagicData> _magics = new List<MagicData>();
    
    [Header("Magic Combinations")]
    [Tooltip("마법 조합 규칙 리스트 - 자동으로 Assets/Datas/MagicCombination에서 로드됩니다")]
    [SerializeField] private List<MagicCombinationData> _combinations = new List<MagicCombinationData>();
    
    private Dictionary<string, MagicData> _magicDict = new Dictionary<string, MagicData>();
    private Dictionary<string, MagicCombinationData> _combinationDict = new Dictionary<string, MagicCombinationData>();
    private bool _isInitialized = false;
    private bool _isDataLoaded = false;
    
    /// <summary>
    /// Assets/Datas/Magic 및 Assets/Datas/MagicCombination 폴더에서 모든 데이터를 자동으로 로드합니다.
    /// 에디터에서만 작동하며, 런타임에서는 Inspector에 할당된 리스트를 사용합니다.
    /// </summary>
    public void LoadDataFromAssets()
    {
        if (_isDataLoaded) return;
        
#if UNITY_EDITOR
        _magics.Clear();
        _combinations.Clear();
        
        // Assets/Datas/Magic 폴더에서 모든 MagicData 찾기
        string[] magicGuids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(MagicData)}", new[] { "Assets/Datas/Magic" });
        
        foreach (string guid in magicGuids)
        {
            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            MagicData magicData = UnityEditor.AssetDatabase.LoadAssetAtPath<MagicData>(assetPath);
            
            if (magicData != null)
            {
                _magics.Add(magicData);
            }
        }
        
        // 이름순으로 정렬
        _magics.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        
        // Assets/Datas/MagicCombination 폴더에서 모든 MagicCombinationData 찾기
        string[] combinationGuids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(MagicCombinationData)}", new[] { "Assets/Datas/MagicCombination" });
        
        foreach (string guid in combinationGuids)
        {
            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            MagicCombinationData combinationData = UnityEditor.AssetDatabase.LoadAssetAtPath<MagicCombinationData>(assetPath);
            
            if (combinationData != null)
            {
                _combinations.Add(combinationData);
            }
        }
        
        // 이름순으로 정렬
        _combinations.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        
        Debug.Log($"[MagicService] {_magics.Count}개의 마법 데이터와 {_combinations.Count}개의 조합 데이터를 자동으로 로드했습니다.");
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
        
        _magicDict.Clear();
        foreach (var magic in _magics)
        {
            if (magic != null && !string.IsNullOrEmpty(magic.code))
            {
                if (_magicDict.ContainsKey(magic.code))
                {
                    Debug.LogWarning($"Duplicate magic code found: {magic.code}. Skipping duplicate.");
                    continue;
                }
                _magicDict[magic.code] = magic;
            }
        }
        
        // 조합 데이터 딕셔너리 초기화
        _combinationDict.Clear();
        foreach (var combination in _combinations)
        {
            if (combination != null && combination.IsValid() && !string.IsNullOrEmpty(combination.code))
            {
                if (_combinationDict.ContainsKey(combination.code))
                {
                    Debug.LogWarning($"Duplicate combination code found: {combination.code}. Skipping duplicate.");
                    continue;
                }
                _combinationDict[combination.code] = combination;
            }
        }
        
        _isInitialized = true;
    }
    
    /// <summary>
    /// Get magic data by index
    /// </summary>
    /// <param name="index">Magic index</param>
    /// <returns>MagicData or null if invalid index</returns>
    public MagicData GetMagic(int index)
    {
        if (index >= 0 && index < _magics.Count)
        {
            return _magics[index];
        }
        
        Debug.LogWarning($"Invalid magic index: {index}");
        return null;
    }
    
    /// <summary>
    /// Get magic data by magic code
    /// </summary>
    /// <param name="magicCode">Magic code (integer identifier)</param>
    /// <returns>MagicData or null if not found</returns>
    public MagicData GetMagicByName(int magicCode)
    {
        foreach (var magic in _magics)
        {
            if (magic != null && magic.magicCode == magicCode)
            {
                return magic;
            }
        }
        
        Debug.LogWarning($"Magic with code {magicCode} not found");
        return null;
    }
    
    /// <summary>
    /// Get magic data by code (GUID string)
    /// </summary>
    /// <param name="code">Magic code (GUID string)</param>
    /// <returns>MagicData or null if not found</returns>
    public MagicData GetMagicByCode(string code)
    {
        if (!_isInitialized)
        {
            InitializeDictionary();
        }
        
        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning("Magic code is null or empty");
            return null;
        }
        
        if (_magicDict.TryGetValue(code, out MagicData magic))
        {
            return magic;
        }
        
        Debug.LogWarning($"Magic with code '{code}' not found");
        return null;
    }
    
    /// <summary>
    /// Get total number of magics
    /// </summary>
    public int MagicCount => _magics.Count;
    
    /// <summary>
    /// Get all magics
    /// </summary>
    public List<MagicData> GetAllMagics()
    {
        return _magics;
    }
    
    /// <summary>
    /// 두 마법 코드를 조합하여 결과 마법 코드를 반환합니다.
    /// 조합이 존재하면 결과 마법 코드를, 존재하지 않으면 -1을 반환합니다.
    /// 순서를 고려하여 먼저 시전된 마법이 첫 번째 인자로 전달되어야 합니다.
    /// </summary>
    /// <param name="magicCode1">먼저 시전된 마법 코드 (ActivatedMagicCode)</param>
    /// <param name="magicCode2">나중에 시전된 마법 코드 (AbsorbedMagicCode)</param>
    /// <returns>조합 결과 마법 코드, 조합이 없으면 -1</returns>
    public int GetCombinedMagic(int magicCode1, int magicCode2)
    {
        // 하나라도 유효하지 않은 마법 코드면 조합 불가
        if (magicCode1 == -1 || magicCode2 == -1)
        {
            return -1;
        }
        
        // 같은 마법끼리는 조합 불가
        if (magicCode1 == magicCode2)
        {
            return -1;
        }
        
        // 먼저 순서를 고려한 조합 찾기 (정확한 순서)
        foreach (var combination in _combinations)
        {
            if (combination != null && combination.IsValid() && combination.MatchesOrdered(magicCode1, magicCode2))
            {
                return combination.resultMagicCode;
            }
        }

        // 순서를 고려한 조합을 찾지 못하면, 이 순서에서는 조합 마법이 없는 것으로 간주
        return -1;
    }
    
    /// <summary>
    /// 두 마법 코드를 조합하여 결과 MagicData를 반환합니다.
    /// </summary>
    /// <param name="magicCode1">첫 번째 마법 코드</param>
    /// <param name="magicCode2">두 번째 마법 코드</param>
    /// <returns>조합 결과 MagicData, 조합이 없으면 null</returns>
    public MagicData GetCombinedMagicData(int magicCode1, int magicCode2)
    {
        int resultCode = GetCombinedMagic(magicCode1, magicCode2);
        if (resultCode == -1)
        {
            return null;
        }
        
        // resultMagicData가 없으면 마법 코드로 조회
        return GetMagicByName(resultCode);
    }
    
    /// <summary>
    /// 조합 규칙 리스트 가져오기 (에디터용)
    /// </summary>
    public List<MagicCombinationData> GetAllCombinations()
    {
        return _combinations;
    }
    
    /// <summary>
    /// 두 마법 코드를 조합하여 결과 MagicCombinationData를 반환합니다.
    /// </summary>
    /// <param name="magicCode1">첫 번째 마법 코드</param>
    /// <param name="magicCode2">두 번째 마법 코드</param>
    /// <returns>조합 결과 MagicCombinationData, 조합이 없으면 null</returns>
    public MagicCombinationData GetCombinationData(int magicCode1, int magicCode2)
    {
        // 하나라도 유효하지 않은 마법 코드면 조합 불가
        if (magicCode1 == -1 || magicCode2 == -1)
        {
            return null;
        }
        
        // 같은 마법끼리는 조합 불가
        if (magicCode1 == magicCode2)
        {
            return null;
        }
        
        // 먼저 순서를 고려한 조합 찾기 (정확한 순서)
        foreach (var combination in _combinations)
        {
            if (combination != null && combination.IsValid() && combination.MatchesOrdered(magicCode1, magicCode2))
            {
                return combination;
            }
        }
        
        // 순서를 고려한 조합이 없으면 순서 무관 조합 찾기
        foreach (var combination in _combinations)
        {
            if (combination != null && combination.IsValid() && combination.Matches(magicCode1, magicCode2))
            {
                return combination;
            }
        }
        
        // 조합을 찾지 못함
        return null;
    }
    
    /// <summary>
    /// 결과 마법 코드로 조합 데이터를 찾습니다. (기존 코드 호환성을 위해 유지)
    /// </summary>
    /// <param name="resultMagicCode">결과 마법 코드</param>
    /// <returns>조합 데이터, 없으면 null</returns>
    public MagicCombinationData GetCombinationDataByResult(int resultMagicCode)
    {
        if (!_isInitialized)
        {
            InitializeDictionary();
        }
        
        // 리스트를 순회하여 resultMagicCode로 찾기
        foreach (var combination in _combinations)
        {
            if (combination != null && combination.IsValid() && combination.resultMagicCode == resultMagicCode)
            {
                return combination;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// 코드(code)로 조합 데이터를 찾습니다.
    /// </summary>
    /// <param name="code">조합 데이터 코드 (GUID string)</param>
    /// <returns>조합 데이터, 없으면 null</returns>
    public MagicCombinationData GetCombinationDataByCode(string code)
    {
        if (!_isInitialized)
        {
            InitializeDictionary();
        }
        
        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning("Combination code is null or empty");
            return null;
        }
        
        if (_combinationDict.TryGetValue(code, out MagicCombinationData combination))
        {
            return combination;
        }
        
        Debug.LogWarning($"Combination with code '{code}' not found");
        return null;
    }
}
