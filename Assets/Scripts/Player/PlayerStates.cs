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

        // 왼클릭: 들기 시작
        if (Ctx.Input.LMBDown)
        {
            if (Ctx.SkillAsset != null)
            {
                Ctx.Goto("Hold");
                return;
            }
        }

        // 이미 들고 있는 상태에서 오른클릭: 던지기
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

        // 이동 중에도 즉시 들기/던지기 가능
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
        // 이미 들고 있으면 유지, 아니면 새로 들기 시작
        if (!Ctx.IsHolding)
        {
            if (Ctx.SkillAsset == null) { Ctx.Goto("Idle"); return; }
            Ctx.BeginHold(); // 왼클릭 역할: "원소 들고 있기" 시작
        }

        Ctx.PlayAnim("Hold");
    }

    public override void Tick()
    {
        // 들고 있는 동안 좌우 입력 반영
        var m = Ctx.Input.Move;
        if (Mathf.Abs(m.x) > 0.01f) Ctx.SetFacingByX(m.x);

        // 오른클릭 들어오면 던지기
        if (Ctx.Input.RMBDown)
        {
            Ctx.Goto("Fire");
            return;
        }
    }

    public override void FixedTick()
    {
        // 들고 있어도 이동 가능
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
        Ctx.SkillAsset?.OnFire(Ctx.gameObject); // 발사 처리
        GameEvents.SkillFired?.Invoke();

        // 들고 있던 건 해제
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
        Ctx.EndHold(); // 쓰러질 땐 들고 있던 것도 해제
    }
}
