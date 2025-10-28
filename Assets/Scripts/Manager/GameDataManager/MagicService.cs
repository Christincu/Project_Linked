using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class MagicService
{
    [Header("Magic List")]
    [Tooltip("List of all available magics")]
    [SerializeField] private List<MagicData> magics = new List<MagicData>();
    
    /// <summary>
    /// Get magic data by index
    /// </summary>
    /// <param name="index">Magic index</param>
    /// <returns>MagicData or null if invalid index</returns>
    public MagicData GetMagic(int index)
    {
        if (index >= 0 && index < magics.Count)
        {
            return magics[index];
        }
        
        Debug.LogWarning($"Invalid magic index: {index}");
        return null;
    }
    
    /// <summary>
    /// Get magic data by magic name
    /// </summary>
    /// <param name="magicName">Magic name identifier</param>
    /// <returns>MagicData or null if not found</returns>
    public MagicData GetMagicByName(int magicName)
    {
        foreach (var magic in magics)
        {
            if (magic != null && magic.magicCode == magicName)
            {
                return magic;
            }
        }
        
        Debug.LogWarning($"Magic with name {magicName} not found");
        return null;
    }
    
    /// <summary>
    /// Get total number of magics
    /// </summary>
    public int MagicCount => magics.Count;
    
    /// <summary>
    /// Get all magics
    /// </summary>
    public List<MagicData> GetAllMagics()
    {
        return magics;
    }
}
