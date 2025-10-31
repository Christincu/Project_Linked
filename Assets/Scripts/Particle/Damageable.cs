using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class Damageable : MonoBehaviour
{
    [Header("HP Settings")]
    public int maxHP = 50;
    private int _hp;

    [Header("Hit Flash")]
    public Color flashColor = Color.white;
    public float flashDuration = 0.12f; 
    public int flashRepeat = 2;        
    [Range(0f, 1f)] public float alphaDip = 0.75f; 

    private SpriteRenderer[] _renderers;
    private Color[] _origColors;

    void Awake()
    {
        _hp = maxHP;

        _renderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: false);
        _origColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
            _origColors[i] = _renderers[i].color;
    }

    public void ApplyDamage(int dmg)
    {
        _hp -= dmg;
        if (_hp < 0) _hp = 0;

        if (_renderers.Length > 0)
        {
            StopAllCoroutines();
            StartCoroutine(FlashEffect());
        }

        if (_hp <= 0) Die();
    }

    private IEnumerator FlashEffect()
    {
        Color target = flashColor;
        if (NearlyWhite(target))
            target = new Color(1f, 0.6f, 0.6f, 1f);

        for (int r = 0; r < Mathf.Max(1, flashRepeat); r++)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                var c = target;
                c.a = _origColors[i].a * alphaDip;
                _renderers[i].color = c;
            }
            yield return new WaitForSeconds(flashDuration);

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                _renderers[i].color = _origColors[i];
            }
            yield return new WaitForSeconds(flashDuration * 0.5f);
        }
    }

    private static bool NearlyWhite(Color c)
    {
        return (c.r > 0.95f && c.g > 0.95f && c.b > 0.95f);
    }

    private void Die()
    {
        Destroy(gameObject);
    }
}
