using UnityEngine;
using Fusion;

public class BarrierMagicObject : NetworkBehaviour
{
    #region Networked Properties
    [Networked] private PlayerRef OwnerRef { get; set; }
    #endregion

    #region Private Fields
    private PlayerController _owner;
    private BarrierMagicCombinationData _barrierData;
    private GameDataManager _gameDataManager;

    // Visuals
    private SpriteRenderer _barrierRenderer;
    private GameObject _explosionRangeObj;
    private SpriteRenderer _explosionRangeRenderer;
    private float _lastExplosionRadius = -1f;
    #endregion

    #region Unity & Fusion Callbacks
    public override void Spawned()
    {
        // 클라이언트 초기화
        InitializeClientComponents();
        FindOwner();
        
        // 부모 설정 (위치 동기화 시작)
        if (_owner != null)
        {
            transform.SetParent(_owner.transform);
            transform.localPosition = Vector3.zero;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // 서버 권한 로직 (종료 체크)
        if (Object.HasStateAuthority)
        {
            if (_owner == null) return;

            // 베리어가 해제되었거나 타이머가 끝났으면 제거
            if (!_owner.HasBarrier)
            {
                Runner.Despawn(Object);
            }
        }
    }

    public override void Render()
    {
        // 매 프레임 시각적 업데이트
        if (_owner == null) FindOwner();
        if (_owner == null) return;
        
        // 데이터 로드 (안전장치)
        if (_barrierData == null) LoadBarrierData();
        if (_barrierData == null) return;

        // 시각화 갱신
        UpdateBarrierVisuals();
        UpdateExplosionRangeVisuals();
    }

    private void LateUpdate()
    {
        // [중요] 부모가 NetworkRigidbody로 움직일 때 자식이 튀는 현상 방지
        // 매 프레임 강제로 위치를 0으로 고정
        if (_owner != null)
        {
            if (transform.parent != _owner.transform) transform.SetParent(_owner.transform);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }
    }
    #endregion

    #region Initialization
    public void Initialize(PlayerController owner, BarrierMagicCombinationData data)
    {
        _owner = owner;
        _barrierData = data;

        if (Object.HasStateAuthority)
        {
            OwnerRef = owner.Object.InputAuthority;
        }
    }

    private void InitializeClientComponents()
    {
        // 베리어 렌더러
        if (_barrierRenderer == null)
        {
            _barrierRenderer = GetComponent<SpriteRenderer>() ?? gameObject.AddComponent<SpriteRenderer>();
            _barrierRenderer.sortingOrder = 10;
            _barrierRenderer.sortingLayerName = "Default";
        }
        
        // 폭발 범위 오브젝트
        if (_explosionRangeObj == null)
        {
            _explosionRangeObj = new GameObject("ExplosionRange");
            _explosionRangeObj.transform.SetParent(transform, false);
            _explosionRangeRenderer = _explosionRangeObj.AddComponent<SpriteRenderer>();
            _explosionRangeRenderer.sortingOrder = 5; // 바닥보다 위, 베리어보다 아래
            _explosionRangeRenderer.sortingLayerName = "Default";
            _explosionRangeRenderer.enabled = false;
            
            // 텍스처 생성
            Texture2D tex = CreateCircleTexture(128, Color.white);
            _explosionRangeRenderer.sprite = Sprite.Create(tex, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 128);
        }
        
        LoadBarrierData();
    }
    
    private void FindOwner()
    {
        if (_owner != null) return;
        
        // 1. OwnerRef를 통해 찾기 (서버에서 설정됨)
        if (Runner != null && OwnerRef != PlayerRef.None && MainGameManager.Instance != null)
        {
            _owner = MainGameManager.Instance.GetPlayer(OwnerRef);
            if (_owner != null) return;
        }
    }

    private void LoadBarrierData()
    {
        if (_gameDataManager == null) _gameDataManager = GameDataManager.Instance ?? FindObjectOfType<GameDataManager>();
        if (_gameDataManager?.MagicService != null)
        {
            var data = _gameDataManager.MagicService.GetCombinationDataByResult(10); // Code 10 = Barrier
            _barrierData = data as BarrierMagicCombinationData;
        }
    }
    #endregion

    #region Visual Logic
    private void UpdateBarrierVisuals()
    {
        if (_barrierRenderer == null) return;
        
        // 초기 스프라이트 설정 (한 번만 하거나 변경 시)
        if (_barrierRenderer.sprite == null)
        {
            Texture2D tex = CreateCircleTexture(64, new Color(0.2f, 0.8f, 1f, 0.5f)); // 청록색 반투명
            _barrierRenderer.sprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64);
            transform.localScale = Vector3.one * 2.0f; // 크기 조정
        }
        
        _barrierRenderer.enabled = !_owner.IsDead;
    }

    private void UpdateExplosionRangeVisuals()
    {
        if (_explosionRangeRenderer == null || _owner.BarrierTimer.ExpiredOrNotRunning(Runner))
        {
            if (_explosionRangeRenderer) _explosionRangeRenderer.enabled = false;
            return;
        }

        _explosionRangeRenderer.enabled = true;
        float remaining = _owner.BarrierTimer.RemainingTime(Runner) ?? 0f;
        
        // 반지름 계산 및 적용
        float radius = _barrierData.GetExplosionRadius(remaining);
        
        // 최적화: 반경이 변했을 때만 스케일 수정
        if (Mathf.Abs(radius - _lastExplosionRadius) > 0.01f)
        {
            // 부모 스케일이 2.0이므로 자식 스케일 보정 (radius * 2 / 2.0 = radius)
            float scale = radius; 
            _explosionRangeObj.transform.localScale = new Vector3(scale, scale, 1f);
            _lastExplosionRadius = radius;
        }

        // 색상 변경 (단계별 경고)
        Color color = new Color(1f, 1f, 0f, 0.3f); // 노랑
        if (remaining > 7f) color = new Color(1f, 0f, 0f, 0.3f); // 빨강 (초기)
        else if (remaining > 3f) color = new Color(1f, 0.5f, 0f, 0.3f); // 주황
        
        _explosionRangeRenderer.color = color;
    }

    private Texture2D CreateCircleTexture(int size, Color color)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.45f;
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                if (dist <= radius)
                {
                    float alpha = Mathf.Clamp01((radius - dist) + 0.5f);
                    texture.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
                else
                {
                    texture.SetPixel(x, y, Color.clear);
                }
            }
        }
        texture.Apply();
        return texture;
    }
    #endregion

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // 자식 오브젝트(폭발 범위 표시 등)는 Unity가 자동 파괴하므로 별도 처리 불필요
        // 로컬 변수나 참조만 정리
        _owner = null;
        _barrierData = null;
        
        Debug.Log($"[BarrierMagicObject] Despawned (StateAuth: {hasState})");
    }
}