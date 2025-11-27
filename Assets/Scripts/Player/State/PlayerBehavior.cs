using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어의 게임 로직을 담당합니다.
/// 전투, 상호작용 등 게임플레이 관련 기능을 처리합니다.
/// MonoBehaviour로 작동하며 네트워크 기능은 PlayerController를 통해 접근합니다.
/// 
/// 현재는 플레이스홀더로 사용되며, 향후 게임플레이 기능 확장 시 사용됩니다.
/// </summary>
public class PlayerBehavior : MonoBehaviour
{
    #region Private Fields
    private PlayerController _controller;
    #endregion

    #region Properties
    /// <summary>
    /// 연결된 PlayerController를 반환합니다.
    /// </summary>
    public PlayerController Controller => _controller;
    #endregion

    #region Initialization
    /// <summary>
    /// PlayerController에서 호출하여 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController controller)
    {
        _controller = controller;
    }
    #endregion
}
