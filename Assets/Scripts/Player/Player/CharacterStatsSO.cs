using UnityEngine;

[CreateAssetMenu(menuName = "Game/Character Stats")]
public class CharacterStatsSO : ScriptableObject
{
    [Header("Core")]
    [Min(1)] public int maxHP = 3;
    [Min(0.1f)] public float moveSpeed = 1.0f;

    [Header("Skill Timing")]
    [Min(0.0f)] public float holdMinSeconds = 0.0f;    
    [Min(0.0f)] public float fireRecoverSeconds = 0.3f; 

    [Header("On-Hit")]
    [Min(0.0f)] public float invulnOnHitSeconds = 1.0f;

    [Header("Facing")]
    public bool spriteDefaultFacingLeft = false; 
    public bool flipColliderWithScale = true;    
}
