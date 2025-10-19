using UnityEngine;

public class Hazard : MonoBehaviour
{
    public int Damage = 1;
    public bool DestroyAfterHit = true;

    void OnTriggerEnter2D(Collider2D other)
    {
        var behaviour = other.GetComponent<PlayerBehaviour>();
        if (!behaviour) return;
        if (behaviour.IsInvincible) return;

        behaviour.Damage(Damage);
        if (DestroyAfterHit) Destroy(gameObject);
    }
}
