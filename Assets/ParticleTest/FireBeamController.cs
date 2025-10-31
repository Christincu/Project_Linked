using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class FireBeamController : MonoBehaviour
{
    #region === 인터페이스 정의 ===
    /// <summary>
    /// FireBeamController가 외부에서 공격 속도(초당 공격 횟수)를 받아올 수 있게 하는 인터페이스.
    /// </summary>
    public interface IAttackRateProvider
    {
        /// <summary>
        /// 초당 공격 횟수(Attacks Per Second).
        /// 0 이하를 반환하면 FireBeamController의 기본값을 사용합니다.
        /// </summary>
        float GetAttacksPerSecond();
    }
    #endregion

    #region === Refs ===
    [Header("References")]
    [Tooltip("비워두면 MainCamera 자동 할당")]
    public Camera cam;

    [Tooltip("빔 회전의 기준이 되는 축(필수)")]
    public Transform startPointAnchor;

    [Tooltip("시각용 파티클(회전/이동 건드리지 않음)")]
    public ParticleSystem vfx;

    [Tooltip("끝점(퍼지는 부분) 콜라이더 (CircleCollider2D, Trigger)")]
    public CircleCollider2D aimCollider;
    #endregion

    #region === Options ===
    [Header("General Options")]
    [Tooltip("활성 시, 시작 프레임 방향으로 고정 (마우스 추적 안함)")]
    public bool lockDirectionOnStart = false;

    [Tooltip("시작점과 끝점 사이 거리")]
    public float endDistance = 5.0f;

    [Tooltip("끝점 콜라이더 반경")]
    public float aimColliderRadius = 0.35f;

    [Tooltip("좌우 반전 기준 (비우면 현재 오브젝트)")]
    public Transform facingPivot;

    public bool flipByScaleX = true;
    public int manualFacing = 1;

    [Tooltip("콘 축 보정 (+X=0, +Y=-90 등)")]
    public float axisOffsetDeg = 0f;

    [Tooltip("빔 유지 시간(초)")]
    public float beamDuration = 5f;

    [Tooltip("Scene 뷰에서 디버그 레이 표시")]
    public bool debugDraw = false;
    #endregion

    #region === 공격 관련 설정 ===
    [Header("Attack Settings")]
    [Tooltip("부채꼴 반각(°)")]
    public float coneAngle = 60f;

    [Tooltip("한 번에 발사할 레이 수")]
    public int probesPerBurst = 7;

    [Tooltip("기본 초당 공격 횟수(APS)")]
    public float attacksPerSecond = 5f;

    [Tooltip("레이 충돌 레이어 마스크")]
    public LayerMask hitMask = ~0;

    [Tooltip("피격 대상 태그")]
    public string enemyTag = "Enemy";

    [Tooltip("공격 1회당 데미지")]
    public int damagePerHit = 10;

    [Tooltip("같은 버스트 내 중복 타격 방지")]
    public bool preventDuplicateHitPerBurst = true;

    [Tooltip("피격 시 SpriteRenderer 하이라이트")]
    public bool blinkOnHit = true;

    [Tooltip("하이라이트 지속 시간(초)")]
    public float blinkDuration = 0.12f;
    #endregion

    #region === Attack Rate Provider ===
    [Header("Attack Rate Provider (Optional)")]
    [Tooltip("공격 속도를 외부에서 제어하고 싶을 때 연결 (IAttackRateProvider 구현체)")]
    public MonoBehaviour attackRateProviderBehaviour;
    private IAttackRateProvider _rateProvider;
    #endregion

    #region === Private Fields ===
    private Camera _cam;
    private float _endTime;
    private float _nextFireTime;
    private Vector3 _endPos;
    private float _endAngleDeg;
    private bool _locked;
    private readonly HashSet<GameObject> _hitThisBurst = new();
    #endregion

    #region === Unity Lifecycle ===
    void Awake()
    {
        _cam = cam ? cam : Camera.main;
        if (!facingPivot) facingPivot = transform;

        if (!startPointAnchor || !aimCollider)
        {
            Debug.LogError("[FireBeamController] startPointAnchor 또는 aimCollider가 비어 있음.", this);
            enabled = false;
            return;
        }

        aimCollider.isTrigger = true;
        aimCollider.radius = aimColliderRadius;

        if (attackRateProviderBehaviour)
            _rateProvider = attackRateProviderBehaviour as IAttackRateProvider;

        if (vfx)
        {
            var main = vfx.main;
            if (vfx.transform.parent != startPointAnchor || main.simulationSpace != ParticleSystemSimulationSpace.Local)
                Debug.LogWarning("[FireBeamController] VFX는 startPointAnchor의 자식이며 SimulationSpace=Local 권장.");
        }
    }

    void OnEnable()
    {
        ComputeEndAndDirection(out _endPos, out _endAngleDeg);
        ApplyRotation(_endAngleDeg);
        SnapAimCollider();

        _locked = lockDirectionOnStart;
        _endTime = Time.time + Mathf.Max(0.01f, beamDuration);
        _nextFireTime = 0f;
        _hitThisBurst.Clear();

        if (vfx)
        {
            vfx.Clear();
            vfx.Play();
        }
    }

    void Update()
    {
        if (!startPointAnchor) return;

        if (!_locked)
        {
            ComputeEndAndDirection(out _endPos, out _endAngleDeg);
            ApplyRotation(_endAngleDeg);
        }

        SnapAimCollider();

        float aps = GetCurrentAPS();
        if (aps > 0f && Time.time >= _nextFireTime)
        {
            CastConeRaycasts();
            _nextFireTime = Time.time + (1f / aps);
        }

        if (Time.time >= _endTime && vfx && vfx.isPlaying)
            vfx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void OnDisable() { StopAllCoroutines(); }
    void OnDestroy() { StopAllCoroutines(); }
    #endregion

    #region === Aim / Visual ===
    private void ComputeEndAndDirection(out Vector3 endPos, out float endAngleDeg)
    {
        Vector3 origin = startPointAnchor.position;
        Vector3 mp = Input.mousePosition;
        float z = _cam.WorldToScreenPoint(origin).z;
        if (z <= 0f) z = 1f;
        Vector3 wp = _cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, z));

        Vector2 dir = (Vector2)(wp - origin);
        if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right;
        dir.Normalize();

        int face = flipByScaleX ? ((facingPivot.localScale.x >= 0) ? 1 : -1)
                                : ((manualFacing >= 0) ? 1 : -1);
        if (face < 0) dir.x = -dir.x;

        float baseAng = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        endAngleDeg = baseAng + axisOffsetDeg;

        Vector2 fwd = new(Mathf.Cos(endAngleDeg * Mathf.Deg2Rad),
                          Mathf.Sin(endAngleDeg * Mathf.Deg2Rad));
        endPos = origin + (Vector3)(fwd * Mathf.Max(0.01f, endDistance));
    }

    private void ApplyRotation(float angleDeg)
    {
        startPointAnchor.rotation = Quaternion.Euler(0f, 0f, angleDeg);
    }

    private void SnapAimCollider()
    {
        aimCollider.radius = aimColliderRadius;
        aimCollider.transform.position = _endPos;
    }
    #endregion

    #region === Attack Rate ===
    private float GetCurrentAPS()
    {
        if (_rateProvider != null)
        {
            float p = _rateProvider.GetAttacksPerSecond();
            if (p > 0f) return p;
        }
        return Mathf.Max(0f, attacksPerSecond);
    }
    #endregion

    #region === Raycast Logic ===
    private void CastConeRaycasts()
    {
        _hitThisBurst.Clear();

        Vector2 origin = startPointAnchor.position;
        float half = Mathf.Clamp(coneAngle, 0f, 360f) * 0.5f;
        float startDeg = _endAngleDeg - half;
        float step = (probesPerBurst > 1) ? (2f * half) / (probesPerBurst - 1) : 0f;

        for (int i = 0; i < Mathf.Max(1, probesPerBurst); i++)
        {
            float ang = startDeg + step * i;
            float rad = ang * Mathf.Deg2Rad;
            Vector2 dir = new(Mathf.Cos(rad), Mathf.Sin(rad));

            var hits = Physics2D.RaycastAll(origin, dir, endDistance, hitMask);

            if (debugDraw)
                Debug.DrawRay(origin, dir * endDistance, Color.yellow, 0.15f);

            foreach (var h in hits)
            {
                if (!h.collider) continue;
                var go = h.collider.gameObject;

                if (!string.IsNullOrEmpty(enemyTag) && !go.CompareTag(enemyTag))
                    continue;

                if (preventDuplicateHitPerBurst && _hitThisBurst.Contains(go))
                    continue;

                _hitThisBurst.Add(go);

                if (go.TryGetComponent(out Damageable dmg))
                    dmg.ApplyDamage(damagePerHit);

                if (blinkOnHit && go.TryGetComponent(out SpriteRenderer sr) && sr && go.activeInHierarchy)
                    StartCoroutine(Blink(sr, blinkDuration));
            }
        }
    }
    #endregion

    #region === Blink Effect ===
    private System.Collections.IEnumerator Blink(SpriteRenderer sr, float dur)
    {
        if (!sr) yield break;
        var go = sr.gameObject;
        if (!go || !go.activeInHierarchy) yield break;

        Color original;
        try { original = sr.color; }
        catch { yield break; }

        try { sr.color = Color.white; } catch { yield break; }

        float t = Mathf.Max(0.01f, dur);
        float elapsed = 0f;
        while (elapsed < t)
        {
            if (!sr || !go || !go.activeInHierarchy) yield break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (sr && go && go.activeInHierarchy)
        {
            try { sr.color = original; } catch { }
        }
    }
    #endregion
}
