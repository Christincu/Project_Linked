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

    private bool _hasTriggered = false;
    
    /// <summary>
    /// 이 트리거의 라운드 인덱스를 반환합니다.
    /// </summary>
    public int RoundIndex => _roundIndex;
    
    /// <summary>
    /// 이 트리거의 EnemySpawner 리스트를 반환합니다.
    /// </summary>
    public List<EnemySpawner> EnemySpawners => _enemySpawners;
    
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 이미 트리거되었고 한 번만 트리거되도록 설정된 경우 무시
        if (_hasTriggered && _triggerOnce)
        {
            return;
        }
        
        // 플레이어인지 확인
        if (collision.CompareTag("Player"))
        {
            // MainGameManager에 라운드 시작 요청 (이 트리거의 스포너 리스트 전달)
            if (MainGameManager.Instance != null)
            {
                MainGameManager.Instance.StartRound(_roundIndex, _enemySpawners);
                _hasTriggered = true;
                Debug.Log($"[RoundTrigger] Round {_roundIndex} triggered by player (Spawners: {_enemySpawners.Count})");
            }
            else
            {
                Debug.LogWarning($"[RoundTrigger] MainGameManager.Instance is null! Cannot start round {_roundIndex}");
            }
        }
    }
    
    /// <summary>
    /// 트리거 상태를 리셋합니다 (테스트용).
    /// </summary>
    public void ResetTrigger()
    {
        _hasTriggered = false;
    }
}
