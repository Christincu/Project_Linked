using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어의 보호막 시각 효과를 관리하는 컴포넌트
/// </summary>
public class PlayerBarrierVisual : MonoBehaviour
{
    #region Private Fields
    private PlayerController _controller;
    private GameObject _viewObj;
    private GameObject _barrierVisualObject;
    private bool _previousHasBarrier = false;
    #endregion

    #region Initialization
    /// <summary>
    /// 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController controller)
    {
        _controller = controller;
        _viewObj = controller.ViewObj;
        _previousHasBarrier = false;
    }
    
    /// <summary>
    /// ViewObj가 변경되었을 때 호출합니다.
    /// </summary>
    public void OnViewObjChanged(GameObject newViewObj)
    {
        _viewObj = newViewObj;
        // 기존 보호막 오브젝트 제거 후 재생성
        if (_barrierVisualObject != null)
        {
            DestroyBarrierVisual();
            if (_controller.HasBarrier)
            {
                CreateBarrierVisual();
            }
        }
    }
    #endregion

    #region Visual Update
    /// <summary>
    /// 보호막 시각 효과를 업데이트합니다.
    /// </summary>
    public void UpdateBarrierVisual()
    {
        if (_controller == null) return;
        
        if (_controller.HasBarrier && _barrierVisualObject == null)
        {
            CreateBarrierVisual();
        }
        else if (!_controller.HasBarrier && _barrierVisualObject != null)
        {
            DestroyBarrierVisual();
        }
        
        _previousHasBarrier = _controller.HasBarrier;
    }
    
    /// <summary>
    /// HasBarrier 상태 변경을 확인하고 업데이트합니다.
    /// </summary>
    public void CheckBarrierState()
    {
        if (_controller == null) return;
        
        if (_controller.HasBarrier != _previousHasBarrier)
        {
            UpdateBarrierVisual();
        }
    }
    #endregion

    #region Visual Creation
    /// <summary>
    /// 보호막 시각 효과 오브젝트를 생성합니다.
    /// </summary>
    private void CreateBarrierVisual()
    {
        if (_viewObj == null) return;
        
        // 보호막 오브젝트 생성
        _barrierVisualObject = new GameObject("BarrierVisual");
        _barrierVisualObject.transform.SetParent(_viewObj.transform, false);
        _barrierVisualObject.transform.localPosition = Vector3.zero;
        
        // SpriteRenderer 추가
        SpriteRenderer barrierRenderer = _barrierVisualObject.AddComponent<SpriteRenderer>();
        
        // 기본 원형 스프라이트 생성
        Texture2D barrierTexture = CreateCircleTexture(64, new Color(0.2f, 0.8f, 1f, 0.5f)); // 반투명 청록색
        Sprite barrierSprite = Sprite.Create(barrierTexture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64);
        barrierRenderer.sprite = barrierSprite;
        
        // 플레이어보다 약간 크게 설정
        _barrierVisualObject.transform.localScale = Vector3.one * 1.3f;
        
        // 플레이어 스프라이트보다 뒤에 렌더링
        SpriteRenderer playerRenderer = _viewObj.GetComponent<SpriteRenderer>();
        if (playerRenderer != null)
        {
            barrierRenderer.sortingOrder = playerRenderer.sortingOrder - 1;
        }
        else
        {
            barrierRenderer.sortingOrder = -1;
        }
        
        Debug.Log($"[BarrierVisual] Created barrier visual for {_controller.name}");
    }
    
    /// <summary>
    /// 보호막 시각 효과 오브젝트를 제거합니다.
    /// </summary>
    private void DestroyBarrierVisual()
    {
        if (_barrierVisualObject != null)
        {
            Destroy(_barrierVisualObject);
            _barrierVisualObject = null;
            Debug.Log($"[BarrierVisual] Removed barrier visual for {_controller.name}");
        }
    }
    
    /// <summary>
    /// 원형 텍스처를 생성합니다.
    /// </summary>
    private Texture2D CreateCircleTexture(int size, Color color)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.4f;
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 pos = new Vector2(x, y);
                float distance = Vector2.Distance(pos, center);
                
                if (distance <= radius)
                {
                    // 거리에 따라 알파값 조정 (외곽은 더 투명하게)
                    float alpha = 1f - (distance / radius) * 0.5f;
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

    #region Cleanup
    /// <summary>
    /// 오브젝트가 파괴될 때 호출됩니다.
    /// </summary>
    private void OnDestroy()
    {
        DestroyBarrierVisual();
    }
    #endregion
}

