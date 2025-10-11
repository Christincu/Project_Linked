using UnityEngine;

public class Controls : MonoBehaviour
{
    public static Controls Instance { get; private set; }
    public ControlSchemeSO scheme;

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else Destroy(gameObject);
    }

    public static Vector2 ReadMove()
    {
        var s = Instance?.scheme;
        float x = 0, y = 0;
        if (s == null)
        {
            x = Input.GetAxisRaw("Horizontal"); y = Input.GetAxisRaw("Vertical");
        }
        else
        {
            if (Input.GetKey(s.left)) x -= 1; if (Input.GetKey(s.right)) x += 1;
            if (s.allowArrowsAsMove)
            {
                if (Input.GetKey(KeyCode.LeftArrow)) x -= 1;
                if (Input.GetKey(KeyCode.RightArrow)) x += 1;
            }
            if (Input.GetKey(s.down)) y -= 1; if (Input.GetKey(s.up)) y += 1;
        }
        var v = new Vector2(x, y);
        return v.sqrMagnitude > 1 ? v.normalized : v;
    }

    public static bool InteractPressed()
    {
        var s = Instance?.scheme;
        return s != null ? Input.GetKeyDown(s.interact) : Input.GetKeyDown(KeyCode.F);
    }

    public static bool LMBHold() => Input.GetMouseButton(Instance?.scheme?.leftMouse ?? 0);
    public static bool RMBHold() => Input.GetMouseButton(Instance?.scheme?.rightMouse ?? 1);
    public static bool LMBDown() => Input.GetMouseButtonDown(Instance?.scheme?.leftMouse ?? 0);
    public static bool RMBDown() => Input.GetMouseButtonDown(Instance?.scheme?.rightMouse ?? 1);
    public static bool LMBUp() => Input.GetMouseButtonUp(Instance?.scheme?.leftMouse ?? 0);
    public static bool RMBUp() => Input.GetMouseButtonUp(Instance?.scheme?.rightMouse ?? 1);
}
