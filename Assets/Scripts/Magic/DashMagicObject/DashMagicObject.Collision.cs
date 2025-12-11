using System.Collections;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

/// <summary>
/// DashMagicObject의 충돌 처리 관련 로직
/// </summary>
public partial class DashMagicObject
{
    #region Collision Handling
    /// <summary>
    /// [핵심 개선] FixedUpdateNetwork 내에서 물리 충돌을 직접 감지합니다.
    /// 롤백 상황에서도 안전하며, 정확한 타이밍에 충돌을 처리합니다.
    /// State Authority에서만 실행됩니다.
    /// </summary>
    private void DetectCollisions()
    {
        // [중요] 충돌 처리는 State Authority에서만 실행
        if (_owner == null || !_owner.Object.HasStateAuthority) return;
        
        if (!_attackCollider.enabled) return;
        if (Runner == null) return;
        if (!_owner.DashIsMoving) return; // 정지 상태에서는 충돌 처리 안 함
        
        // 공격 범위 내의 모든 콜라이더 감지
        // State Authority에서만 실행되므로 Physics2D 사용이 안전함
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(
            transform.position,
            attackColliderRadius,
            enemyLayer | playerLayer // 적과 플레이어 모두 감지
        );
        
        if (hitColliders == null || hitColliders.Length == 0) return;
        
        foreach (var hit in hitColliders)
        {
            if (hit == null) continue;

            // 거리 기반 이중 체크 (물리 엔진 오차 보정)
            float rawDistance = Vector3.Distance(transform.position, hit.transform.position);
            if (rawDistance > attackColliderRadius + COLLISION_DISTANCE_TOLERANCE)
            {
                continue;
            }
            
            // 1. 적 충돌 처리
            var enemy = hit.GetComponent<EnemyController>();
            if (enemy == null)
            {
                enemy = hit.GetComponentInParent<EnemyController>();
            }
            if (enemy == null && hit.attachedRigidbody != null)
            {
                enemy = hit.attachedRigidbody.GetComponent<EnemyController>();
            }
            
            if (enemy != null && !enemy.IsDead && enemy.Object != null)
            {
                if (_enemyCollisionCooldowns.TryGetValue(enemy.Object.Id, out TickTimer cooldown))
                {
                    if (cooldown.IsRunning && !cooldown.ExpiredOrNotRunning(Runner))
                    {
                        // 쿨다운 중: 플레이어가 피해를 입음
                        // [최종 강화 무적] 최종 강화 상태에서는 무적 (PlayerState.TakeDamage에서 처리)
                        if (_owner.State != null)
                        {
                            _owner.State.TakeDamage(_dashData.baseDamage);
                        }
                        continue; // 아직 쿨다운 중임
                    }
                }

                HandleEnemyCollision(enemy);
                continue;
            }
            
            // 2. 플레이어 충돌 처리
            var otherPlayer = hit.GetComponent<PlayerController>();
            if (otherPlayer == null)
            {
                otherPlayer = hit.GetComponentInParent<PlayerController>();
            }
            if (otherPlayer == null && hit.attachedRigidbody != null)
            {
                otherPlayer = hit.attachedRigidbody.GetComponent<PlayerController>();
            }
            
            if (otherPlayer != null && otherPlayer != _owner && !otherPlayer.IsDead)
            {
                // 상대방도 돌진 스킬 사용 중인지 확인
                if (otherPlayer.HasDashSkill && otherPlayer.DashIsMoving)
                {
                    HandlePlayerCollision(otherPlayer);
                }
            }
        }
    }
    
