using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyHealthFlash : MonoBehaviour
{
    [Header("Health")]
    public float maxHP = 100.0f;
    public float hp = 100.0f;

    [Header("Flash")]
    [Tooltip("한 번 번쩍 전체 길이")]
    public float flashDuration = 0.12f;
    [Tooltip("연속 번쩍 횟수")]
    public int flashCount = 1;
    [Tooltip("HDR 강도")]
    public float flashIntensity = 2.0f;   
    [Tooltip("스프라이트가 이미 흰색일 때도 확실히 드러나도록 처리")]
    public bool forceVisible = true;
    public bool includeInactiveChildren = true;

    private readonly List<SpriteRenderer> _sprites = new();
    private readonly List<Color> _origColors = new();
    private readonly List<MaterialPropertyBlock> _mpbs = new();

    private Coroutine _flashCo;

    void Awake()
    {
        var sprites = GetComponentsInChildren<SpriteRenderer>(includeInactiveChildren);
        foreach (var sr in sprites)
        {
            if (!sr) continue;
            _sprites.Add(sr);
            _origColors.Add(sr.color);
            _mpbs.Add(new MaterialPropertyBlock());
        }
        if (hp <= 0f) hp = maxHP;

        Debug.Log($"[EnemyHealthFlash] sprites found: {_sprites.Count}");
    }

    public void TakeDamage(float dmg)
    {
        if (dmg <= 0f || hp <= 0.0f) return;

        hp = Mathf.Max(0f, hp - dmg);

        if (_flashCo != null) StopCoroutine(_flashCo);
        _flashCo = StartCoroutine(FlashWhite());

        if (hp <= 0.0f)
        {
            Destroy(gameObject);
        }
    }

    private IEnumerator FlashWhite()
    {
        for (int i = 0; i < flashCount; i++)
        {
            // Flash ON
            for (int s = 0; s < _sprites.Count; s++)
            {
                var sr = _sprites[s];
                if (!sr) continue;

                var mpb = _mpbs[s];
                sr.GetPropertyBlock(mpb);

                var hdrWhite = Color.white * flashIntensity; hdrWhite.a = 1f;
                bool applied = false;

                if (sr.sharedMaterial && sr.sharedMaterial.HasProperty("_BaseColor"))
                {
                    mpb.SetColor("_BaseColor", hdrWhite);
                    applied = true;
                }
                if (sr.sharedMaterial && sr.sharedMaterial.HasProperty("_Color"))
                {
                    mpb.SetColor("_Color", hdrWhite);
                    applied = true;
                }

                if (applied)
                {
                    sr.SetPropertyBlock(mpb);
                }
                else
                {
                    if (forceVisible)
                    {
                        sr.color = new Color(1.0f, 1.0f, 1.0f, 0.3f);
                    }
                    else
                    {
                        sr.color = Color.white;
                    }
                }
            }
            yield return new WaitForSeconds(flashDuration * 0.5f);

            for (int s = 0; s < _sprites.Count; s++)
            {
                var sr = _sprites[s];
                if (!sr) continue;

                var mpb = _mpbs[s];
                sr.GetPropertyBlock(mpb);

                bool restored = false;
                if (sr.sharedMaterial && sr.sharedMaterial.HasProperty("_BaseColor"))
                {
                    mpb.SetColor("_BaseColor", _origColors[s]);
                    restored = true;
                }
                if (sr.sharedMaterial && sr.sharedMaterial.HasProperty("_Color"))
                {
                    mpb.SetColor("_Color", _origColors[s]);
                    restored = true;
                }

                if (restored)
                {
                    sr.SetPropertyBlock(mpb);
                }
                else
                {
                    sr.color = _origColors[s];
                }
            }
            yield return new WaitForSeconds(flashDuration * 0.5f);
        }
    }
}
