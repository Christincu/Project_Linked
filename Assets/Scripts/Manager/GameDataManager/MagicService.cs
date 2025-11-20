using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class MagicService
{
    [Header("Magic List")]
    [Tooltip("List of all available magics")]
    [SerializeField] private List<MagicData> magics = new List<MagicData>();
    
    [Header("Magic Combinations")]
    [Tooltip("마법 조합 규칙 리스트")]
    [SerializeField] private List<MagicCombinationData> combinations = new List<MagicCombinationData>();
    
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
        foreach (var combination in combinations)
        {
            if (combination != null && combination.IsValid() && combination.MatchesOrdered(magicCode1, magicCode2))
            {
                return combination.resultMagicCode;
            }
        }
        
        // 순서를 고려한 조합이 없으면 순서 무관 조합 찾기
        foreach (var combination in combinations)
        {
            if (combination != null && combination.IsValid() && combination.Matches(magicCode1, magicCode2))
            {
                return combination.resultMagicCode;
            }
        }
        
        // 조합을 찾지 못함
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
        return combinations;
    }
}
