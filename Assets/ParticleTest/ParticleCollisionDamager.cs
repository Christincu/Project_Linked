using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class ParticleCollisionDamager : MonoBehaviour
{
    [Header("Damage")]
    public float damagePerHit = 10f;
    public float perTargetCooldown = 0.2f;          // 같은 대상에 연속 타격 간격
    public LayerMask targetLayers;                  // 맞을 대상 레이어(Enemy 등)

    // (선택) 파티클이 따라갈 기준 위치가 있다면 지정 (예: MagicAnchor)
    public Transform follow;

    // 내부
    private readonly Dictionary<EnemyHealthFlash, float> _nextHitTime = new();
    private ParticleSystem _ps;

    void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
    }

    void LateUpdate()
    {
        // 시각은 World Space 권장이라 위치만 동기화(원본 모양 유지)
        if (follow) transform.position = follow.position;
    }

    // Send Collision Messages 가 켜져 있을 때 호출됨
    void OnParticleCollision(GameObject other)
    {
        // 레이어 필터
        if (((1 << other.layer) & targetLayers) == 0) return;

        // 적에서 컴포넌트 찾기(루트/부모 어디에 붙었든 상관없이 올라가며 탐색)
        var target = other.GetComponentInParent<EnemyHealthFlash>();
        if (target == null) return;

        // 쿨다운 확인
        float now = Time.time;
        if (_nextHitTime.TryGetValue(target, out var t) && now < t) return;
        _nextHitTime[target] = now + perTargetCooldown;

        // 피해 적용
        target.TakeDamage(damagePerHit);
    }
}