    /// <summary>
    /// 적 충돌을 처리합니다.
    /// </summary>
    private void HandleEnemyCollision(EnemyController enemy)
    {
        if (enemy == null || enemy.IsDead || enemy.Object == null) return;
        if (_owner == null || !_owner.HasDashSkill) return;
        if (!_owner.DashIsMoving) return; // 정지 상태에서는 충돌 처리 안 함
        
        // enemy.Runner 확인
        if (enemy.Runner == null)
        {
            Debug.LogWarning($"[DashHit] {_owner.name} - Enemy {enemy.name} has no Runner!");
            return;
        }
        
        // enemy.State 확인
        if (enemy.State == null)
        {
            Debug.LogWarning($"[DashHit] {_owner.name} - Enemy {enemy.name} has no State component!");
            return;
        }
        
        // [중요] NetworkObject가 완전히 스폰되었는지 확인
        float damage = 0f;
        
        try
        {
            // 데미지 계산
            damage = CalculateDamage();
            
            // 적에게 데미지 적용
            enemy.State.TakeDamage(damage);
        }
        catch (System.InvalidOperationException ex)
        {
            Debug.LogWarning($"[DashHit] {_owner.name} - Cannot access enemy {enemy.name} network properties: {ex.Message}");
            return;
        }
        
        // 적 넉백
        ApplyEnemyKnockback(enemy);
        
        // 지속 시간 감소
        if (_owner.Object.HasStateAuthority)
        {
            float remainingTime = _owner.DashSkillTimer.RemainingTime(Runner) ?? 0f;
            float newTime = Mathf.Max(0f, remainingTime - _dashData.durationReductionOnHit);
            _owner.DashSkillTimer = TickTimer.CreateFromSeconds(Runner, newTime);
        }
        
        // 재충돌 방지 타이머 설정
        if (_owner.Object.HasStateAuthority)
        {
            _enemyCollisionCooldowns[enemy.Object.Id] = TickTimer.CreateFromSeconds(Runner, _dashData.enemyCollisionCooldown);
        }
    }
    
    /// <summary>
    /// 적에게 넉백을 적용합니다.
    /// </summary>
    private void ApplyEnemyKnockback(EnemyController enemy)
    {
        if (enemy == null || _owner == null || Runner == null) return;
        
        if (enemy.Runner == null)
        {
            Debug.LogWarning($"[DashKnockback] {_owner.name} - Enemy {enemy.name} has no Runner!");
            return;
        }
        
        // 넉백 방향 결정
        Vector2 knockbackDirection = GetKnockbackDirection(enemy.transform.position);
        
        // 2. 각도 비틀기 (랜덤성 추가)
        float baseAngle = Mathf.Atan2(knockbackDirection.y, knockbackDirection.x) * Mathf.Rad2Deg;
        float randomAngleOffset = Random.Range(-_dashData.enemyKnockbackAngleRange, _dashData.enemyKnockbackAngleRange);
        float finalAngleRad = (baseAngle + randomAngleOffset) * Mathf.Deg2Rad;
        Vector2 finalDirection = new Vector2(Mathf.Cos(finalAngleRad), Mathf.Sin(finalAngleRad));
        
        // 3. 적에게 힘 가하기
        var networkRb = enemy.GetComponent<NetworkRigidbody2D>();
        if (networkRb != null && networkRb.Rigidbody != null)
        {
            float knockbackForce = _dashData.enemyKnockbackForce;
            
            networkRb.Rigidbody.velocity = finalDirection * knockbackForce;
            enemy.KnockbackTimer = TickTimer.CreateFromSeconds(enemy.Runner, _dashData.enemyKnockbackDuration);
        }
        else
        {
            Debug.LogWarning($"[DashKnockback] {_owner.name} - Enemy {enemy.name} has no NetworkRigidbody2D component!");
        }
    }
    
    /// <summary>
    /// 플레이어 충돌을 처리합니다.
    /// </summary>
    private void HandlePlayerCollision(PlayerController otherPlayer)
    {
        if (otherPlayer == null || otherPlayer == _owner) return;
        if (!_owner.HasDashSkill || !otherPlayer.HasDashSkill) return;
        if (!_owner.DashIsMoving || !otherPlayer.DashIsMoving) return;
        
        // 최근 충돌 체크
        if (_recentHitPlayers.Contains(otherPlayer))
        {
            return;
        }
        
        // 정면 판정 확인
        string frontReason;
        bool isFrontCollision = CheckFrontCollision(otherPlayer, out frontReason);
        
        if (isFrontCollision)
        {
            // 정면 충돌: 강화 및 정지→튕김 시퀀스 시작
            // --- A. 강화 적용 ---
            HandleEnhancement(otherPlayer);
            
            // --- B. 정지(Freeze) 시작 ---
            float totalFreeze = _dashData.playerCollisionFreezeDuration + GRACE_PERIOD_AFTER_STUN;
            StartFreezeAndRecoilSequence(totalFreeze, GetRecoilDirection(otherPlayer.transform.position));
            
            // --- C. 쿨다운 등록 ---
            RegisterPlayerCollisionCooldown(otherPlayer, totalFreeze);
            
            // 상대방도 동일한 처리
            if (otherPlayer.DashMagicObject != null)
            {
                otherPlayer.DashMagicObject.OnPlayerCollision(_owner);
            }
        }
        else
        {
            // 잘못된 충돌
            HandleWrongCollision();
            
            if (otherPlayer.DashMagicObject != null)
            {
                otherPlayer.DashMagicObject.OnWrongCollision(_owner);
            }
        }
    }
    
