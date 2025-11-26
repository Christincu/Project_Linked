using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 적의 상태 로직을 관리합니다.
/// 네트워크 변수는 EnemyController에 있으며, 이 클래스는 로직만 담당합니다.
/// </summary>
public class EnemyState : MonoBehaviour
{
    #region Private Fields
    private EnemyController _controller;
    private EnemyData _enemyData;
    #endregion

    #region Properties (EnemyController의 네트워크 변수 참조)
    public float CurrentHealth
    {
        get
        {
            // [안전성] NetworkObject가 완전히 스폰되었는지 확인
            if (_controller == null || _controller.Object == null) return 0;
            try
            {
                return _controller.CurrentHealth;
            }
            catch (System.InvalidOperationException)
            {
                // Spawned()가 호출되기 전이거나 디스폰된 경우
                return 0;
            }
        }
        set
        {
            if (_controller != null && _controller.Object != null)
            {
                try
                {
                    _controller.CurrentHealth = value;
                }
                catch (System.InvalidOperationException)
                {
                    // Spawned()가 호출되기 전이거나 디스폰된 경우 무시
                }
            }
        }
    }

    public float MaxHealth
    {
        get => _controller != null ? _controller.MaxHealth : 0;
        set { if (_controller != null) _controller.MaxHealth = value; }
    }

    public bool IsDead
    {
        get => _controller != null && _controller.IsDead;
        set { if (_controller != null) _controller.IsDead = value; }
    }

    public float HealthPercentage => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;
    #endregion

    #region Events
    public System.Action<float, float> OnHealthChanged; // (current, max)
    public System.Action OnDeath;
    public System.Action<float> OnDamageTaken; // (damage)
    #endregion

    #region Initialization
    /// <summary>
    /// EnemyController에서 호출하여 초기화합니다.
    /// </summary>
    public void Initialize(EnemyController controller, EnemyData enemyData)
    {
        _controller = controller;
        _enemyData = enemyData;
    }
    #endregion

    #region Health Management
    /// <summary>
    /// 적에게 데미지를 입힙니다.
    /// </summary>
    public void TakeDamage(float damage)
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority) return;
        if (IsDead) return;

        // 데미지 적용
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        
        // 이벤트 발생
        OnDamageTaken?.Invoke(damage);
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);

        // 체력이 0 이하면 사망 처리
        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// 적을 사망 처리합니다.
    /// </summary>
    private void Die()
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority) return;
        if (IsDead) return;

        IsDead = true;
        CurrentHealth = 0;
        
        // 이벤트 발생
        OnDeath?.Invoke();
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        
        // 적 오브젝트 삭제 (Fusion 네트워크 오브젝트)
        if (_controller.Runner != null && _controller.Object != null)
        {
            _controller.Runner.Despawn(_controller.Object);
            Debug.Log($"[EnemyState] {_controller.name} despawned after death");
        }
    }
    #endregion
}

