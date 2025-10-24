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
    /// <summary>
    /// PlayerController에서 호출하여 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController controller)
    {
        _controller = controller;

        Debug.Log($"[PlayerBehavior] Initialized - Controller: {_controller != null}");
    }
    #endregion


    #region Combat
    /// <summary>
    /// 기본 공격을 실행합니다.
    /// </summary>
    public void PerformAttack()
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasInputAuthority) return;
        if (_controller.IsDead) return;

        // TODO: 공격 로직 구현
        Debug.Log($"[PlayerBehavior] Performed attack");
        
        OnAttackPerformed?.Invoke();
    }

    /// <summary>
    /// 스킬을 사용합니다.
    /// </summary>
    public void UseSkill(int skillIndex)
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasInputAuthority) return;
        if (_controller.IsDead) return;

        // TODO: 스킬 로직 구현
        Debug.Log($"[PlayerBehavior] Used skill {skillIndex}");
        
        OnSkillUsed?.Invoke(skillIndex);
    }

    /// <summary>
    /// 대상에게 데미지를 입힙니다 (서버에서 처리).
    /// </summary>
    public void DealDamage(PlayerController targetController, float damage)
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasStateAuthority) return;
        if (targetController == null) return;

        // PlayerController를 통해 데미지 처리
        targetController.TakeDamage(damage, _controller.Object.InputAuthority);
        Debug.Log($"[PlayerBehavior] Dealt {damage} damage to target");
    }
    #endregion

    #region Interaction
    /// <summary>
    /// 오브젝트와 상호작용합니다.
    /// </summary>
    public void Interact()
    {
        if (_controller == null || _controller.Object == null) return;
        if (!_controller.Object.HasInputAuthority) return;
        if (_controller.IsDead) return;

        // TODO: 상호작용 로직 구현
        Debug.Log($"[PlayerBehavior] Interacted");
        
        OnInteracted?.Invoke();
    }
    #endregion
}
