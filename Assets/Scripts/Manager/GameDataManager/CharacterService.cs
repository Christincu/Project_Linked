using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class CharacterService
{
    [Header("Character List")]
    [Tooltip("List of all available characters (index 0, 1, 2)")]
    [SerializeField] private List<CharacterData> characters = new List<CharacterData>();
    
    /// <summary>
    /// Get character data by index
    /// </summary>
    /// <param name="index">Character index (0, 1, 2)</param>
    /// <returns>CharacterData or null if invalid index</returns>
    public CharacterData GetCharacter(int index)
    {
        if (index >= 0 && index < characters.Count)
        {
            return characters[index];
        }
        
        Debug.LogWarning($"Invalid character index: {index}");
        return null;
    }
    
    /// <summary>
    /// Get total number of characters
    /// </summary>
    public int CharacterCount => characters.Count;
    
    /// <summary>
    /// Get all characters
    /// </summary>
    public List<CharacterData> GetAllCharacters()
    {
        return characters;
    }
}

