using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class CharacterService
{
    [Header("Character List")]
    [Tooltip("List of all available characters (index 0, 1, 2) - 자동으로 Assets/Datas/Character에서 로드됩니다")]
    [SerializeField] private List<CharacterData> _characters = new List<CharacterData>();
    
    private Dictionary<string, CharacterData> _characterDict = new Dictionary<string, CharacterData>();
    private bool _isInitialized = false;
    
    /// <summary>
    /// Assets/Datas/Character 폴더에서 모든 CharacterData를 자동으로 로드합니다. (에디터 전용)
    /// </summary>
    /// <param name="owner">데이터 저장을 위해 GameDataManager 인스턴스를 받습니다.</param>
    public void LoadDataFromAssets(MonoBehaviour owner)
    {
#if UNITY_EDITOR
        _characters.Clear();
        
        // Assets/Datas/Character 폴더에서 모든 CharacterData 찾기
        string[] guids = AssetDatabase.FindAssets($"t:{nameof(CharacterData)}", new[] { "Assets/Datas/Character" });
        
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            CharacterData characterData = AssetDatabase.LoadAssetAtPath<CharacterData>(assetPath);
            
            if (characterData != null)
            {
                _characters.Add(characterData);
            }
        }
        
        // 이름순으로 정렬 (0, 1, 2 순서 유지)
        _characters.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        
        // [중요] 변경 사항 저장 표시 (Dirty Flag)
        // 이 코드가 있어야 씬이나 프리팹 저장 시 리스트가 함께 저장됩니다.
        if (owner != null)
        {
            EditorUtility.SetDirty(owner);
        }
        
        Debug.Log($"[CharacterService] Loaded {_characters.Count} characters. Ready to save.");
#endif
    }
    
    /// <summary>
    /// 딕셔너리를 초기화합니다. (런타임용)
    /// </summary>
    public void InitializeDictionary()
    {
        if (_isInitialized) return;
        
        // 런타임에서는 LoadDataFromAssets를 자동 호출하지 않음 (이미 저장된 리스트 사용)
        if (_characters == null || _characters.Count == 0)
        {
            Debug.LogWarning("[CharacterService] Character list is empty! Please run 'Load All Data From Assets' in the editor and save the scene.");
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

