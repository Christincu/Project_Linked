using System;
using UnityEngine;

/// <summary>
/// 두 마법의 조합 결과를 정의하는 ScriptableObject
/// 예: Fire(0) + Ice(1) = Steam(10)
/// </summary>
[CreateAssetMenu(fileName = "New Magic Combination", menuName = "Game/Magic Combination Data")]
public class MagicCombinationData : ScriptableObject
{
    public string code;

    [Header("Combination Info")]
    [Tooltip("첫 번째 마법 코드")]
    public int magicCode1;
    
    [Tooltip("두 번째 마법 코드")]
    public int magicCode2;
    
    [Header("Result")]
    [Tooltip("조합 결과 마법 코드")]
    public int resultMagicCode;
    
    /// <summary>
    /// 이 조합이 주어진 두 마법 코드에 해당하는지 확인
    /// 순서는 상관없음 (magicCode1 + magicCode2 = magicCode2 + magicCode1)
    /// </summary>
    public bool Matches(int code1, int code2)
    {
        return (magicCode1 == code1 && magicCode2 == code2) || 
               (magicCode1 == code2 && magicCode2 == code1);
    }
    
    /// <summary>
    /// 이 조합이 주어진 두 마법 코드에 해당하는지 확인 (순서 중요)
    /// 첫 번째 마법이 먼저 시전되어야 함
    /// </summary>
    public bool MatchesOrdered(int firstCode, int secondCode)
    {
        return magicCode1 == firstCode && magicCode2 == secondCode;
    }
    
    /// <summary>
    /// 조합이 유효한지 확인 (두 마법 코드가 모두 -1이 아니고, 결과 마법 코드도 설정되어 있는지)
    /// </summary>
    public bool IsValid()
    {
        return magicCode1 != -1 && magicCode2 != -1 && resultMagicCode != -1;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(code))
        {
            code = Guid.NewGuid().ToString();
        }
    }
#endif
}

