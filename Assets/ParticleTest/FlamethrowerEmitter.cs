using UnityEngine;

public class FlamethrowerEmitter : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private ParticleSystem flamePrefab;

    [Header("Anchors & Owner")]
    [SerializeField] private Transform anchor;
    [SerializeField] private PlayerController owner;

    [Header("Behaviour")]
    [SerializeField] private float aimDistance = 1.2f;
    [SerializeField] private float segInterval = 0.12f;
    [SerializeField] private float segmentLifetime = 0.7f;
    [SerializeField] private float totalDurationOnRelease = 5.0f;

    [Header("Input (¿É¼Ç)")]
    [SerializeField] private bool useMouseInput = true;

    private bool _isHolding;
    private bool _released;
    private float _lastSpawnAt;
    private float _releaseEndsAt;
    private bool _forceStop;  

    void Reset() { anchor = transform; }

    void Update()
    {
        if (useMouseInput)
        {
            if (Input.GetMouseButtonDown(0)) BeginHold();
            if (Input.GetMouseButtonUp(0)) ReleaseHold();
        }

        if (_forceStop) return; 

        if (!_isHolding && !_released) return;

        if (_released && Time.time >= _releaseEndsAt)
        {
            StopAll();
            return;
        }

        if (Time.time - _lastSpawnAt >= segInterval)
        {
            SpawnOneSegment(GetClampedAimDir(), anchor ? anchor.position : transform.position);
            _lastSpawnAt = Time.time;
        }
    }

    public void BeginHold()
    {
        _forceStop = false;
        _isHolding = true;
        _released = false;
        _lastSpawnAt = 0f;
    }

    public void ReleaseHold()
    {
        if (!_isHolding) return;
        _isHolding = false;
        _released = true;
        _releaseEndsAt = Time.time + Mathf.Max(0f, totalDurationOnRelease);
    }

    public void StopAll()
    {
        _isHolding = false;
        _released = false;
        _forceStop = true; 
    }

    private Vector2 GetClampedAimDir()
    {
        var center = anchor ? anchor.position : transform.position;
        Vector2 fwd = Vector2.right;
        if (owner != null)
            fwd = (owner.ScaleX < 0f) ? Vector2.right : Vector2.left;

        var cam = Camera.main;
        Vector3 mouse = cam ? cam.ScreenToWorldPoint(Input.mousePosition) : Vector3.zero;
        mouse.z = 0f;

        Vector2 rawDir = ((Vector2)mouse - (Vector2)center).normalized;
        if (rawDir.sqrMagnitude < 0.0001f) rawDir = fwd;

        float ang = Vector2.SignedAngle(fwd, rawDir);
        float clamped = Mathf.Clamp(ang, -90f, 90f);
        Quaternion rot = Quaternion.AngleAxis(clamped, Vector3.forward);
        Vector2 dir = rot * fwd;

        return dir.normalized;
    }

    private void SpawnOneSegment(Vector2 dir, Vector3 origin)
    {
        if (!flamePrefab) return;
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Quaternion rot = Quaternion.AngleAxis(ang, Vector3.forward);
        Vector3 pos = origin + (Vector3)(dir * aimDistance);

        var ps = Instantiate(flamePrefab, pos, rot);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        ps.Clear(true);
        ps.Play(true);
        Destroy(ps.gameObject, segmentLifetime);
    }
}