    /// <summary>
    /// 다른 플레이어의 DashMagicObject에서 호출됩니다.
    /// 상대방도 동일한 정지→튕김 시퀀스를 시작합니다.
    /// </summary>
    public void OnPlayerCollision(PlayerController otherPlayer)
    {
        if (otherPlayer == null || _owner == null || Runner == null) return;
        if (!_owner.Object.HasStateAuthority) return;
        if (_recentHitPlayers.Contains(otherPlayer)) return;
        
        float totalFreeze = _dashData.playerCollisionFreezeDuration + GRACE_PERIOD_AFTER_STUN;
        StartFreezeAndRecoilSequence(totalFreeze, GetRecoilDirection(otherPlayer.transform.position));
        RegisterPlayerCollisionCooldown(otherPlayer, totalFreeze);
    }
    
    /// <summary>
    /// [수정됨] 정면 충돌 판정 + 실패 이유를 반환합니다.
    /// (거리 체크는 Physics 충돌에 맡기고 별도 처리하지 않음)
    /// </summary>
    private bool CheckFrontCollision(PlayerController otherPlayer, out string reason)
    {
        reason = "Unknown";

        if (_owner == null || otherPlayer == null || _dashData == null)
        {
            reason = "Owner/Other/DashData is null";
            return false;
        }
        
        // 1. 방향 벡터 계산 (입력 -> 속도 -> ScaleX 우선순위)
        Vector2 myForward = GetForwardDirection(_owner);
        Vector2 otherForward = GetForwardDirection(otherPlayer);

        // 2. Heading Check (서로 마주보고 있는지)
        // 두 벡터의 내적(Dot)이 -1에 가까울수록 정면 충돌
        float headingDot = Vector2.Dot(myForward, otherForward);
        
        // 기준값을 0.0f 로 완화 (직각까지 허용)
        float headingThreshold = 0.0f;

        if (headingDot > headingThreshold) 
        {
            reason = $"Heading failed: dot={headingDot:F2} > threshold={headingThreshold:F2}";
            return false; 
        }

        // 3. FOV Check (상대방이 내 시야각 안에 있는지)
        Vector2 toOther = (otherPlayer.transform.position - _owner.transform.position).normalized;
        float fovDot = Vector2.Dot(myForward, toOther);
        
        float angleThreshold = _dashData.playerCollisionFrontAngle * 0.5f;
        float fovThreshold = Mathf.Cos(angleThreshold * Mathf.Deg2Rad);
        
        bool inFov = fovDot >= fovThreshold;

        if (!inFov)
        {
             reason = $"FOV failed: dot={fovDot:F2} < threshold={fovThreshold:F2}";
             return false;
        }

        reason = "OK";
        return true;
    }

    /// <summary>
    /// 플레이어의 전방 방향 벡터를 계산하는 헬퍼 메서드
    /// [가속도/속도 기반] 실제 Rigidbody 이동 방향을 우선으로 사용합니다.
    /// </summary>
    private Vector2 GetForwardDirection(PlayerController p)
    {
        if (p == null) return Vector2.right;

        // 1) 실제 물리 속도 기반 (가속도로 움직인 결과)
        if (p.Movement != null)
        {
            Vector2 vel = p.Movement.GetVelocity();
            if (vel.sqrMagnitude > 0.001f)
            {
                return vel.normalized;
            }
        }

        // 2) 대시 전용 방향들
        if (p.DashVelocity.sqrMagnitude > 0.001f)
            return p.DashVelocity.normalized;

        if (p.DashLastInputDirection.sqrMagnitude > 0.001f)
            return p.DashLastInputDirection.normalized;

        // 3) 마지막 fallback: 스프라이트 좌우 플립 값
        return new Vector2(p.ScaleX, 0).normalized;
    }
    
