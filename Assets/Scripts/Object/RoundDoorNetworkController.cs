using UnityEngine;
using Fusion;

/// <summary>
/// 라운드 문 오브젝트의 열림/닫힘 상태를 네트워크로 동기화합니다.
/// - 기본 상태: 열림 (플레이어가 자유롭게 이동 가능)
/// - 라운드 시작 시: 닫힘 (라운드 진행 중 이동 차단)
/// - 라운드 완료 시: 다시 열림 (다음 라운드로 이동 가능)
/// - 서버(StateAuthority)에서 IsClosed 값을 변경하면
///   모든 클라이언트에서 동일하게 시각/콜라이더 상태를 적용합니다.
/// - 문에는 NetworkObject + 이 컴포넌트를 추가해 사용하는 것을 권장합니다.
/// </summary>
public class RoundDoorNetworkController : NetworkBehaviour
{
    [Networked] public NetworkBool IsClosed { get; set; } = false; // 초기값: 열림 (라운드 시작 전 기본 상태)

    private Collider2D[] _colliders;
    private Renderer[] _renderers;
    private SpriteRenderer[] _spriteRenderers;
    
    // Fusion 변경 감지기
    private ChangeDetector _changeDetector;
    
    // 원본 색상 저장 (SpriteRenderer용)
    private Color[] _originalColors;

    [Header("Visual Settings")]
    [Tooltip("문이 열렸을 때의 투명도 (0.0 = 완전 투명, 1.0 = 불투명)")]
    [SerializeField] private float _openAlpha = 0.3f;
    
    [Tooltip("문이 닫혔을 때의 투명도 (0.0 = 완전 투명, 1.0 = 불투명)")]
    [SerializeField] private float _closedAlpha = 1.0f;

    private void Awake()
    {
        // 자신 및 자식에서 콜라이더/렌더러를 모두 수집
        _colliders = GetComponentsInChildren<Collider2D>(true);
        _renderers = GetComponentsInChildren<Renderer>(true);
        _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        
        // 원본 색상 저장 (SpriteRenderer용)
        if (_spriteRenderers != null && _spriteRenderers.Length > 0)
        {
            _originalColors = new Color[_spriteRenderers.Length];
            for (int i = 0; i < _spriteRenderers.Length; i++)
            {
                if (_spriteRenderers[i] != null)
                {
                    _originalColors[i] = _spriteRenderers[i].color;
                }
            }
        }
        
        // 초기 상태: 기본값은 열림 (콜라이더 비활성화)
        // Spawned()가 호출되기 전에도 열린 상태로 시작하도록 보장
        if (_colliders != null)
        {
            foreach (var col in _colliders)
            {
                if (col != null)
                {
                    col.enabled = false; // 열림 상태 = 콜라이더 비활성화
                }
            }
        }
    }

    public override void Spawned()
    {
        base.Spawned();
        
        // 변경 감지기 초기화
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        
        // 서버인 경우 초기 상태를 명시적으로 열린 상태로 설정 (기본값 보장)
        if (Object != null && Object.HasStateAuthority)
        {
            // 기본 상태는 열림이므로 false로 명시적 설정
            IsClosed = false;
        }
        
        // 초기 상태 강제 적용 (접속 시점 동기화)
        ApplyState();
    }

    public override void Render()
    {
        // 매 프레임 호출이 아니라, 값이 '변했을 때만' 호출 (최적화)
        foreach (var change in _changeDetector.DetectChanges(this))
        {
            if (change == nameof(IsClosed))
            {
                ApplyState();
            }
        }
    }

    /// <summary>
    /// 서버에서 호출하여 문 상태를 변경합니다.
    /// </summary>
    public void SetClosed(bool closed)
    {
        Debug.Log($"[RoundDoorNetworkController] SetClosed: {closed}");
        // StateAuthority 확인 (서버 권한 체크)
        if (Object == null || !Object.HasStateAuthority)
        {
            return;
        }

        IsClosed = closed;
        // Render()의 DetectChanges가 처리하도록 둠 (네트워크 동기화 후 자동 적용)
    }

    /// <summary>
    /// Networked IsClosed 값에 따라 콜라이더/렌더러 상태를 적용합니다.
    /// - 콜라이더: 닫힘(true)일 때만 활성화 → 통행 불가
    /// - 렌더러: 열림/닫힘 상태에 따라 투명도 조절 → 시각적 피드백
    /// </summary>
    private void ApplyState()
    {
        bool closed = IsClosed;

        // 1. 콜라이더 처리 (물리적 차단)
        if (_colliders != null)
        {
            foreach (var col in _colliders)
            {
                if (col != null)
                {
                    col.enabled = closed;
                }
            }
        }

        // 2. 렌더러 처리 (시각적 피드백)
        float targetAlpha = closed ? _closedAlpha : _openAlpha;
        
        // SpriteRenderer인 경우 (더 효율적)
        if (_spriteRenderers != null && _originalColors != null)
        {
            for (int i = 0; i < _spriteRenderers.Length; i++)
            {
                if (_spriteRenderers[i] != null && i < _originalColors.Length)
                {
                    Color c = _originalColors[i];
                    c.a = targetAlpha;
                    _spriteRenderers[i].color = c;
                }
            }
        }
        
        // 일반 Renderer인 경우 (MaterialPropertyBlock 사용하여 메모리 안전)
        if (_renderers != null)
        {
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                
                // SpriteRenderer는 이미 처리했으므로 스킵
                if (r is SpriteRenderer) continue;
                
                SetRendererAlpha(r, targetAlpha);
            }
        }
    }

    /// <summary>
    /// 일반 Renderer의 투명도를 조절합니다. (MaterialPropertyBlock 사용)
    /// </summary>
    private void SetRendererAlpha(Renderer r, float alpha)
    {
        if (r == null || r.material == null) return;
        
        // MaterialPropertyBlock을 사용하여 메모리 안전하게 색상 변경
        MaterialPropertyBlock propBlock = new MaterialPropertyBlock();
        r.GetPropertyBlock(propBlock);
        
        if (r.material.HasProperty("_Color"))
        {
            Color c = r.material.color;
            c.a = alpha;
            propBlock.SetColor("_Color", c);
            r.SetPropertyBlock(propBlock);
        }
        else if (r.material.HasProperty("_TintColor"))
        {
            Color c = r.material.GetColor("_TintColor");
            c.a = alpha;
            propBlock.SetColor("_TintColor", c);
            r.SetPropertyBlock(propBlock);
        }
    }
}


