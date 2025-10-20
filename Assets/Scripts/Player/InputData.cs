using Fusion;
using UnityEngine;

public enum InputButton
{
    LEFT = 0,
    RIGHT = 1,
    UP = 2,
    DOWN = 3,
}

public enum InputMouseButton
{
    LEFT = 0,
    RIGHT = 1,
}

public struct InputData : INetworkInput
{
    public NetworkButtons Buttons;
    public NetworkButtons MouseButtons;
    public Vector2 MouseDelta;
    public Vector2 MousePosition;
    public float MouseScroll;
    public int ControlledSlot; // 로컬에서 선택된 조종 슬롯 (0,1,...)

    public bool GetButton(InputButton button)
    {
        return Buttons.IsSet((int)button);
    }

    public NetworkButtons GetButtonPressed(NetworkButtons prev)
    {
        return Buttons.GetPressed(prev);
    }

    public bool AxisPressed()
    {
        return GetButton(InputButton.LEFT) || GetButton(InputButton.RIGHT);
    }

    public bool GetMouseButton(InputMouseButton button)
    {
        return MouseButtons.IsSet((int)button);
    }

    public NetworkButtons GetMouseButtonPressed(NetworkButtons prev)
    {
        return MouseButtons.GetPressed(prev);
    }
}
