using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 적을 스폰하고 스폰 후 사라집니다.
/// EnemyData를 기반으로 적을 생성합니다.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("Enemy Settings")]
    [Tooltip("스폰할 적 데이터")]
    [SerializeField] private EnemyData enemyData;
    
    [Tooltip("적 프리팹 (NetworkObject 컴포넌트 포함)")]
    [SerializeField] private NetworkPrefabRef enemyPrefab;

    [Header("Spawn Settings")]
    [Tooltip("스폰 지연 시간 (초)")]
    [SerializeField] private float spawnDelay = 0.5f;

    private NetworkRunner _runner;
    private bool _hasSpawned = false;

    void Start()
    {
        // NetworkRunner 찾기
        _runner = FusionManager.LocalRunner;
        
        if (_runner == null)
        {
            _runner = FindObjectOfType<NetworkRunner>();
        }

        if (_runner != null && _runner.IsRunning)
        {
            StartCoroutine(SpawnEnemyCoroutine());
        }
        else
        {
            // NetworkRunner가 아직 준비되지 않음 - StartCoroutine으로 계속 재시도
            StartCoroutine(WaitForRunnerAndSpawn());
        }
    }

    private IEnumerator WaitForRunnerAndSpawn()
    {
        while (_runner == null || !_runner.IsRunning)
        {
            yield return null;
            _runner = FusionManager.LocalRunner ?? FindObjectOfType<NetworkRunner>();
        }

        if (_runner.IsServer)
        {
            yield return SpawnEnemyCoroutine();
        }
    }

    private IEnumerator SpawnEnemyCoroutine()
    {
        if (_hasSpawned || enemyData == null) yield break;

        // 스폰 지연
        yield return new WaitForSeconds(spawnDelay);

        // 서버에서만 스폰
        if (_runner != null && _runner.IsServer)
        {
            SpawnEnemy();
        }

        _hasSpawned = true;
    }

    private void SpawnEnemy()
    {
        if (enemyData == null)
        {
            Debug.LogError("[EnemySpawner] EnemyData가 설정되지 않았습니다!");
            return;
        }

        if (enemyPrefab.IsValid == false)
        {
            Debug.LogError("[EnemySpawner] EnemyPrefab이 설정되지 않았습니다!");
            return;
        }

        Vector2 spawnPosition = transform.position;

        // 적 스폰
        NetworkObject enemyObject = _runner.Spawn(
            enemyPrefab,
            spawnPosition,
            Quaternion.identity,
            null, // InputAuthority는 없음
            (runner, obj) =>
            {
                if (obj.TryGetComponent(out EnemyController controller))
                {
                    // EnemyService에서 EnemyIndex 찾기
                    int enemyIndex = FindEnemyIndex(enemyData);
                    if (enemyIndex >= 0)
                    {
                        controller.SetEnemyIndex(enemyIndex);
                        Debug.Log($"[EnemySpawner] Enemy spawned at {spawnPosition} with EnemyIndex: {enemyIndex}");
                    }
                    else
                    {
                        Debug.LogWarning($"[EnemySpawner] EnemyData가 EnemyService에 등록되지 않았습니다!");
                    }
                }
            }
        );

        // 스폰 후 EnemySpawner 오브젝트 비활성화 (스폰 포인트는 보이지 않게)
        gameObject.SetActive(false);
    }

    /// <summary>
    /// EnemyService에서 해당 EnemyData의 인덱스를 찾습니다.
    /// </summary>
    private int FindEnemyIndex(EnemyData data)
    {
        if (GameDataManager.Instance == null) return -1;

        var allEnemies = GameDataManager.Instance.EnemyService.GetAllEnemies();
        for (int i = 0; i < allEnemies.Count; i++)
        {
            if (allEnemies[i] == data)
            {
                return i;
            }
        }

        return -1;
    }

    #if UNITY_EDITOR
    /// <summary>
    /// 에디터에서 적 위치를 시각화합니다.
    /// </summary>
    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
    #endif
}
