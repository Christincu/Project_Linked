using UnityEngine;
using System.Collections.Generic;

public class MagicMergeVFXAutoBinder : MonoBehaviour
{
    [SerializeField] private MagicMergeVFXListener listener;

    private readonly HashSet<PlayerMagicController> _bound = new();

    void Awake()
    {
        // �ν����Ϳ��� ���� �� ������ ������ �ڵ� ã��(��Ȱ�� ����)
        if (!listener)
            listener = FindObjectOfType<MagicMergeVFXListener>(true);
    }

    void OnEnable()
    {
        RebindAll();
    }

    public void RebindAll()
    {
        if (!listener) return;

        var pmcs = FindObjectsOfType<PlayerMagicController>(true);
        foreach (var pmc in pmcs)
        {
            if (pmc == null || _bound.Contains(pmc)) continue;
            listener.Bind(pmc);
            _bound.Add(pmc);
        }
    }
}
