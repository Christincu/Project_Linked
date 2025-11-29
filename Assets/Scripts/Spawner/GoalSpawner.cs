using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 웨이브의 Collect 목표일 때, GoalSpawnData를 기반으로 목표 오브젝트를 스폰하는 스포너입니다.
/// EnemySpawner와 비슷한 구조로 설계되며, 프리팹은 이 컴포넌트에서만 지정합니다.
/// </summary>
public class GoalSpawner : MonoBehaviour
{
    [Header("Spawner Settings")]
    [Tooltip("스포너 인덱스 (WaveData의 GoalSpawnData.spawnerIndex와 매칭)")]
    [SerializeField] private int _spawnerIndex = 0;

    [Header("Goal Settings")]
    [Tooltip("스폰할 목표 오브젝트 프리팹 (Collect 목표에서 사용, NetworkObject 필수)")]
    [SerializeField] private GameObject _goalPrefab;

    [Header("Spawn Points")]
    [Tooltip("목표 오브젝트를 스폰할 위치들 (랜덤으로 선택됨)")]
    [SerializeField] private List<Transform> _spawnTransforms = new List<Transform>();

    // 네트워크 러너 (EnemySpawner와 동일한 방식으로 사용)
    private NetworkRunner _runner;
    private bool _isInitialized = false;

    // 이 스포너가 생성한 목표 오브젝트(NetworkObject)들을 추적합니다.
    private readonly List<NetworkObject> _spawnedGoals = new List<NetworkObject>();

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
    /// 이 GoalSpawner의 스포너 인덱스를 반환합니다.
    /// </summary>
    public int SpawnerIndex => _spawnerIndex;

    /// <summary>
    /// 주어진 웨이브 데이터에서 이 스포너에 해당하는 GoalSpawnData를 찾아 목표 오브젝트를 스폰합니다.
    /// </summary>
    /// <param name="waveData">Collect 목표를 가진 웨이브 데이터</param>
    public void SpawnGoals(WaveData waveData)
    {
        if (waveData == null || waveData.goalSpawnDataList == null || waveData.goalSpawnDataList.Count == 0)
        {
            Debug.LogWarning($"[GoalSpawner] WaveData has no goalSpawnDataList for spawner {_spawnerIndex}");
            return;
        }

        if (_goalPrefab == null)
        {
            Debug.LogError($"[GoalSpawner] Goal prefab is not assigned for spawner {_spawnerIndex}!");
            return;
        }

        if (!_isInitialized)
        {
            Debug.LogWarning($"[GoalSpawner] Spawner {_spawnerIndex} is not initialized yet!");
            return;
        }

        // NetworkRunner 확인
        if (_runner == null)
        {
            _runner = FusionManager.LocalRunner ?? FindObjectOfType<NetworkRunner>();
        }

        if (_runner == null || !_runner.IsRunning)
        {
            Debug.LogWarning($"[GoalSpawner] NetworkRunner is not available for spawner {_spawnerIndex}");
            return;
        }

        // 서버에서만 스폰
        if (!_runner.IsServer)
        {
            return;
        }

        // 이 스포너에 해당하는 GoalSpawnData만 처리
        foreach (var goalData in waveData.goalSpawnDataList)
        {
            if (goalData == null) continue;

            if (goalData.spawnerIndex == _spawnerIndex)
            {
                StartCoroutine(SpawnGoalCoroutine(goalData, waveData));
            }
        }
    }

    /// <summary>
    /// GoalSpawnData에 따라 목표 오브젝트를 스폰합니다.
    /// waveGoalCount에 따라 여러 개를 스폰하며, spawnDelay/Interval을 사용합니다.
    /// </summary>
    private IEnumerator SpawnGoalCoroutine(GoalSpawnData goalData, WaveData waveData)
    {
        // 스폰 지연
        if (goalData.spawnDelay > 0f)
        {
            yield return new WaitForSeconds(goalData.spawnDelay);
        }

        // waveGoalCount에 따라 여러 개 스폰 (최소 1개)
        int goalCount = Mathf.Max(1, waveData.waveGoalCount);

        // GoalPrefab에서 NetworkObject 필수
        NetworkObject goalPrefabNO = _goalPrefab.GetComponent<NetworkObject>();
        if (goalPrefabNO == null)
        {
            Debug.LogError($"[GoalSpawner] Goal prefab '{_goalPrefab.name}' has no NetworkObject component! (Spawner: {_spawnerIndex})");
            yield break;
        }

        for (int i = 0; i < goalCount; i++)
        {
            Vector3 spawnPosition = GetRandomSpawnPosition();

            if (_runner == null || !_runner.IsServer)
            {
                yield break;
            }

            NetworkObject goalNO = _runner.Spawn(
                goalPrefabNO,
                spawnPosition,
                Quaternion.identity,
                null,
                (runner, obj) =>
                {
                    if (obj.TryGetComponent(out GoalObject goalObject))
                    {
                        // 서버에서 WaveData를 설정 (Collect 목표 진행에 사용)
                        goalObject.Initialize(waveData);
                    }
                });

            if (goalNO != null)
            {
                _spawnedGoals.Add(goalNO);
            }

            // 다음 목표 오브젝트 스폰까지 대기 (마지막은 대기하지 않음)
            if (i < goalCount - 1 && goalData.spawnInterval > 0f)
            {
                yield return new WaitForSeconds(goalData.spawnInterval);
            }
        }
    }

    /// <summary>
    /// _spawnTransforms 중 랜덤 위치를 반환합니다.
    /// </summary>
    private Vector3 GetRandomSpawnPosition()
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

        int randomIndex = Random.Range(0, validTransforms.Count);
        return validTransforms[randomIndex].position;
    }

    /// <summary>
    /// 이 스포너가 생성한 모든 목표 오브젝트를 제거합니다.
    /// (서버에서만 네트워크 디스폰)
    /// </summary>
    public void DestroyAllGoals()
    {
        if (_runner == null || !_runner.IsServer)
        {
            _spawnedGoals.Clear();
            return;
        }

        for (int i = 0; i < _spawnedGoals.Count; i++)
        {
            NetworkObject goalNO = _spawnedGoals[i];
            if (goalNO != null && goalNO.IsValid)
            {
                _runner.Despawn(goalNO);
            }
        }
        _spawnedGoals.Clear();
    }

#if UNITY_EDITOR
    /// <summary>
    /// 에디터에서 스포너 위치와 스폰 포인트를 시각화합니다.
    /// </summary>
    private void OnDrawGizmos()
    {
        // 스포너 위치 표시
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.4f);

        // 스폰 포인트들 표시
        if (_spawnTransforms != null && _spawnTransforms.Count > 0)
        {
            Gizmos.color = Color.green;
            foreach (var spawnPoint in _spawnTransforms)
            {
                if (spawnPoint != null)
                {
                    Gizmos.DrawWireSphere(spawnPoint.position, 0.25f);
                    Gizmos.DrawLine(transform.position, spawnPoint.position);
                }
            }
        }
    }
#endif
}
