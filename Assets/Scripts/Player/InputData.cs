using Fusion;
using UnityEngine;

// NetworkButtons는 비트 플래그로 동작하므로 비트 시프트 사용
[System.Flags]
public enum InputButton
{
    LEFT = 1 << 0,
    RIGHT = 1 << 1,
    UP = 1 << 2,
    DOWN = 1 << 3,
}

[System.Flags]
public enum InputMouseButton
{
    LEFT = 1 << 0,
    RIGHT = 1 << 1,
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
        return Buttons.IsSet(button);
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
        return MouseButtons.IsSet(button);
    }

    public NetworkButtons GetMouseButtonPressed(NetworkButtons prev)
    {
        return MouseButtons.GetPressed(prev);
    }
}
