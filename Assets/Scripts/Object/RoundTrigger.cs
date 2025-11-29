using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 라운드 트리거 콜리더입니다. 플레이어가 이 콜리더에 진입하면 해당 라운드의 웨이브가 시작됩니다.
/// </summary>
public class RoundTrigger : MonoBehaviour
{
    [Header("Round Settings")]
    [Tooltip("이 트리거가 활성화할 라운드 인덱스 (StageData의 roundDataList 인덱스)")]
    [SerializeField] private int _roundIndex = 0;
    
    [Tooltip("한 번만 트리거되도록 할지 여부")]
    [SerializeField] private bool _triggerOnce = true;

    [Header("Enemy Spawners")]
    [Tooltip("이 라운드에서 사용할 EnemySpawner 리스트 (EnemySpawnData.spawnerIndex는 이 리스트의 인덱스를 참조)")]
    [SerializeField] private List<EnemySpawner> _enemySpawners = new List<EnemySpawner>();

    [Header("Goal Spawners")]
    [Tooltip("이 라운드에서 사용할 GoalSpawner 리스트 (WaveData.goalSpawnDataList의 GoalSpawnData.spawnerIndex는 이 리스트의 인덱스를 참조)")]
    [SerializeField] private List<GoalSpawner> _goalSpawners = new List<GoalSpawner>();

    [Header("Doors")]
    [Tooltip("이 라운드에서 제어할 문 컨트롤러들 (RoundDoorNetworkController)")]
    [SerializeField] private List<RoundDoorNetworkController> _doorObjects = new List<RoundDoorNetworkController>();

    private bool _hasTriggered = false;
    private readonly HashSet<PlayerController> _playersInside = new HashSet<PlayerController>();
    
    /// <summary>
    /// 이 트리거의 라운드 인덱스를 반환합니다.
    /// </summary>
    public int RoundIndex => _roundIndex;
    
    /// <summary>
    /// 이 트리거의 EnemySpawner 리스트를 반환합니다.
    /// </summary>
    public List<EnemySpawner> EnemySpawners => _enemySpawners;

    /// <summary>
    /// 이 트리거의 GoalSpawner 리스트를 반환합니다.
    /// </summary>
    public List<GoalSpawner> GoalSpawners => _goalSpawners;

    /// <summary>
    /// 이 트리거가 제어하는 문 컨트롤러 리스트를 반환합니다.
    /// </summary>
    public List<RoundDoorNetworkController> DoorObjects => _doorObjects;
    
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 서버(또는 호스트)에서만 라운드 시작 로직 처리
        if (FusionManager.LocalRunner != null && !FusionManager.LocalRunner.IsServer)
        {
            return;
        }

        // 이미 트리거되었고 한 번만 트리거되도록 설정된 경우 무시
        if (_hasTriggered && _triggerOnce)
        {
            return;
        }
        
        // 플레이어인지 확인
        if (collision.CompareTag("Player"))
        {
            // 플레이어 컨트롤러 찾기
            PlayerController player = collision.GetComponent<PlayerController>() ??
                                      collision.GetComponentInParent<PlayerController>();
            if (player != null && !player.IsDead)
            {
                _playersInside.Add(player);
            }

            // 모든 살아있는 플레이어가 이 트리거 안에 있을 때만 라운드 시작
            if (!_hasTriggered && AreAllAlivePlayersInside())
            {
                if (MainGameManager.Instance != null)
                {
                    // 라운드 시작
                    MainGameManager.Instance.StartRound(_roundIndex, _enemySpawners, _goalSpawners);

                    // 문 닫기: 네트워크 컨트롤러를 직접 참조하여 Networked 상태로 닫기
                    foreach (var door in _doorObjects)
                    {
                        if (door == null) continue;

                        door.SetClosed(true);
                    }

                    _hasTriggered = true;
                    Debug.Log($"[RoundTrigger] Round {_roundIndex} started (EnemySpawners: {_enemySpawners.Count}, GoalSpawners: {_goalSpawners.Count})");
                }
                else
                {
                    Debug.LogWarning($"[RoundTrigger] MainGameManager.Instance is null! Cannot start round {_roundIndex}");
                }
            }
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;

        PlayerController player = collision.GetComponent<PlayerController>() ??
                                  collision.GetComponentInParent<PlayerController>();
        if (player != null)
        {
            _playersInside.Remove(player);
        }
    }

    /// <summary>
    /// 현재 씬의 모든 살아있는 플레이어가 이 트리거 안에 있는지 확인합니다.
    /// </summary>
    private bool AreAllAlivePlayersInside()
    {
        if (MainGameManager.Instance == null) return false;

        List<PlayerController> players = MainGameManager.Instance.GetAllPlayers();
        if (players == null || players.Count == 0) return false;

        foreach (var player in players)
        {
            if (player == null || player.IsDead) continue;
            if (!_playersInside.Contains(player))
            {
                return false;
            }
        }

        return true;
    }
    
    /// <summary>
    /// 트리거 상태를 리셋합니다 (테스트용).
    /// </summary>
    public void ResetTrigger()
    {
        _hasTriggered = false;
        _playersInside.Clear();
    }
}
