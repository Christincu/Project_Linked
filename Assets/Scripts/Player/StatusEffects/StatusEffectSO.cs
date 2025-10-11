using UnityEngine;

public abstract class StatusEffectSO : ScriptableObject
{
    [Min(0.1f)] public float duration = 1.0f;
    public abstract void OnApply(GameObject target);
    public abstract void OnRemove(GameObject target);
}
