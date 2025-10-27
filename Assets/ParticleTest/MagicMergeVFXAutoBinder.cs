using UnityEngine;

public class MagicMergeVFXAutoBinder : MonoBehaviour
{
    private MagicMergeVFXListener _listener;

    void Awake()
    {
        _listener = FindObjectOfType<MagicMergeVFXListener>(true);
    }

    void OnEnable()
    {
        if (_listener == null) return;

        // 씬의 모든 PlayerMagicController에 바인딩
        var pmcs = FindObjectsOfType<PlayerMagicController>(true);
        foreach (var pmc in pmcs)
            _listener.Bind(pmc);
    }
}
