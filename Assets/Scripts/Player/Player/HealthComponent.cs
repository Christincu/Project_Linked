using UnityEngine;

[DisallowMultipleComponent]
public class HealthComponent : MonoBehaviour, IHealth
{
    [SerializeField] private int _max = 3;
    public int Max => _max;
    public int Current { get; private set; }

    public event System.Action<int> OnDamaged;
    public event System.Action OnDied;

    public event System.Action<int, int> OnHPChanged;

    private PlayerCtx _pc;
    private StatusRunner _status;

    [Header("OnHit Effects")]
    public InvulnSO InvulnOnHit;

    void Awake()
    {
        Current = Max;
        _pc = GetComponent<PlayerCtx>();
        _status = GetComponent<StatusRunner>();

        OnHPChanged?.Invoke(Current, Max);
    }

    public void SetMaxHP(int newMax, bool healToFull = true)
    {
        _max = Mathf.Max(1, newMax);
        if (healToFull)
            Current = _max;
        else
            Current = Mathf.Min(Current, _max);

        OnHPChanged?.Invoke(Current, Max); 
    }

    public void Damage(int value)
    {
        if (value <= 0) return;

        var inv = GetComponent<IInvulnerable>();
        if (inv != null && inv.IsInvincible) return;

        Current = Mathf.Max(0, Current - value);
        OnDamaged?.Invoke(value);
        OnHPChanged?.Invoke(Current, Max);
        GameEvents.PlayerDamaged?.Invoke(value);

        if (_status && InvulnOnHit)
            _status.Apply(InvulnOnHit);

        if (Current <= 0)
        {
            OnDied?.Invoke();
            GameEvents.PlayerDied?.Invoke();
            if (_pc) _pc.Goto("Faint");
        }
    }

    public void Heal(int value)
    {
        if (value <= 0) return;
        Current = Mathf.Min(Max, Current + value);
        OnHPChanged?.Invoke(Current, Max); 
    }
}
