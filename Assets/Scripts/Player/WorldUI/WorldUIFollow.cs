using UnityEngine;

[DisallowMultipleComponent]
public class WorldUIFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;               
    public Vector2 worldOffset = new(0.0f, 1.2f);

    [Header("2D Options")]
    public bool lockRotation = true;        
    public bool pixelSnap = false;           
    public float pixelsPerUnit = 100.0f;      

    Camera cam;

    void Awake()
    {
        if (!cam) cam = Camera.main;
    }

    void LateUpdate()
    {
        if (!target) return;

        Vector3 pos = target.position;
        pos.x += worldOffset.x;
        pos.y += worldOffset.y;
        pos.z = transform.position.z;

        if (pixelSnap && pixelsPerUnit > 0.0f)
        {
            pos.x = Mathf.Round(pos.x * pixelsPerUnit) / pixelsPerUnit;
            pos.y = Mathf.Round(pos.y * pixelsPerUnit) / pixelsPerUnit;
        }

        transform.position = pos;

        if (lockRotation)
            transform.rotation = Quaternion.identity; 
    }
}
