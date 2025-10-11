using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "Skill/BasicSkill")]
public class SkillSO : ScriptableObject
{
    [Header("Projectile")]
    public GameObject projectilePrefab;
    public ProjectileModule[] modules;

    [Header("Projectile Identity")]
    public string projectileTag;          // 비워두면 미설정
    public string projectileLayerName;    // 비워두면 미설정

    static int _castSeq = 0;

    public void OnHoldBegin(GameObject caster, SkillButton btn) { }

    public void OnFire(GameObject caster)
    {
        if (!projectilePrefab) return;

        var spawn = SpawnAt(caster);
        var go = Instantiate(projectilePrefab, spawn, Quaternion.identity);

        if (!string.IsNullOrEmpty(projectileTag))
        {
            try { go.tag = projectileTag; }
            catch (UnityException)
            {
                Debug.LogWarning($"[SkillSO] Tag '{projectileTag}' not defined. Add it in Project Settings or clear the field.", this);
            }
        }
        if (!string.IsNullOrEmpty(projectileLayerName))
        {
            int layer = LayerMask.NameToLayer(projectileLayerName);
            if (layer >= 0) go.layer = layer;
            else Debug.LogWarning($"[SkillSO] Layer '{projectileLayerName}' not defined. Create it or clear the field.", this);
        }

        var ctx = go.GetComponent<ProjCtx>();
        ctx.CastOrder = _castSeq++;
        ctx.MouseWorld = Camera.main ? Camera.main.ScreenToWorldPoint(Input.mousePosition) : spawn; // ← 대소문자 주의
        ctx.InitOwner(caster); // 자기충돌 무시

        if (modules != null && modules.Length > 0) ctx.modules = modules.ToList();
        foreach (var m in ctx.modules) m.OnSpawn(ctx);
    }

    Vector2 SpawnAt(GameObject caster)
    {
        var col = caster.GetComponent<Collider2D>();
        var b = col.bounds;
        return new Vector2(b.max.x, b.max.y);
    }
}
