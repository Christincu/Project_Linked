using UnityEngine;
public class FollowTransform : MonoBehaviour
{
    public Transform target; public Vector3 offset; public bool matchRotation = false;
    void LateUpdate() { if (!target) return; transform.position = target.position + offset; if (matchRotation) transform.rotation = target.rotation; }
}
