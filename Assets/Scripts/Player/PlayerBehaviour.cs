using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerBehaviour : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHP = 3;
    public int CurrentHP { get; private set; }
    public int MaxHP => maxHP;

    [Header("Invulnerability")]
    [SerializeField] private float invulnDuration = 1.0f;
    public bool IsInvincible { get; private set; }

    [Header("Status Effects")]
    [SerializeField] private InvulnSO invulnOnHit;

    // Events
    public event System.Action<int> OnDamaged;
    public event System.Action OnDied;
    public event System.Action<int, int> OnHPChanged;

    private PlayerController _controller;
    private SpriteRenderer _spriteRenderer;
    private readonly List<(StatusEffectSO effect, float endTime)> _statusEffects = new();

    void Awake()
    {
        _controller = GetComponent<PlayerController>();
        _spriteRenderer = _controller ? _controller.spriteRenderer : GetComponent<SpriteRenderer>();
        CurrentHP = maxHP;
    }

    void Start()
    {
        OnHPChanged?.Invoke(CurrentHP, maxHP);
    }

    void Update()
    {
        UpdateStatusEffects();
    }

    // ===== HEALTH =====
    public void SetMaxHP(int newMax, bool healToFull = true)
    {
        maxHP = Mathf.Max(1, newMax);
        if (healToFull)
            CurrentHP = maxHP;
        else
            CurrentHP = Mathf.Min(CurrentHP, maxHP);

        OnHPChanged?.Invoke(CurrentHP, maxHP);
    }

    public void Damage(int value)
    {
        if (value <= 0) return;
        if (IsInvincible) return;

        CurrentHP = Mathf.Max(0, CurrentHP - value);
        OnDamaged?.Invoke(value);
        OnHPChanged?.Invoke(CurrentHP, maxHP);
        GameEvents.PlayerDamaged?.Invoke(value);

        if (invulnOnHit)
            ApplyStatusEffect(invulnOnHit);

        if (CurrentHP <= 0)
        {
            OnDied?.Invoke();
            GameEvents.PlayerDied?.Invoke();
            if (_controller) _controller.GotoState("Faint");
        }
    }

    public void Heal(int value)
    {
        if (value <= 0) return;
        CurrentHP = Mathf.Min(maxHP, CurrentHP + value);
        OnHPChanged?.Invoke(CurrentHP, maxHP);
    }

    // ===== INVULNERABILITY =====
    public void GrantInvuln(float seconds)
    {
        if (gameObject.activeInHierarchy)
            StartCoroutine(InvulnCoroutine(seconds));
    }

    public void SetInvulnDuration(float seconds)
    {
        invulnDuration = seconds;
        if (invulnOnHit)
            invulnOnHit.duration = seconds;
    }

    IEnumerator InvulnCoroutine(float seconds)
    {
        IsInvincible = true;
        float t = 0f;
        bool visible = true;

        while (t < seconds)
        {
            t += 0.1f;
            if (_spriteRenderer)
            {
                visible = !visible;
                _spriteRenderer.enabled = visible;
            }
            yield return new WaitForSeconds(0.1f);
        }

        if (_spriteRenderer) _spriteRenderer.enabled = true;
        IsInvincible = false;
    }

    // ===== STATUS EFFECTS =====
    public void ApplyStatusEffect(StatusEffectSO effect)
    {
        if (!effect) return;
        effect.OnApply(gameObject);
        _statusEffects.Add((effect, Time.time + effect.duration));
    }

    void UpdateStatusEffects()
    {
        for (int i = _statusEffects.Count - 1; i >= 0; --i)
        {
            if (Time.time >= _statusEffects[i].endTime)
            {
                _statusEffects[i].effect.OnRemove(gameObject);
                _statusEffects.RemoveAt(i);
            }
        }
    }
}

