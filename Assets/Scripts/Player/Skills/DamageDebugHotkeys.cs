using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class HurtZone2D : MonoBehaviour
{
    public int damagePerTick = 1;
    public float tickInterval = 0.5f;
    public string targetTag = "Player";   // 플레이어에게만

    Collider2D _col; float _timer = 0.0f;

    void Awake()
    {
        _col = GetComponent<Collider2D>();
        _col.isTrigger = true; 
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!string.IsNullOrEmpty(targetTag) && other.tag != targetTag) return;

        _timer -= Time.deltaTime;
        if (_timer > 0.0f) return;

        var hp = other.GetComponent<IHealth>();
        var inv = other.GetComponent<IInvulnerable>();
        if (hp != null && (inv == null || !inv.IsInvincible))
        {
            hp.Damage(damagePerTick);
            _timer = tickInterval;
        }
    }

    void OnTriggerExit2D(Collider2D other) { _timer = 0.0f; }
}