    /// <summary>
    /// 강화를 처리합니다.
    /// </summary>
    private void HandleEnhancement(PlayerController otherPlayer)
    {
        if (_owner == null || Runner == null) return;
        if (_owner.IsDashFinalEnhancement) return;
        
        int newEnhancementCount = Mathf.Min(_owner.DashEnhancementCount + 1, _dashData.finalEnhancementCount);
        ApplyEnhancementInternal(newEnhancementCount);
        
        // 상대방에게도 동일한 강화 적용
        if (otherPlayer?.DashMagicObject != null)
        {
            otherPlayer.DashMagicObject.ApplyEnhancement(_owner.DashEnhancementCount);
        }
    }
    
    /// <summary>
    /// 다른 플레이어의 DashMagicObject에서 호출됩니다.
    /// </summary>
    public void ApplyEnhancement(int otherPlayerEnhancement)
    {
        if (_owner == null || Runner == null) return;
        if (_owner.IsDashFinalEnhancement) return;
        
        int newEnhancementCount = Mathf.Min(otherPlayerEnhancement, _dashData.finalEnhancementCount);
        ApplyEnhancementInternal(newEnhancementCount);
    }
    
    /// <summary>
    /// 강화를 내부적으로 적용하는 공통 로직입니다.
    /// </summary>
    private void ApplyEnhancementInternal(int newEnhancementCount)
    {
        if (_owner == null || Runner == null || _dashData == null) return;
        
        _owner.DashEnhancementCount = newEnhancementCount;
        
        if (_owner.DashEnhancementCount >= _dashData.finalEnhancementCount)
        {
            _owner.IsDashFinalEnhancement = true;
            _owner.DashSkillTimer = TickTimer.CreateFromSeconds(Runner, _dashData.finalEnhancementDuration);
        }
        else
        {
            float remainingTime = _owner.DashSkillTimer.RemainingTime(Runner) ?? 0f;
            float newTime = Mathf.Min(
                _dashData.maxDuration,
                remainingTime + _dashData.durationIncreasePerEnhancement
            );
            _owner.DashSkillTimer = TickTimer.CreateFromSeconds(Runner, newTime);
        }
        
        UpdateBarrierSprite();
    }
    
    /// <summary>
    /// 넉백 방향을 계산합니다.
    /// </summary>
    private Vector2 GetKnockbackDirection(Vector3 targetPosition)
    {
        // 1순위: 현재 대시 속도 방향
        if (_owner.DashVelocity.sqrMagnitude > MIN_VELOCITY_SQR)
        {
            return _owner.DashVelocity.normalized;
        }
        
        // 2순위: 마지막 입력 방향
        if (_owner.DashLastInputDirection.sqrMagnitude > MIN_VELOCITY_SQR)
        {
            return _owner.DashLastInputDirection.normalized;
        }
        
        // 3순위: 타겟 방향
        Vector2 toTarget = (targetPosition - transform.position);
        if (toTarget.sqrMagnitude > MIN_VELOCITY_SQR)
        {
            return toTarget.normalized;
        }
        
        // Fallback: 위 방향
        return Vector2.up;
    }
    
    /// <summary>
    /// 반동 방향을 계산합니다.
    /// </summary>
    private Vector2 GetRecoilDirection(Vector3 otherPlayerPosition)
    {
        // 1순위: 상대 플레이어로부터 멀어지는 방향
        Vector2 recoilDir = (transform.position - otherPlayerPosition).normalized;
        if (recoilDir.sqrMagnitude > MIN_VELOCITY_SQR)
        {
            return recoilDir;
        }
        
        // 2순위: 마지막 입력 반대 방향
        Vector2 oppositeInput = -_owner.DashLastInputDirection.normalized;
        if (oppositeInput.sqrMagnitude > MIN_VELOCITY_SQR)
        {
            return oppositeInput;
        }
        
        // Fallback: 위 방향
        return Vector2.up;
    }
    
