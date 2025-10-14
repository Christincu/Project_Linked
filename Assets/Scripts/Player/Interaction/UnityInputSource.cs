using UnityEngine;

public class UnityInputSource : IInputSource
{
    public Vector2 Move
    {
        get
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            return new Vector2(h, v).normalized;
        }
    }

    public bool LMBHold => Input.GetMouseButton(0);
    public bool RMBHold => Input.GetMouseButton(1);
    public bool LMBDown => Input.GetMouseButtonDown(0);
    public bool RMBDown => Input.GetMouseButtonDown(1);
    public bool LMBUp => Input.GetMouseButtonUp(0);
    public bool RMBUp => Input.GetMouseButtonUp(1);

    public bool InteractPressed =>
        Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.E);
}
