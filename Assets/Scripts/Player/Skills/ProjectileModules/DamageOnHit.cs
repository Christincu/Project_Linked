using UnityEngine;

public enum DamageTargetRule { AffectAll, OnlyTags, ExcludeTags }

[CreateAssetMenu(menuName = "Projectile/Hit/Damage")]
public class DamageOnHit : ProjectileModule
{
    [Header("Damage")]
    public int damage = 1;
    public bool destroyOnHit = true;

    [Header("Self / Owner")]
    public bool ignoreOwner = true;
    public bool ignoreOwnerChildren = true;

    [Header("Target Filter by Tag")]
    public DamageTargetRule targetRule = DamageTargetRule.AffectAll;
    public string[] targetTags;

    public override void OnHit(ProjCtx c, Collider2D other)
    {
        if (ignoreOwner && c.Owner)
        {
            if (other.gameObject == c.Owner) return;
            if (ignoreOwnerChildren && other.transform.IsChildOf(c.Owner.transform)) return;
        }

        if (targetRule != DamageTargetRule.AffectAll)
        {
            string tag = other.tag;
            bool inList = IsInList(tag, targetTags);
            if (targetRule == DamageTargetRule.OnlyTags && !inList) return;
            if (targetRule == DamageTargetRule.ExcludeTags && inList) return;
        }

        var behaviour = other.GetComponent<PlayerBehaviour>();
        if (behaviour != null)
        {
            if (!behaviour.IsInvincible)
            {
                behaviour.Damage(damage);
                if (destroyOnHit) Object.Destroy(c.gameObject);
            }
        }
    }

    bool IsInList(string tag, string[] list)
    {
        if (list == null || list.Length == 0) return false;
        for (int i = 0; i < list.Length; i++)
            if (!string.IsNullOrEmpty(list[i]) && list[i] == tag) return true;
        return false;
    }
}
