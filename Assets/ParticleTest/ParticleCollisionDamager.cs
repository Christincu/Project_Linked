using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class ParticleCollisionDamager : MonoBehaviour
{
    [Header("Damage")]
    public float damagePerHit = 10f;
    public float perTargetCooldown = 0.2f;          // ���� ��� ���� Ÿ�� ����
    public LayerMask targetLayers;                  // ���� ��� ���̾�(Enemy ��)

    // (����) ��ƼŬ�� ���� ���� ��ġ�� �ִٸ� ���� (��: MagicAnchor)
    public Transform follow;

    // ����
    private readonly Dictionary<EnemyHealthFlash, float> _nextHitTime = new();
    private ParticleSystem _ps;

    void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
    }

    void LateUpdate()
    {
        // �ð��� World Space �����̶� ��ġ�� ����ȭ(���� ��� ����)
        if (follow) transform.position = follow.position;
    }

    // Send Collision Messages �� ���� ���� �� ȣ���
    void OnParticleCollision(GameObject other)
    {
        // ���̾� ����
        if (((1 << other.layer) & targetLayers) == 0) return;

        // ������ ������Ʈ ã��(��Ʈ/�θ� ��� �پ��� ������� �ö󰡸� Ž��)
        var target = other.GetComponentInParent<EnemyHealthFlash>();
        if (target == null) return;

        // ��ٿ� Ȯ��
        float now = Time.time;
        if (_nextHitTime.TryGetValue(target, out var t) && now < t) return;
        _nextHitTime[target] = now + perTargetCooldown;

        // ���� ����
        target.TakeDamage(damagePerHit);
    }
}
