using UnityEngine;

public class Hazard : MonoBehaviour
{
    public int Damage = 1;
    public bool DestroyAfterHit = true; // Fireball: true, Spike: false

    void OnTriggerEnter2D(Collider2D other)
    {
        var hp = other.GetComponent<IHealth>(); if (hp == null) return;
        var inv = other.GetComponent<IInvulnerable>(); if (inv != null && inv.IsInvincible) return;

        hp.Damage(Damage);
        if (DestroyAfterHit) Destroy(gameObject);
    }
}
