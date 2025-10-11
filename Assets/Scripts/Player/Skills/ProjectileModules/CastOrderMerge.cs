using UnityEngine;

[CreateAssetMenu(menuName = "Projectile/Merge/CastOrder")]
public class CastOrderMerge : ProjectileModule
{
    [Header("Merge Conditions")]
    public bool mergeOnlyDifferentOwners = true;
    public bool requireSimultaneous = true;
    [Range(0f, 0.5f)]
    public float simultaneousWindow = 0.12f;

    public override void OnHit(ProjCtx c, Collider2D other)
    {
        var o = other.GetComponent<ProjCtx>();
        if (!o || o == c) return;

        if (mergeOnlyDifferentOwners)
        {
            if (c.Owner == null || o.Owner == null) return;
            if (c.Owner == o.Owner) return;
        }

        if (requireSimultaneous && Mathf.Abs(c.SpawnTime - o.SpawnTime) > simultaneousWindow)
            return;

        if (c.CastOrder < o.CastOrder) { Absorb(c, o); Object.Destroy(o.gameObject); }
        else if (c.CastOrder > o.CastOrder) { Absorb(o, c); Object.Destroy(c.gameObject); }
    }

    void Absorb(ProjCtx winner, ProjCtx absorbed)
    {
        winner.TF.localScale *= 1.1f;
    }
}
