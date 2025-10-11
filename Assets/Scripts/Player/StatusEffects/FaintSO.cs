using UnityEngine;

[CreateAssetMenu(menuName = "Status/Faint")]
public class FaintSO : StatusEffectSO
{
    public override void OnApply(GameObject t) { t.GetComponent<PlayerCtx>()?.Goto("Faint"); }
    public override void OnRemove(GameObject t) { }
}
