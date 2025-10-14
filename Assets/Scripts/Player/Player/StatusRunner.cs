using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class StatusRunner : MonoBehaviour
{
    readonly List<(StatusEffectSO, float)> _list = new();

    public void Apply(StatusEffectSO so)
    {
        if (!so) return;
        so.OnApply(gameObject);
        _list.Add((so, Time.time + so.duration));
    }

    void Update()
    {
        for (int i = _list.Count - 1; i >= 0; --i)
        {
            if (Time.time >= _list[i].Item2)
            {
                _list[i].Item1.OnRemove(gameObject);
                _list.RemoveAt(i);
            }
        }
    }
}
