using UnityEngine;

/// <summary>
/// DashMagicObject의 시각화 관련 로직
/// </summary>
public partial class DashMagicObject
{
    #region Visual
    /// <summary>
    /// 강화 상태에 따라 베리어 스프라이트를 업데이트합니다.
    /// </summary>
    private void UpdateBarrierSprite()
    {
        // 렌더러가 없으면 생성 (안전장치)
        if (_barrierSpriteRenderer == null) 
        {
            _barrierSpriteRenderer = GetComponent<SpriteRenderer>();
            if (_barrierSpriteRenderer == null) 
            {
                _barrierSpriteRenderer = gameObject.AddComponent<SpriteRenderer>();
                _barrierSpriteRenderer.sortingOrder = 10;
                _barrierSpriteRenderer.sortingLayerName = "Default";
            }
        }
        
        if (_owner == null) return;
        
        // _dashData가 없으면 다시 가져오기 시도
        if (_dashData == null)
        {
            LoadDashData();
        }
        
        if (_dashData == null) return;
        
        Sprite targetSprite = _dashData.baseBarrierSprite;
        
        if (_owner.IsDashFinalEnhancement)
            targetSprite = _dashData.finalEnhancementBarrierSprite;
        else if (_owner.DashEnhancementCount >= 1)
            targetSprite = _dashData.enhancedBarrierSprite;
            
        if (targetSprite != null)
        {
            _barrierSpriteRenderer.sprite = targetSprite;
            _barrierSpriteRenderer.enabled = true;
            
            UpdateBarrierColliderSize(targetSprite);
        }
        else
        {
            _barrierSpriteRenderer.enabled = false;
            
            if (_barrierCollider != null)
            {
                _barrierCollider.enabled = false;
            }
        }
    }
    
    /// <summary>
    /// 베리어 콜라이더 크기를 스프라이트에 맞춰 업데이트합니다.
    /// </summary>
    private void UpdateBarrierColliderSize(Sprite sprite)
    {
        if (_barrierCollider == null)
        {
            _barrierCollider = GetComponent<CircleCollider2D>();
            if (_barrierCollider == null) 
            {
                _barrierCollider = gameObject.AddComponent<CircleCollider2D>();
                _barrierCollider.isTrigger = true;
            }
        }

        if (sprite != null)
        {
            float radius = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y) * 0.5f;
            _barrierCollider.radius = radius;
            _barrierCollider.enabled = true;
        }
    }
    #endregion
}

