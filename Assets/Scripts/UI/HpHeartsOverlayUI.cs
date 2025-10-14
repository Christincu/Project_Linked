using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HpHeartsOverlayUI : MonoBehaviour
{
    [Header("Bind")]
    [SerializeField] private HealthComponent target;

    [Header("Rows")]
    [SerializeField] private Transform emptyRow;
    [SerializeField] private Transform fullRow;  

    [Header("Prefabs & Sprites")]
    [SerializeField] private GameObject heartPrefab; 
    [SerializeField] private Sprite emptySprite;
    [SerializeField] private Sprite fullSprite;

    [Header("Options")]
    [SerializeField, Range(2, 24)] private int maxVisualClamp = 20;

    private readonly List<Image> _emptyPool = new();
    private readonly List<Image> _fullPool = new();

    private void OnEnable()
    {
        if (target) target.OnHPChanged += Refresh;
    }

    private void OnDisable()
    {
        if (target) target.OnHPChanged -= Refresh;
    }

    public void Bind(HealthComponent hp)
    {
        if (target) target.OnHPChanged -= Refresh;
        target = hp;
        if (target)
        {
            target.OnHPChanged += Refresh;

            Refresh(target.Current, target.Max);
        }
    }

    private void EnsurePoolSize(List<Image> pool, Transform parent, int count, Sprite sprite)
    {
        while (pool.Count < count)
        {
            var go = Instantiate(heartPrefab, parent);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            pool.Add(img);
        }

        for (int i = 0; i < pool.Count; i++)
        {
            bool active = i < count;
            var img = pool[i];
            if (img.gameObject.activeSelf != active)
                img.gameObject.SetActive(active);
            if (active && img.sprite != sprite)
                img.sprite = sprite;
        }
    }

    private void Refresh(int cur, int max)
    {
        if (max <= 0) max = 1;
        int visualMax = Mathf.Min(max, maxVisualClamp);
        int visualCur = Mathf.Clamp(cur, 0, visualMax);

        EnsurePoolSize(_emptyPool, emptyRow, visualMax, emptySprite);
        EnsurePoolSize(_fullPool, fullRow, visualCur, fullSprite);
    }
}
