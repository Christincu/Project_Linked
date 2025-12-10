using System;
using UnityEngine;

[CreateAssetMenu(fileName = "New Magic", menuName = "Game/Magic Data")]
public class MagicData : ScriptableObject
{
    public string code;

    [Header("Basic Info")]
    public int magicCode;
    public string magicName;
    public Sprite magicCombinedSprite;
    public Sprite magicIdleSprite;
    public Sprite magicInsideSprite;
    
    [Header("Magic Settings")]
    [Tooltip("마법 쿨다운 시간 (초)")]
    public float cooldown = 1f;
    
    [Tooltip("마법 데미지")]
    public float damage = 15f;
    
    [Tooltip("마법 발사 속도")]
    public float speed = 10f;
    
    [Tooltip("마법 사거리")]
    public float range = 20f;
    
    [Header("Mana Settings")]
    [Tooltip("시전 시 마나 소모량")]
    public float manaCost = 20f;
    
    [Header("Prefabs")]
    [Tooltip("마법 발사체 프리팹 (일반 마법용)")]
    public GameObject magicProjectilePrefab;

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
