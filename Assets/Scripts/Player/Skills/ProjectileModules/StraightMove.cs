using UnityEngine;

[CreateAssetMenu(menuName = "Projectile/Move/Straight")]
public class StraightMove : ProjectileModule
{
    public float speed = 6.0f;
    public override void OnTick(ProjCtx c)
    {
        var dir = (c.MouseWorld - c.TF.position); dir.z = 0;
        c.RB.velocity = dir.normalized * speed;
        c.gameObject.layer = LayerMask.NameToLayer("Magic");
    }
}
