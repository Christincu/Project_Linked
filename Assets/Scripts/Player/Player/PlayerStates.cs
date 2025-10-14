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

// Idle
public sealed class IdleState : PlayerState
{
    public override void Enter() => Ctx.PlayAnim("1P_Idle");

    public override void Tick()
    {
        var m = Ctx.Input.Move;
        if (m.sqrMagnitude > 0f) { Ctx.Goto("Move"); return; }

        // 좌우 버튼 누르면 Hold 시작
        if (Ctx.Input.LMBDown) { Ctx.OnButtonDown(SkillButton.Left); Ctx.Goto("Hold"); return; }
        if (Ctx.Input.RMBDown) { Ctx.OnButtonDown(SkillButton.Right); Ctx.Goto("Hold"); return; }

        // 인터랙트
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

// Move
public sealed class MoveState : PlayerState
{
    public override void Tick()
    {
        var m = Ctx.Input.Move;
        if (m == Vector2.zero) { Ctx.Goto("Idle"); return; }

        // 애니메이션 전환
        if (Mathf.Abs(m.x) >= Mathf.Abs(m.y))
        {
            Ctx.PlayAnim("1P_leftrightMove");
            Ctx.SetFacingByX(m.x);
        }
        else
        {
            if (m.y >= 0f) Ctx.PlayAnim("1P_upMove");
            else Ctx.PlayAnim("1P_downMove");
            if (Mathf.Abs(m.x) > 0.01f) Ctx.SetFacingByX(m.x);
        }

        // 입력
        if (Ctx.Input.LMBDown) { Ctx.OnButtonDown(SkillButton.Left); Ctx.Goto("Hold"); return; }
        if (Ctx.Input.RMBDown) { Ctx.OnButtonDown(SkillButton.Right); Ctx.Goto("Hold"); return; }
    }

    public override void FixedTick()
    {
        var m = Ctx.Input.Move;
        var delta = m * Ctx.MoveSpeed * Time.fixedDeltaTime;
        Ctx.RB.MovePosition(Ctx.RB.position + delta);
    }
}

// Hold
public sealed class HoldState : PlayerState
{
    public override void Enter() => Ctx.PlayAnim("1P_Idle"); // 기본 Idle pose로 시작

    public override void Tick()
    {
        var m = Ctx.Input.Move;

        if (m == Vector2.zero)
            Ctx.PlayAnim("1P_Idle");
        else if (Mathf.Abs(m.x) >= Mathf.Abs(m.y))
        {
            Ctx.PlayAnim("1P_leftrightMove");
            Ctx.SetFacingByX(m.x);
        }
        else
        {
            if (m.y >= 0f) Ctx.PlayAnim("1P_upMove");
            else Ctx.PlayAnim("1P_downMove");
            if (Mathf.Abs(m.x) > 0.01f) Ctx.SetFacingByX(m.x);
        }

        // 합체 / 발사 규칙
        if (Ctx.Input.LMBDown) Ctx.OnButtonDown(SkillButton.Left);
        if (Ctx.Input.RMBDown) Ctx.OnButtonDown(SkillButton.Right);

        if (Ctx.Input.LMBUp) { Ctx.OnButtonUp(SkillButton.Left); return; }
        if (Ctx.Input.RMBUp) { Ctx.OnButtonUp(SkillButton.Right); return; }
    }

    public override void FixedTick()
    {
        var m = Ctx.Input.Move;
        if (m != Vector2.zero)
        {
            var delta = m * (Ctx.MoveSpeed * Ctx.HoldMoveSpeedMultiplier) * Time.fixedDeltaTime;
            Ctx.RB.MovePosition(Ctx.RB.position + delta);
        }
    }
}

// Fire
public sealed class FireState : PlayerState
{
    public override void Enter()
    {
        Ctx.PlayAnim("Fire");
        Ctx.RunAfter(0.3f, () => Ctx.Goto("Idle"));
    }
}

// Faint
public sealed class FaintState : PlayerState
{
    public override void Enter()
    {
        Ctx.RB.velocity = Vector2.zero;
        Ctx.PlayAnim("Faint");
        Ctx.HideHoldVfx();
    }
}
