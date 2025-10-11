using UnityEngine;

public enum InteractState { Click, Grib, Push }
public enum SkillButton { Left, Right }

public interface IHealth
{
    int Current { get; }
    int Max { get; }
    void Damage(int value);
    event System.Action<int> OnDamaged;
    event System.Action OnDied;
}

public interface IInvulnerable
{
    bool IsInvincible { get; }
    void GrantInvuln(float seconds);
}

public interface IInteractable
{
    InteractState CallState { get; }
    void Interact(GameObject actor);
}

public interface IInputSource
{
    Vector2 Move { get; }
    bool LMBHold { get; }
    bool RMBHold { get; }
    bool LMBDown { get; }
    bool RMBDown { get; }
    bool LMBUp { get; }
    bool RMBUp { get; }

    bool InteractPressed { get; }
}
