using UnityEngine;

/// <summary>
/// _magicAnchor 오브젝트에 붙어서 다른 플레이어의 _magicAnchor와의 충돌을 감지합니다.
/// 이 감지는 순전히 로컬 UI 흡수 로직에 사용됩니다.
/// </summary>
public class MagicAnchorCollision : MonoBehaviour
{
    private PlayerMagicController _magicController;
    
    /// <summary>
    /// PlayerMagicController를 설정합니다.
    /// </summary>
    public void Initialize(PlayerMagicController controller)
    {
        // 초기화 시 부모의 PlayerMagicController 설정
        _magicController = controller;
    }
    
    /// <summary>
    /// 다른 플레이어의 _magicAnchor와의 충돌을 감지합니다.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_magicController == null) return;
        
        // 충돌 대상의 MagicAnchorCollision 스크립트 가져오기
        MagicAnchorCollision otherAnchor = other.GetComponent<MagicAnchorCollision>();
        
        if (otherAnchor != null)
        {
            // 다른 앵커의 PlayerMagicController를 통해 PlayerController를 가져옴
            PlayerController otherPlayer = otherAnchor.GetOtherPlayerController();
            
            // 유효한 다른 플레이어 컨트롤러인지 확인
            if (otherPlayer != null)
            {
                // 충돌 처리 로직을 PlayerMagicController로 위임
                _magicController.OnPlayerCollisionEnter(otherPlayer);
            }
        }
    }

    /// <summary>
    /// 외부에서 이 충돌 오브젝트의 PlayerController를 안전하게 가져오기 위한 Public 메서드
    /// </summary>
    public PlayerController GetOtherPlayerController()
    {
        return _magicController?.Controller;
    }
}