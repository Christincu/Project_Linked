using UnityEngine;

[CreateAssetMenu(menuName = "Status/Invuln")]
public class InvulnSO : StatusEffectSO
{
    public override void OnApply(GameObject t) { t.GetComponent<IInvulnerable>()?.GrantInvuln(duration); }
    public override void OnRemove(GameObject t) { }
}
