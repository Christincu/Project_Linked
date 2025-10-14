using UnityEngine;

[CreateAssetMenu(menuName = "Game/Control Scheme")]
public class ControlSchemeSO : ScriptableObject
{
    [Header("Movement")]
    public KeyCode up = KeyCode.W;
    public KeyCode down = KeyCode.S;
    public KeyCode left = KeyCode.A;
    public KeyCode right = KeyCode.D;
    public bool allowArrowsAsMove = true;

    [Header("Actions")]
    public KeyCode interact = KeyCode.F;
    public int leftMouse = 0;
    public int rightMouse = 1; 
}
