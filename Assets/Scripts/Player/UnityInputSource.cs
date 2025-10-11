using UnityEngine;

public class UnityInputSource : IInputSource
{
    public Vector2 Move => Controls.ReadMove();
    public bool LMBHold => Controls.LMBHold();
    public bool RMBHold => Controls.RMBHold();
    public bool LMBDown => Controls.LMBDown();
    public bool RMBDown => Controls.RMBDown();
    public bool LMBUp => Controls.LMBUp();
    public bool RMBUp => Controls.RMBUp();
    public bool InteractPressed => Controls.InteractPressed();
}
