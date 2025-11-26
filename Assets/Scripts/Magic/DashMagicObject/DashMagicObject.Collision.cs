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
    /// </summary>
    private void DetectCollisions()
    {
        if (!_attackCollider.enabled) return;
        if (_owner == null || Runner == null) return;
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
        float healthBefore = 0f;
        float healthAfter = 0f;
        
        try
        {
            // 데미지 계산
            damage = CalculateDamage();
            
            // 적에게 데미지 적용
            healthBefore = enemy.State.CurrentHealth;
            enemy.State.TakeDamage(damage);
            healthAfter = enemy.State.CurrentHealth;
            
            Debug.Log($"[DashHit] {_owner.name} - Applied {damage} damage to {enemy.name} " +
                     $"(Health: {healthBefore} -> {healthAfter}/{enemy.State.MaxHealth}, " +
                     $"Actual Damage: {healthBefore - healthAfter})");
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
        
        // 1. 넉백 방향 결정
        Vector2 knockbackDirection = _owner.DashVelocity.normalized;
        
        if (knockbackDirection.magnitude < 0.1f)
        {
            knockbackDirection = _owner.DashLastInputDirection.normalized;
            
            if (knockbackDirection.magnitude < 0.1f)
            {
                Vector2 toEnemy = (enemy.transform.position - transform.position);
                if (toEnemy.magnitude > 0.1f)
                {
                    knockbackDirection = toEnemy.normalized;
                }
                else
                {
                    knockbackDirection = Vector2.up;
                }
            }
        }
        
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
            Vector2 velocityBefore = networkRb.Rigidbody.velocity;
            
            networkRb.Rigidbody.velocity = finalDirection * knockbackForce;
            enemy.KnockbackTimer = TickTimer.CreateFromSeconds(enemy.Runner, _dashData.enemyKnockbackDuration);
            
            Debug.Log($"[DashKnockback] {_owner.name} - Applied knockback to {enemy.name}: " +
                     $"Direction={finalDirection}, Force={knockbackForce}, " +
                     $"Velocity: {velocityBefore} -> {networkRb.Rigidbody.velocity}");
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
        Debug.Log($"[DashMagicObject] {_owner.name} - HandlePlayerCollision called with {otherPlayer?.name ?? "null"}");
        
        if (otherPlayer == null || otherPlayer == _owner) return;
        if (!_owner.HasDashSkill || !otherPlayer.HasDashSkill) return;
        if (!_owner.DashIsMoving || !otherPlayer.DashIsMoving) return;
        
        // 최근 충돌 체크
        if (_recentHitPlayers.Contains(otherPlayer))
        {
            Debug.Log($"[DashMagicObject] {_owner.name} - Recent collision with {otherPlayer.name}, ignoring");
            return;
        }
        
        Debug.Log($"[DashMagicObject] {_owner.name} collided with {otherPlayer.name} - All checks passed, processing collision");
        
        // 정면 판정 확인
        bool isFrontCollision = CheckFrontCollision(otherPlayer);
        
        if (isFrontCollision)
        {
            // 정면 충돌: 강화 및 정지→튕김 시퀀스 시작
            Debug.Log($"[DashCollision] {_owner.name} - Perfect Hit! Freezing for {_dashData.playerCollisionFreezeDuration}s...");
            
            // --- A. 강화 적용 ---
            HandleEnhancement(otherPlayer);
            
            // --- B. 정지(Freeze) 시작 ---
            _owner.DashStunTimer = TickTimer.CreateFromSeconds(Runner, _dashData.playerCollisionFreezeDuration);
            _owner.DashVelocity = Vector2.zero;
            _owner.DashIsMoving = false;
            UpdateRigidbodyVelocity(Vector2.zero);
            
            // --- C. 반동(Recoil) 예약 ---
            Vector2 recoilDir = (transform.position - otherPlayer.transform.position).normalized;
            if (recoilDir.magnitude < 0.1f)
            {
                recoilDir = -_owner.DashLastInputDirection.normalized;
                if (recoilDir.magnitude < 0.1f)
                {
                    recoilDir = Vector2.up;
                }
            }
            _owner.DashPendingRecoilDirection = recoilDir;
            _owner.DashIsWaitingToRecoil = true;
            
            // --- D. 쿨다운 등록 ---
            _recentHitPlayers.Add(otherPlayer);
            StartCoroutine(RemoveFromRecentHitPlayers(otherPlayer, _dashData.playerCollisionFreezeDuration + 0.5f));
            
            // 상대방도 동일한 처리
            if (otherPlayer.DashMagicObject != null)
            {
                otherPlayer.DashMagicObject.OnPlayerCollision(_owner);
            }
        }
        else
        {
            // 잘못된 충돌
            Debug.LogWarning($"[DashCollision] {_owner.name} - Wrong collision with {otherPlayer.name}, skill ending and stun applied");
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
        
        Vector2 recoilDir = (transform.position - otherPlayer.transform.position).normalized;
        if (recoilDir.magnitude < 0.1f)
        {
            recoilDir = -_owner.DashLastInputDirection.normalized;
            if (recoilDir.magnitude < 0.1f)
            {
                recoilDir = Vector2.up;
            }
        }
        
        _owner.DashStunTimer = TickTimer.CreateFromSeconds(Runner, _dashData.playerCollisionFreezeDuration);
        _owner.DashVelocity = Vector2.zero;
        _owner.DashIsMoving = false;
        UpdateRigidbodyVelocity(Vector2.zero);
        
        _owner.DashPendingRecoilDirection = recoilDir;
        _owner.DashIsWaitingToRecoil = true;
        
        _recentHitPlayers.Add(otherPlayer);
        StartCoroutine(RemoveFromRecentHitPlayers(otherPlayer, _dashData.playerCollisionFreezeDuration + 0.5f));
    }
    
    /// <summary>
    /// 정면 충돌인지 확인합니다.
    /// </summary>
    private bool CheckFrontCollision(PlayerController otherPlayer)
    {
        if (_owner == null || otherPlayer == null || _dashData == null) return false;
        
        Vector2 toOther = (otherPlayer.transform.position - _owner.transform.position).normalized;
        Vector2 myForward = _owner.DashVelocity.normalized;

        if (myForward.sqrMagnitude < 0.01f)
        {
            myForward = _owner.DashLastInputDirection.normalized;
            
            if (myForward.sqrMagnitude < 0.01f)
            {
                myForward = new Vector2(_owner.ScaleX, 0).normalized;
            }
        }
        
        float dot = Vector2.Dot(myForward, toOther);
        float angleThreshold = _dashData.playerCollisionFrontAngle * 0.5f;
        float dotThreshold = Mathf.Cos(angleThreshold * Mathf.Deg2Rad);
        
        return dot >= dotThreshold;
    }
    
    /// <summary>
    /// 강화를 처리합니다.
    /// </summary>
    private void HandleEnhancement(PlayerController otherPlayer)
    {
        if (_owner == null || Runner == null) return;
        if (_owner.IsDashFinalEnhancement) return;
        
        int currentEnhancement = _owner.DashEnhancementCount;
        _owner.DashEnhancementCount = Mathf.Min(currentEnhancement + 1, _dashData.finalEnhancementCount);
        
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
        
        if (otherPlayer.DashMagicObject != null)
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
        
        _owner.DashEnhancementCount = Mathf.Min(otherPlayerEnhancement, _dashData.finalEnhancementCount);
        
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
        
        _recentHitPlayers.Add(otherPlayer);
        StartCoroutine(RemoveFromRecentHitPlayers(otherPlayer, _dashData.wrongCollisionStunDuration + 0.5f));
    }
    
    /// <summary>
    /// 반동을 실행합니다.
    /// </summary>
    private void ExecuteRecoil()
    {
        if (_owner == null || Runner == null) return;
        
        Debug.Log($"[DashRecoil] {_owner.name} - Launching Recoil! Direction: {_owner.DashPendingRecoilDirection}");
        
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

        Debug.Log($"[DashMagicObject] {_owner.name} - Calculated damage: {damage}");
        
        return damage;
    }
    #endregion
}

