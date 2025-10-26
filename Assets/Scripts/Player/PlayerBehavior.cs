using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어의 게임 로직을 담당합니다.
/// 전투, 상호작용 등 게임플레이 관련 기능을 처리합니다.
/// MonoBehaviour로 작동하며 네트워크 기능은 PlayerController를 통해 접근합니다.
/// </summary>
public class PlayerBehavior : MonoBehaviour
{
    #region Private Fields
    private PlayerController _controller;
    #endregion

    #region Events
    public System.Action OnAttackPerformed;
    public System.Action<int> OnSkillUsed;
    public System.Action OnInteracted;
    #endregion

    #region Properties
    /// <summary>
    /// 연결된 PlayerController를 반환합니다.
    /// </summary>
    public PlayerController Controller => _controller;
    #endregion

    #region Initialization
    public void Initialize(PlayerController controller)
    {
        _controller = controller;
        Debug.Log($"[PlayerBehavior] Initialized - Controller: {_controller != null}");
    }
    #endregion

    #region Combat
    public void PerformAttack()
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasInputAuthority) return;
        if (_controller.IsDead) return;

        Debug.Log($"[PlayerBehavior] Performed attack");
        OnAttackPerformed?.Invoke();
    }

    public void UseSkill(int skillIndex)
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasInputAuthority) return;
        if (_controller.IsDead) return;

        Debug.Log($"[PlayerBehavior] Used skill {skillIndex}");
        OnSkillUsed?.Invoke(skillIndex);
    }
    #endregion

    #region Interaction
    public void Interact()
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasInputAuthority) return;
        if (_controller.IsDead) return;

        Debug.Log($"[PlayerBehavior] Interacted");
        OnInteracted?.Invoke();
    }
    #endregion
}
