using UnityEngine;

[CreateAssetMenu(fileName = "New Magic", menuName = "Game/Magic Data")]
public class MagicData : ScriptableObject
{
    [Header("Basic Info")]
    public int magicName;
    public Sprite magicIdleSprite;
    public Sprite magicActiveSprite;
    public int castOrder;
    
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
}