    /// <summary>
    /// 잘못된 충돌을 처리합니다.
    /// </summary>
    private void HandleWrongCollision()
    {
        if (_owner == null || Runner == null) return;
        
        _owner.HasDashSkill = false;
        _owner.DashSkillTimer = TickTimer.None;
        _owner.DashIsMoving = false;
        _owner.DashVelocity = Vector2.zero;
        _owner.DashPendingRecoilDirection = Vector2.zero;
        _owner.DashIsWaitingToRecoil = false;
        
        UpdateRigidbodyVelocity(Vector2.zero);
        
        if (_owner.EffectManager != null && _dashData != null)
        {
            _owner.EffectManager.AddEffect(EffectType.MoveSpeed, 0f, _dashData.wrongCollisionStunDuration, false);
        }
        
        _owner.DashMagicObject = null;
    }
    
    /// <summary>
    /// 다른 플레이어의 DashMagicObject에서 호출됩니다.
    /// 상대방도 동일한 잘못된 충돌 처리를 받습니다.
    /// </summary>
    public void OnWrongCollision(PlayerController otherPlayer)
    {
        if (otherPlayer == null || _owner == null || Runner == null) return;
        if (!_owner.Object.HasStateAuthority) return;
        if (_recentHitPlayers.Contains(otherPlayer)) return;
        
        HandleWrongCollision();
        
        RegisterPlayerCollisionCooldown(otherPlayer, _dashData.wrongCollisionStunDuration + GRACE_PERIOD_AFTER_STUN);
    }
    
    /// <summary>
    /// 반동을 실행합니다.
    /// </summary>
    private void ExecuteRecoil()
    {
        if (_owner == null || Runner == null) return;
        
        _owner.DashIsWaitingToRecoil = false;
        
        Vector2 launchVelocity = _owner.DashPendingRecoilDirection * _dashData.playerCollisionRecoilForce;
        _owner.DashVelocity = launchVelocity;
        _owner.DashIsMoving = true;
        UpdateRigidbodyVelocity(launchVelocity);
        
        if (_attackCollider != null)
        {
            _attackCollider.enabled = true;
        }
        
        float currentRemaining = _owner.DashSkillTimer.RemainingTime(Runner) ?? 0f;
        _owner.DashSkillTimer = TickTimer.CreateFromSeconds(Runner, currentRemaining + _dashData.playerCollisionRecoilTimeExtension);
        
        _owner.DashPendingRecoilDirection = Vector2.zero;
    }
    
    /// <summary>
    /// 데미지를 계산합니다.
    /// </summary>
    private float CalculateDamage()
    {
        if (_dashData == null) return 0f;
        
        float damage = _dashData.baseDamage;
        
        if (_owner.IsDashFinalEnhancement)
        {
            damage = _dashData.baseDamage + _dashData.finalEnhancementDamageBonus;
        }
        else
        {
            damage += _dashData.damageIncreasePerEnhancement * _owner.DashEnhancementCount;
        }
        
        return damage;
    }
    
    /// <summary>
    /// 정지 및 반동 시퀀스를 시작합니다.
    /// </summary>
    private void StartFreezeAndRecoilSequence(float freezeDuration, Vector2 recoilDirection)
    {
        if (_owner == null || Runner == null) return;
        
        _owner.DashStunTimer = TickTimer.CreateFromSeconds(Runner, freezeDuration);
        _owner.DashVelocity = Vector2.zero;
        _owner.DashIsMoving = false;
        _owner.DashPendingRecoilDirection = recoilDirection;
        _owner.DashIsWaitingToRecoil = true;
        UpdateRigidbodyVelocity(Vector2.zero);
    }
    
    /// <summary>
    /// 플레이어 충돌 쿨다운을 등록합니다.
    /// </summary>
    private void RegisterPlayerCollisionCooldown(PlayerController player, float duration)
    {
        if (player == null) return;
        
        _recentHitPlayers.Add(player);
        StartCoroutine(RemoveFromRecentHitPlayers(player, duration));
    }
    #endregion
}

