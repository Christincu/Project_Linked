using System;
using UnityEngine;

public class MergeConeEmitter : MonoBehaviour
{
    [Header("Timing")]
    public float totalDuration = 5.0f;    // 전체 지속
    public float segmentInterval = 0.20f; // 세그먼트 생성 주기
    public float angleSpawnDelta = 15.0f;   // 각도 변할 때 즉시 새 세그먼트

    [Header("Geometry")]
    public float attackRadius = 1.0f;     // 센터-공격점 거리(고정)
    public float segmentLife = 0.6f;     // 세그먼트 수명

    [Header("Prefabs")]
    public GameObject segmentPrefab;      // 실제 분사 파티클(프리팹)

    // 외부에서 주입
    private Transform _center;                       // 센터(보통 absorber의 앵커)
    private Func<Vector3> _getAimWorld;              // 월드 조준점(마우스)
    private Func<Vector2> _getForward;               // 현재 전방(좌/우)
    private Action _onFinished;                      // 다 끝났을 때 콜백(선택)

    // 상태
    private float _remain;
    private float _segTimer;
    private Vector2 _lastDir;

    public void Init(Transform center,
                     Func<Vector3> getAimWorld,
                     Func<Vector2> getForward,
                     GameObject segPrefab,
                     float duration,
                     float radius,
                     float segInterval,
                     float angleDelta,
                     float segLife,
                     Action onFinished = null)
    {
        _center = center;
        _getAimWorld = getAimWorld;
        _getForward = getForward;
        segmentPrefab = segPrefab;

        totalDuration = duration;
        attackRadius = radius;
        segmentInterval = segInterval;
        angleSpawnDelta = angleDelta;
        segmentLife = segLife;

        _remain = totalDuration;
        _segTimer = 0.0f;
        _lastDir = _getForward != null ? _getForward().normalized : Vector2.right;
        _onFinished = onFinished;

        SpawnSegment(_lastDir);
    }

    void Update()
    {
        if (_center == null || segmentPrefab == null) return;
        if (_remain <= 0f) return;

        _remain -= Time.deltaTime;
        _segTimer += Time.deltaTime;

        Vector2 forward = _getForward != null ? _getForward().normalized : Vector2.right;

        Vector3 aim = _getAimWorld != null ? _getAimWorld() : (_center.position + (Vector3)forward);
        aim.z = 0f;

        Vector2 rawDir = (aim - _center.position);
        if (rawDir.sqrMagnitude < 0.0001f) rawDir = forward;

        Vector2 clamped = ClampToFrontHemisphere(forward, rawDir);

        float angleDelta = Vector2.Angle(_lastDir, clamped);
        if (_segTimer >= segmentInterval || angleDelta >= angleSpawnDelta)
        {
            _lastDir = clamped;
            SpawnSegment(clamped);
            _segTimer = 0.0f;
        }

        if (_remain <= 0.0f)
        {
            _onFinished?.Invoke();
            Destroy(gameObject);
        }
    }

    private void SpawnSegment(Vector2 dir)
    {
        Vector3 pos = _center.position + (Vector3)(dir.normalized * attackRadius);
        var go = Instantiate(segmentPrefab, pos, Quaternion.identity);
        go.transform.right = dir.normalized;

        var seg = go.GetComponent<MergeSegment>();
        if (seg) seg.Init(segmentLife);
        else Destroy(go, segmentLife);
    }

    private static Vector2 ClampToFrontHemisphere(Vector2 forward, Vector2 dir)
    {
        dir = dir.normalized;
        if (Vector2.Dot(forward, dir) >= 0.0f) return dir;

        Vector2 perp = new Vector2(-forward.y, forward.x);
        float s = Vector2.Dot(perp, dir) >= 0 ? 1.0f : -1.0f;
        return (perp * s).normalized;
    }
}
