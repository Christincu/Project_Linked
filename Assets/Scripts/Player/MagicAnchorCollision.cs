using UnityEngine;

/// <summary>
/// _magicAnchor 오브젝트에 붙어서 다른 플레이어의 _magicAnchor와의 충돌을 감지합니다.
/// </summary>
public class MagicAnchorCollision : MonoBehaviour
{
    private PlayerMagicController _magicController;
    
    /// <summary>
    /// PlayerMagicController를 설정합니다.
    /// </summary>
    public void Initialize(PlayerMagicController controller)
    {
        _magicController = controller;
    }
    
    /// <summary>
    /// 다른 플레이어의 _magicAnchor와의 충돌을 감지합니다.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_magicController == null) return;
        
        // 다른 _magicAnchor인지 확인
        MagicAnchorCollision otherAnchor = other.GetComponent<MagicAnchorCollision>();
        if (otherAnchor != null && otherAnchor._magicController != null)
        {
            // 다른 플레이어의 PlayerController를 가져와서 전달
            PlayerController otherPlayer = otherAnchor._magicController.Controller;
            if (otherPlayer != null)
            {
                _magicController.OnPlayerCollisionEnter(otherPlayer);
            }
        }
    }
    
    /// <summary>
    /// 충돌 종료를 감지합니다.
    /// </summary>
    private void OnTriggerExit2D(Collider2D other)
    {
        if (_magicController == null) return;
        
        // 다른 _magicAnchor인지 확인
        MagicAnchorCollision otherAnchor = other.GetComponent<MagicAnchorCollision>();
        if (otherAnchor != null && otherAnchor._magicController != null)
        {
            // 다른 플레이어의 PlayerController를 가져와서 전달
            PlayerController otherPlayer = otherAnchor._magicController.Controller;
            if (otherPlayer != null)
            {
                _magicController.OnPlayerCollisionExit(otherPlayer);
            }
        }
    }
}

