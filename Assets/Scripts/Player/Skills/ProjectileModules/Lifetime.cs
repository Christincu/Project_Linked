using UnityEngine;

[CreateAssetMenu(menuName = "Projectile/Lifetime")]
public class Lifetime : ProjectileModule
{
    public float seconds = 3.0f;
    float _dieAt;
    public override void OnSpawn(ProjCtx c) { _dieAt = Time.time + seconds; }
    public override void OnTick(ProjCtx c) { if (Time.time >= _dieAt) Object.Destroy(c.gameObject); }
}
