using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어 효과 타입 정의
/// </summary>
public enum EffectType
{
    MoveSpeed,      // 이동속도
    AttackDamage,   // 공격력
    Defense,        // 방어력
    // 향후 확장 가능
}

/// <summary>
/// 플레이어 효과 데이터 구조
/// 서버에서만 관리되며, 계산된 결과(이동속도 등)만 네트워크로 동기화됩니다.
/// </summary>
[System.Serializable]
public struct PlayerEffectData
{
    public EffectType type;       // 효과 타입
    public float value;           // 퍼센테이지 (1.0 = 100%, 1.5 = 150%, 0.7 = 70%)
    public float duration;        // 지속 시간 (초)
    public int stackCount;        // 스택 수
    public int effectId;          // 고유 ID (중복 방지용)
    public TickTimer timer;       // 지속 시간 타이머
}

/// <summary>
/// 플레이어 효과를 관리하는 컴포넌트
/// 이동속도, 공격력 등 다양한 효과를 퍼센테이지로 적용합니다.
/// </summary>
public class PlayerEffectManager : MonoBehaviour
{
    #region Private Fields
    private PlayerController _controller;
    private List<PlayerEffectData> _activeEffects = new List<PlayerEffectData>();
    private int _nextEffectId = 1;
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
        _activeEffects.Clear();
        _nextEffectId = 1;
    }
    #endregion

    #region Effect Management
    /// <summary>
    /// 효과를 추가합니다. (State Authority에서만 실행)
    /// </summary>
    /// <param name="type">효과 타입</param>
    /// <param name="value">퍼센테이지 값 (1.0 = 100%, 1.5 = 150%, 0.7 = 70%)</param>
    /// <param name="duration">지속 시간 (초)</param>
    /// <param name="stackable">스택 가능 여부 (기본값: true)</param>
    /// <returns>추가된 효과의 ID</returns>
    public int AddEffect(EffectType type, float value, float duration, bool stackable = true)
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority)
        {
            Debug.LogWarning("[PlayerEffectManager] AddEffect can only be called on State Authority");
            return -1;
        }

        if (_controller.Runner == null)
        {
            Debug.LogWarning("[PlayerEffectManager] Runner is null");
            return -1;
        }

        // 스택 가능한 효과인 경우 기존 효과와 합산
        if (stackable)
        {
            // 같은 타입의 효과가 있으면 스택
            for (int i = 0; i < _activeEffects.Count; i++)
            {
                if (_activeEffects[i].type == type)
                {
                    var existingEffect = _activeEffects[i];
                    existingEffect.stackCount++;
                    existingEffect.value *= value; // 곱셈 방식으로 스택
                    existingEffect.timer = TickTimer.CreateFromSeconds(_controller.Runner, duration);
                    _activeEffects[i] = existingEffect;
                    
                    Debug.Log($"[PlayerEffectManager] Stacked effect {type}: {value} (Total stacks: {existingEffect.stackCount}, Total value: {existingEffect.value})");
                    return existingEffect.effectId;
                }
            }
        }

        // 새 효과 추가
        PlayerEffectData newEffect = new PlayerEffectData
        {
            type = type,
            value = value,
            duration = duration,
            stackCount = 1,
            effectId = _nextEffectId++,
            timer = TickTimer.CreateFromSeconds(_controller.Runner, duration)
        };

        _activeEffects.Add(newEffect);
        Debug.Log($"[PlayerEffectManager] Added effect {type}: {value} ({duration}s)");

        return newEffect.effectId;
    }

    /// <summary>
    /// 효과를 제거합니다. (State Authority에서만 실행)
    /// </summary>
    /// <param name="effectId">제거할 효과의 ID</param>
    public void RemoveEffect(int effectId)
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority)
        {
            Debug.LogWarning("[PlayerEffectManager] RemoveEffect can only be called on State Authority");
            return;
        }

        for (int i = _activeEffects.Count - 1; i >= 0; i--)
        {
            if (_activeEffects[i].effectId == effectId)
            {
                var effect = _activeEffects[i];
                Debug.Log($"[PlayerEffectManager] Removed effect {effect.type} (ID: {effectId})");
                _activeEffects.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>
    /// 특정 타입의 모든 효과를 제거합니다. (State Authority에서만 실행)
    /// </summary>
    /// <param name="type">제거할 효과 타입</param>
    public void RemoveEffectsByType(EffectType type)
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority)
        {
            Debug.LogWarning("[PlayerEffectManager] RemoveEffectsByType can only be called on State Authority");
            return;
        }

        for (int i = _activeEffects.Count - 1; i >= 0; i--)
        {
            if (_activeEffects[i].type == type)
            {
                _activeEffects.RemoveAt(i);
            }
        }

        Debug.Log($"[PlayerEffectManager] Removed all effects of type {type}");
    }

    /// <summary>
    /// 모든 효과를 제거합니다. (State Authority에서만 실행)
    /// </summary>
    public void ClearAllEffects()
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority)
        {
            Debug.LogWarning("[PlayerEffectManager] ClearAllEffects can only be called on State Authority");
            return;
        }

        _activeEffects.Clear();
        Debug.Log("[PlayerEffectManager] Cleared all effects");
    }

    /// <summary>
    /// 만료된 효과를 제거합니다. (FixedUpdateNetwork에서 호출)
    /// </summary>
    public void UpdateEffects()
    {
        if (_controller == null || _controller.Object == null || !_controller.Object.HasStateAuthority)
        {
            return;
        }

        if (_controller.Runner == null) return;

        for (int i = _activeEffects.Count - 1; i >= 0; i--)
        {
            var effect = _activeEffects[i];
            if (effect.timer.Expired(_controller.Runner))
            {
                Debug.Log($"[PlayerEffectManager] Effect {effect.type} expired (ID: {effect.effectId})");
                _activeEffects.RemoveAt(i);
            }
        }
    }
    #endregion

    #region Effect Getters
    /// <summary>
    /// 이동속도 배율을 가져옵니다. (모든 MoveSpeed 효과를 곱셈으로 합산)
    /// </summary>
    /// <returns>이동속도 배율 (1.0 = 기본값)</returns>
    public float GetMoveSpeedMultiplier()
    {
        float multiplier = 1.0f;

        foreach (var effect in _activeEffects)
        {
            if (effect.type == EffectType.MoveSpeed)
            {
                multiplier *= effect.value;
            }
        }

        return multiplier;
    }

    /// <summary>
    /// 공격력 배율을 가져옵니다.
    /// </summary>
    /// <returns>공격력 배율 (1.0 = 기본값)</returns>
    public float GetAttackDamageMultiplier()
    {
        float multiplier = 1.0f;

        foreach (var effect in _activeEffects)
        {
            if (effect.type == EffectType.AttackDamage)
            {
                multiplier *= effect.value;
            }
        }

        return multiplier;
    }

    /// <summary>
    /// 방어력 배율을 가져옵니다.
    /// </summary>
    /// <returns>방어력 배율 (1.0 = 기본값)</returns>
    public float GetDefenseMultiplier()
    {
        float multiplier = 1.0f;

        foreach (var effect in _activeEffects)
        {
            if (effect.type == EffectType.Defense)
            {
                multiplier *= effect.value;
            }
        }

        return multiplier;
    }

    /// <summary>
    /// 특정 타입의 효과가 있는지 확인합니다.
    /// </summary>
    /// <param name="type">확인할 효과 타입</param>
    /// <returns>효과 존재 여부</returns>
    public bool HasEffect(EffectType type)
    {
        foreach (var effect in _activeEffects)
        {
            if (effect.type == type)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 활성화된 효과 목록을 가져옵니다.
    /// </summary>
    /// <returns>활성화된 효과 목록 (읽기 전용)</returns>
    public IReadOnlyList<PlayerEffectData> GetActiveEffects()
    {
        return _activeEffects.AsReadOnly();
    }
    #endregion
}

