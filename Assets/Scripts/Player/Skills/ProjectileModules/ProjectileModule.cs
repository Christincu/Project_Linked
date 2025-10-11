using UnityEngine;

public abstract class ProjectileModule : ScriptableObject
{
    public virtual void OnSpawn(ProjCtx c) { }
    public virtual void OnTick(ProjCtx c) { }
    public virtual void OnHit(ProjCtx c, Collider2D other) { }
}
