using UnityEngine;

[CreateAssetMenu(menuName = "Game/Character Stats")]
public class CharacterStatsSO : ScriptableObject
{
    [Header("Core")]
    [Min(1)] public int maxHP = 3;
    [Min(0.1f)] public float moveSpeed = 1.0f;

    [Header("Skill Timing")]
    [Min(0.0f)] public float holdMinSeconds = 0.0f;     // 필요시 사용
    [Min(0.0f)] public float fireRecoverSeconds = 0.3f; // 발사 후 딜레이

    [Header("On-Hit")]
    [Min(0.0f)] public float invulnOnHitSeconds = 1.0f;

    [Header("Facing")]
    public bool spriteDefaultFacingLeft = false; // 스프라이트 기본 방향
    public bool flipColliderWithScale = true;    // 좌우반전 시 콜라이더 동반 반전
}
