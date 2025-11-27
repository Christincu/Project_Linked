using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class CharacterService
{
    [Header("Character List")]
    [Tooltip("List of all available characters (index 0, 1, 2) - 자동으로 Assets/Datas/Character에서 로드됩니다")]
    [SerializeField] private List<CharacterData> _characters = new List<CharacterData>();
    
    private Dictionary<string, CharacterData> _characterDict = new Dictionary<string, CharacterData>();
    private bool _isInitialized = false;
    private bool _isDataLoaded = false;
    
    /// <summary>
    /// Assets/Datas/Character 폴더에서 모든 CharacterData를 자동으로 로드합니다.
    /// 에디터에서만 작동하며, 런타임에서는 Inspector에 할당된 리스트를 사용합니다.
    /// </summary>
    public void LoadDataFromAssets()
    {
        if (_isDataLoaded) return;
        
#if UNITY_EDITOR
        _characters.Clear();
        
        // Assets/Datas/Character 폴더에서 모든 CharacterData 찾기
        string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(CharacterData)}", new[] { "Assets/Datas/Character" });
        
        foreach (string guid in guids)
        {
            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            CharacterData characterData = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterData>(assetPath);
            
            if (characterData != null)
            {
                _characters.Add(characterData);
            }
        }
        
        // 이름순으로 정렬 (0, 1, 2 순서 유지)
        _characters.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        
        Debug.Log($"[CharacterService] {_characters.Count}개의 캐릭터 데이터를 자동으로 로드했습니다.");
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
        
        _characterDict.Clear();
        foreach (var character in _characters)
        {
            if (character != null && !string.IsNullOrEmpty(character.code))
            {
                if (_characterDict.ContainsKey(character.code))
                {
                    Debug.LogWarning($"Duplicate character code found: {character.code}. Skipping duplicate.");
                    continue;
                }
                _characterDict[character.code] = character;
            }
        }
        _isInitialized = true;
    }
    
    /// <summary>
    /// Get character data by index
    /// </summary>
    /// <param name="index">Character index (0, 1, 2)</param>
    /// <returns>CharacterData or null if invalid index</returns>
    public CharacterData GetCharacter(int index)
    {
        if (index >= 0 && index < _characters.Count)
        {
            return _characters[index];
        }
        
        Debug.LogWarning($"Invalid character index: {index}");
        return null;
    }
    
    /// <summary>
    /// Get character data by code
    /// </summary>
    /// <param name="code">Character code (GUID string)</param>
    /// <returns>CharacterData or null if not found</returns>
    public CharacterData GetCharacterByCode(string code)
    {
        if (!_isInitialized)
        {
            InitializeDictionary();
        }
        
        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning("Character code is null or empty");
            return null;
        }
        
        if (_characterDict.TryGetValue(code, out CharacterData character))
        {
            return character;
        }
        
        Debug.LogWarning($"Character with code '{code}' not found");
        return null;
    }
    
    /// <summary>
    /// Get total number of characters
    /// </summary>
    public int CharacterCount => _characters.Count;
    
    /// <summary>
    /// Get all characters
    /// </summary>
    public List<CharacterData> GetAllCharacters()
    {
        return _characters;
    }
}

