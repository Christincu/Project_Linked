using UnityEngine;

public abstract class PlayerState
{
    protected PlayerCtx Ctx;
    public void Bind(PlayerCtx ctx) => Ctx = ctx;
    public virtual void Enter() { }
    public virtual void Exit() { }
    public virtual void Tick() { }
    public virtual void FixedTick() { }
}

public sealed class IdleState : PlayerState
{
    public override void Enter() { Ctx.PlayAnim("Idle"); }
    public override void Tick()
    {
        var m = Ctx.Input.Move;
        if (m.sqrMagnitude > 0.0f) { Ctx.Goto("Move"); return; }

        // ��Ŭ��: ��� ����
        if (Ctx.Input.LMBDown)
        {
            if (Ctx.SkillAsset != null)
            {
                Ctx.Goto("Hold");
                return;
            }
        }

        // �̹� ��� �ִ� ���¿��� ����Ŭ��: ������
        if (Ctx.IsHolding && Ctx.Input.RMBDown)
        {
            Ctx.Goto("Fire");
            return;
        }

        if (Ctx.Input.InteractPressed) TryInteract();
    }

    void TryInteract()
    {
        var hit = Physics2D.OverlapCircle(Ctx.transform.position, 0.6f, LayerMask.GetMask("Interactable"));
        if (!hit) return;
        var it = hit.GetComponent<IInteractable>();
        it?.Interact(Ctx.gameObject);
        if (it != null)
        {
            switch (it.CallState)
            {
                case InteractState.Click: Ctx.PlayAnim("Click"); break;
                case InteractState.Grib: Ctx.PlayAnim("Grib"); break;
                case InteractState.Push: Ctx.PlayAnim("Push"); break;
            }
            Ctx.RunAfter(0.3f, () => Ctx.Goto("Idle"));
        }
    }
}

public sealed class MoveState : PlayerState
{
    public override void Tick()
    {
        var m = Ctx.Input.Move;
        if (m == Vector2.zero) { Ctx.Goto("Idle"); return; }

        if (Mathf.Abs(m.x) >= Mathf.Abs(m.y))
        {
            if (m.x >= 0.0f) Ctx.PlayAnim("Right");
            else Ctx.PlayAnim("Left");
            Ctx.SetFacingByX(m.x);
        }
        else
        {
            if (m.y >= 0.0f) Ctx.PlayAnim("Up");
            else Ctx.PlayAnim("Down");
            if (Mathf.Abs(m.x) > 0.01f) Ctx.SetFacingByX(m.x);
        }

        // �̵� �߿��� ��� ���/������ ����
        if (Ctx.Input.LMBDown && Ctx.SkillAsset != null) { Ctx.Goto("Hold"); return; }
        if (Ctx.IsHolding && Ctx.Input.RMBDown) { Ctx.Goto("Fire"); return; }
    }

    public override void FixedTick()
    {
        var m = Ctx.Input.Move;
        var delta = m * Ctx.MoveSpeed * Time.fixedDeltaTime;
        Ctx.RB.MovePosition(Ctx.RB.position + delta);
    }
}

public sealed class HoldState : PlayerState
{
    public override void Enter()
    {
        // �̹� ��� ������ ����, �ƴϸ� ���� ��� ����
        if (!Ctx.IsHolding)
        {
            if (Ctx.SkillAsset == null) { Ctx.Goto("Idle"); return; }
            Ctx.BeginHold(); // ��Ŭ�� ����: "���� ��� �ֱ�" ����
        }

        Ctx.PlayAnim("Hold");
    }

    public override void Tick()
    {
        // ��� �ִ� ���� �¿� �Է� �ݿ�
        var m = Ctx.Input.Move;
        if (Mathf.Abs(m.x) > 0.01f) Ctx.SetFacingByX(m.x);

        // ����Ŭ�� ������ ������
        if (Ctx.Input.RMBDown)
        {
            Ctx.Goto("Fire");
            return;
        }
    }

    public override void FixedTick()
    {
        // ��� �־ �̵� ����
        var m = Ctx.Input.Move;
        if (m != Vector2.zero)
        {
            var delta = m * (Ctx.MoveSpeed * Ctx.HoldMoveSpeedMultiplier) * Time.fixedDeltaTime;
            Ctx.RB.MovePosition(Ctx.RB.position + delta);
        }
    }
}

public sealed class FireState : PlayerState
{
    public override void Enter()
    {
        Ctx.PlayAnim("Fire");
        Ctx.SkillAsset?.OnFire(Ctx.gameObject); // �߻� ó��
        GameEvents.SkillFired?.Invoke();

        // ��� �ִ� �� ����
        Ctx.EndHold();

        Ctx.RunAfter(0.3f, () => Ctx.Goto("Idle"));
    }
}

public sealed class FaintState : PlayerState
{
    public override void Enter()
    {
        Ctx.RB.velocity = Vector2.zero;
        Ctx.PlayAnim("Faint");
        Ctx.EndHold(); // ������ �� ��� �ִ� �͵� ����
    }
}
