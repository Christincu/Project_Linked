using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// MainGameManager의 명령에 따라 StageData의 WaveData를 기반으로 적을 스폰합니다.
/// _spawnTransforms 중 랜덤 지점에 스폰합니다.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("Spawner Settings")]
    [Tooltip("스포너 인덱스 (StageData의 EnemySpawnData.spawnerIndex와 매칭)")]
    [SerializeField] private int _spawnerIndex = 0;
    
    [Tooltip("적 프리팹 (NetworkObject 컴포넌트 포함)")]
    [SerializeField] private NetworkPrefabRef _enemyPrefab;

    [Header("Spawn Points")]
    [Tooltip("스폰 가능한 위치들 (랜덤으로 선택됨)")]
    [SerializeField] private List<Transform> _spawnTransforms = new List<Transform>();

    private NetworkRunner _runner;
    private bool _isInitialized = false;

    void Start()
    {
        // NetworkRunner 찾기 (초기화만, 자동 스폰은 하지 않음)
        _runner = FusionManager.LocalRunner;
        
        if (_runner == null)
        {
            _runner = FindObjectOfType<NetworkRunner>();
        }
        
        _isInitialized = true;
    }

    /// <summary>
    /// MainGameManager에서 호출: StageData의 WaveData를 기반으로 적을 스폰합니다.
    /// </summary>
    /// <param name="waveData">스폰할 웨이브 데이터</param>
    public void SpawnWave(WaveData waveData)
    {
        if (waveData == null || waveData.enemySpawnDataList == null || waveData.enemySpawnDataList.Count == 0)
        {
            Debug.LogWarning($"[EnemySpawner] WaveData is null or empty for spawner {_spawnerIndex}");
            return;
        }

        if (!_isInitialized)
        {
            Debug.LogWarning($"[EnemySpawner] Spawner {_spawnerIndex} is not initialized yet!");
            return;
        }

        // NetworkRunner 확인
        if (_runner == null)
        {
            _runner = FusionManager.LocalRunner ?? FindObjectOfType<NetworkRunner>();
        }

        if (_runner == null || !_runner.IsRunning)
        {
            Debug.LogWarning($"[EnemySpawner] NetworkRunner is not available for spawner {_spawnerIndex}");
            return;
        }

        // 서버에서만 스폰
        if (!_runner.IsServer)
        {
            return;
        }

        // 이 스포너에 해당하는 EnemySpawnData 찾기
        foreach (var spawnData in waveData.enemySpawnDataList)
        {
            if (spawnData.spawnerIndex == _spawnerIndex)
            {
                StartCoroutine(SpawnEnemiesCoroutine(spawnData));
            }
        }
    }

    /// <summary>
    /// EnemySpawnData에 따라 적들을 스폰합니다.
    /// </summary>
    private IEnumerator SpawnEnemiesCoroutine(EnemySpawnData spawnData)
    {
        // 스폰 지연
        if (spawnData.spawnDelay > 0)
        {
            yield return new WaitForSeconds(spawnData.spawnDelay);
        }

        // EnemyData 찾기
        EnemyData enemyData = null;
        if (!string.IsNullOrEmpty(spawnData.enemyCode))
        {
            enemyData = GameDataManager.Instance?.EnemyService?.GetEnemyByCode(spawnData.enemyCode);
        }

        if (enemyData == null)
        {
            Debug.LogError($"[EnemySpawner] EnemyData not found for code: {spawnData.enemyCode} (Spawner: {_spawnerIndex})");
            yield break;
        }

        if (_enemyPrefab.IsValid == false)
        {
            Debug.LogError($"[EnemySpawner] EnemyPrefab is not set for spawner {_spawnerIndex}!");
            yield break;
        }

        // 적 개수만큼 스폰
        for (int i = 0; i < spawnData.enemyCount; i++)
        {
            // 랜덤 스폰 위치 선택
            Vector2 spawnPosition = GetRandomSpawnPosition();

            // 적 스폰
            SpawnEnemy(enemyData, spawnPosition);

            // 스폰 간격 대기 (마지막 적은 대기하지 않음)
            if (i < spawnData.enemyCount - 1 && spawnData.spawnInterval > 0)
            {
                yield return new WaitForSeconds(spawnData.spawnInterval);
            }
        }
    }

    /// <summary>
    /// 단일 적을 스폰합니다.
    /// </summary>
    private void SpawnEnemy(EnemyData enemyData, Vector2 spawnPosition)
    {
        if (_runner == null || !_runner.IsServer) return;

        NetworkObject enemyObject = _runner.Spawn(
            _enemyPrefab,
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
                        Debug.Log($"[EnemySpawner] Enemy spawned at {spawnPosition} with EnemyIndex: {enemyIndex} (Spawner: {_spawnerIndex})");
                    }
                    else
                    {
                        Debug.LogWarning($"[EnemySpawner] EnemyData not found in EnemyService! Code: {enemyData.code}");
                    }
                }
            }
        );
    }

    /// <summary>
    /// _spawnTransforms 중 랜덤 위치를 반환합니다.
    /// </summary>
    private Vector2 GetRandomSpawnPosition()
    {
        if (_spawnTransforms == null || _spawnTransforms.Count == 0)
        {
            // 스폰 포인트가 없으면 자신의 위치 사용
            return transform.position;
        }

        // 유효한 Transform만 필터링
        List<Transform> validTransforms = new List<Transform>();
        foreach (var t in _spawnTransforms)
        {
            if (t != null)
            {
                validTransforms.Add(t);
            }
        }

        if (validTransforms.Count == 0)
        {
            return transform.position;
        }

        // 랜덤 선택
        int randomIndex = Random.Range(0, validTransforms.Count);
        return validTransforms[randomIndex].position;
    }

    /// <summary>
    /// EnemyService에서 해당 EnemyData의 인덱스를 찾습니다.
    /// </summary>
    private int FindEnemyIndex(EnemyData data)
    {
        if (GameDataManager.Instance == null || data == null) return -1;

        var allEnemies = GameDataManager.Instance.EnemyService.GetAllEnemies();
        for (int i = 0; i < allEnemies.Count; i++)
        {
            if (allEnemies[i] != null && allEnemies[i].code == data.code)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 스포너 인덱스를 반환합니다.
    /// </summary>
    public int SpawnerIndex => _spawnerIndex;

    #if UNITY_EDITOR
    /// <summary>
    /// 에디터에서 스포너 위치와 스폰 포인트를 시각화합니다.
    /// </summary>
    void OnDrawGizmos()
    {
        // 스포너 위치 표시
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        
        // 스폰 포인트들 표시
        if (_spawnTransforms != null && _spawnTransforms.Count > 0)
        {
            Gizmos.color = Color.yellow;
            foreach (var spawnPoint in _spawnTransforms)
            {
                if (spawnPoint != null)
                {
                    Gizmos.DrawWireSphere(spawnPoint.position, 0.3f);
                    Gizmos.DrawLine(transform.position, spawnPoint.position);
                }
            }
        }
    }
    #endif
}
