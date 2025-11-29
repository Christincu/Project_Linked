using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// Collect 타입 목표 오브젝트의 네트워크용 로직을 담당합니다.
/// 서버(또는 StateAuthority)에서만 목표 달성을 처리하고, MainGameManager에 진행도를 보고합니다.
/// </summary>
public class GoalObject : NetworkBehaviour
{
    [Header("Goal Settings")]
    [Tooltip("수집 시 오브젝트를 즉시 파괴/디스폰할지 여부")]
    [SerializeField] private bool _destroyOnCollect = true;

    // 이 목표가 속한 웨이브 데이터 (서버에서만 사용)
    private WaveData _waveData;
    private bool _isCollected = false;

    /// <summary>
    /// GoalSpawner에서 생성 직후 호출하여 이 목표가 속한 WaveData를 설정합니다.
    /// (서버에서만 의미 있음)
    /// </summary>
    public void Initialize(WaveData waveData)
    {
        _waveData = waveData;
    }

    public override void Spawned()
    {
        base.Spawned();
        
        // Fusion Physics 시뮬레이션에 포함시킴 (NetworkRigidbody2D가 있을 때 필요)
        // Kinematic 오브젝트이지만 클라이언트 예측을 위해 시뮬레이션에 포함해야 함
        if (Runner != null && Object != null)
        {
            Runner.SetIsSimulated(Object, true);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 네트워크: StateAuthority(보통 서버)에서만 처리
        if (Object != null && !Object.HasStateAuthority) return;
        if (_isCollected) return;

        // 플레이어 감시용 DetectionTrigger는 무시 (적 감시용 트리거)
        if (other.GetComponent<PlayerDetectionTrigger>() != null) return;

        // 플레이어와의 충돌만 처리
        var player = other.GetComponent<PlayerController>() ??
                     other.GetComponentInParent<PlayerController>();
        if (player == null && other.attachedRigidbody != null)
        {
            player = other.attachedRigidbody.GetComponent<PlayerController>();
        }
        if (player == null) return;

        // 목표 달성 처리
        _isCollected = true;

        if (_waveData != null && MainGameManager.Instance != null)
        {
            MainGameManager.Instance.AddWaveGoalProgress(_waveData, 1);
        }

        // 오브젝트 제거 (네트워크/로컬 상황에 따라 처리)
        if (Object != null && Object.IsValid && Runner != null)
        {
            // 네트워크 오브젝트인 경우 디스폰
            Runner.Despawn(Object);
        }
        else if (_destroyOnCollect)
        {
            // 일반 GameObject인 경우 파괴
            Destroy(gameObject);
        }
    }
}

