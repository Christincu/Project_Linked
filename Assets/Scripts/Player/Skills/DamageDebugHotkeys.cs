using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class HurtZone2D : MonoBehaviour
{
    public int damagePerTick = 1;
    public float tickInterval = 0.5f;
    public string targetTag = "Player";   // �÷��̾�Ը�

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

        var behaviour = other.GetComponent<PlayerBehaviour>();
        if (behaviour != null && !behaviour.IsInvincible)
        {
            behaviour.Damage(damagePerTick);
            _timer = tickInterval;
        }
    }

    void OnTriggerExit2D(Collider2D other) { _timer = 0.0f; }
}
