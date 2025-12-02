using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 웨이브의 Collect 목표일 때, GoalSpawnData를 기반으로 목표 오브젝트를 스폰하는 스포너입니다.
/// </summary>
public class GoalSpawner : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("스포너 인덱스 (WaveData의 GoalSpawnData.spawnerIndex와 매칭)")]
    [SerializeField] private int _spawnerIndex = 0;

    [Header("Goal Settings")]
    [Tooltip("스폰할 목표 오브젝트 프리팹 (Collect 목표에서 사용, NetworkObject 필수)")]
    [SerializeField] private GameObject _goalPrefab;

    [Header("Spawn Area")]
    [Tooltip("목표 오브젝트를 스폰할 위치들 (랜덤으로 선택됨)")]
    [SerializeField] private List<Transform> _spawnTransforms = new List<Transform>();

    [Header("Collision Prevention")]
    [Tooltip("스폰 시 겹침을 방지할 레이어")]
    [SerializeField] private LayerMask _collisionLayerMask;
    [Tooltip("겹침 확인 반경")]
    [SerializeField] private float _checkRadius = 0.5f;
    [Tooltip("빈 공간 찾기 최대 시도 횟수")]
    [SerializeField] private int _maxSpawnAttempts = 10;

    // State
    private NetworkRunner _runner;
    private readonly List<NetworkObject> _spawnedGoals = new List<NetworkObject>();
    private readonly List<Coroutine> _activeSpawnCoroutines = new List<Coroutine>();
    private bool _isSpawningActive = true;

    // Public Property
    public int SpawnerIndex => _spawnerIndex;

    private void Start()
    {
        InitializeRunner();
    }

    /// <summary>
    /// NetworkRunner 초기화 및 유효성 검사
    /// </summary>
    private bool InitializeRunner()
    {
        if (_runner != null && _runner.IsRunning) return true;

        _runner = FusionManager.LocalRunner;
        if (_runner == null)
        {
            _runner = FindObjectOfType<NetworkRunner>();
        }

        return _runner != null && _runner.IsRunning;
    }

    /// <summary>
    /// 외부 호출용: 웨이브 데이터를 받아 목표물 스폰 시작
    /// </summary>
    public void SpawnGoals(WaveData waveData)
    {
        // 1. 기본 유효성 검사
        if (!IsValidWaveData(waveData)) return;

        // 2. 프리팹 검사
        if (_goalPrefab == null)
        {
            Debug.LogError($"[GoalSpawner-{_spawnerIndex}] Goal prefab is not assigned!");
            return;
        }

        // 3. 러너 상태 확인 (서버만 스폰 가능)
        if (!InitializeRunner() || !_runner.IsServer) return;

        // 4. 해당 스포너의 데이터만 필터링하여 스폰 진행
        _isSpawningActive = true; // 스폰 활성화
        foreach (var goalData in waveData.goalSpawnDataList)
        {
            if (goalData.spawnerIndex == _spawnerIndex)
            {
                Coroutine spawnCoroutine = StartCoroutine(SpawnRoutine(goalData, waveData));
                _activeSpawnCoroutines.Add(spawnCoroutine);
            }
        }
    }

    /// <summary>
    /// 실제 스폰을 수행하는 코루틴
    /// </summary>
    private IEnumerator SpawnRoutine(GoalSpawnData goalData, WaveData waveData)
    {
        try
        {
            // 초기 지연
            if (goalData.spawnDelay > 0f)
            {
                yield return new WaitForSeconds(goalData.spawnDelay);
                
                // 지연 후 스폰 활성 상태 확인
                if (!_isSpawningActive) yield break;
            }

            // NetworkObject 컴포넌트 확인
            NetworkObject goalPrefabNO = _goalPrefab.GetComponent<NetworkObject>();
            if (goalPrefabNO == null)
            {
                Debug.LogError($"[GoalSpawner-{_spawnerIndex}] Prefab '{_goalPrefab.name}' missing NetworkObject!");
                yield break;
            }

            int goalCount = Mathf.Max(1, waveData.waveGoalCount);
            
            // ★ 핵심: 이번 웨이브 루프 동안 예약된 위치들을 기억함 (물리 업데이트 딜레이 해결)
            List<Vector3> reservedPositions = new List<Vector3>();

            for (int i = 0; i < goalCount; i++)
            {
                // 스폰이 중단되었는지 확인 (라운드 종료 체크)
                if (!_isSpawningActive)
                {
                    yield break;
                }

                // Runner 체크
                if (_runner == null || !_runner.IsRunning)
                {
                    _isSpawningActive = false;
                    yield break;
                }

                // MainGameManager가 없거나 라운드가 종료되었는지 확인
                if (MainGameManager.Instance != null)
                {
                    int currentRound = MainGameManager.Instance.GetCurrentRoundIndex();
                    if (currentRound < 0) // 라운드가 종료되면 -1로 설정됨
                    {
                        _isSpawningActive = false;
                        yield break;
                    }
                }

                // 1. 유효한 위치 찾기 (물리 충돌 + 예약된 위치 회피)
                Vector3 spawnPos = GetValidSpawnPosition(reservedPositions);

                // 2. 위치 예약
                reservedPositions.Add(spawnPos);

                // 3. 스폰 실행
                SpawnSingleGoal(goalPrefabNO, spawnPos, waveData);

                // 4. 간격 대기
                if (i < goalCount - 1 && goalData.spawnInterval > 0f)
                {
                    yield return new WaitForSeconds(goalData.spawnInterval);
                    
                    // 대기 후에도 스폰 활성 상태 확인
                    if (!_isSpawningActive) yield break;
                }
            }
        }
        finally
        {
            // 코루틴이 종료되면 리스트에서 제거
            _activeSpawnCoroutines.RemoveAll(c => c == null);
        }
    }

    /// <summary>
    /// 단일 목표 오브젝트 네트워크 스폰
    /// </summary>
    private void SpawnSingleGoal(NetworkObject prefab, Vector3 position, WaveData waveData)
    {
        NetworkObject goalNO = _runner.Spawn(
            prefab,
            position,
            Quaternion.identity,
            null,
            (runner, obj) =>
            {
                if (obj.TryGetComponent(out GoalObject goalObject))
                {
                    goalObject.Initialize(waveData);
                }
            });

        if (goalNO != null)
        {
            _spawnedGoals.Add(goalNO);
        }
    }

    /// <summary>
    /// 충돌하지 않는 유효한 스폰 위치를 반환합니다.
    /// </summary>
    private Vector3 GetValidSpawnPosition(List<Vector3> reservedPositions)
    {
        if (_spawnTransforms == null || _spawnTransforms.Count == 0) return transform.position;

        // 유효한 Transform만 추리기
        var validPoints = _spawnTransforms.FindAll(t => t != null);
        if (validPoints.Count == 0) return transform.position;

        for (int i = 0; i < _maxSpawnAttempts; i++)
        {
            Vector3 candidatePos = validPoints[Random.Range(0, validPoints.Count)].position;

            // 1차 검사: 물리 충돌 (Vector3 -> Vector2 변환하여 2D 체크)
            Collider2D hit = Physics2D.OverlapCircle(candidatePos, _checkRadius, _collisionLayerMask);
            if (hit != null) continue; // 충돌체 있으면 재시도

            // 2차 검사: 예약 목록 확인
            bool isReserved = false;
            foreach (var pos in reservedPositions)
            {
                // Vector2.Distance를 사용하여 Z축 무시하고 평면 거리 계산 (필요시 Vector3.Distance 사용)
                if (Vector2.Distance(candidatePos, pos) < _checkRadius)
                {
                    isReserved = true;
                    break;
                }
            }

            if (isReserved) continue; // 예약된 위치면 재시도

            // 통과!
            return candidatePos;
        }

        // 실패 시 랜덤 반환
        return validPoints[Random.Range(0, validPoints.Count)].position;
    }

    /// <summary>
    /// 이 스포너가 생성한 모든 목표 오브젝트를 제거합니다.
    /// (서버에서만 네트워크 디스폰)
    /// 모든 실행 중인 스폰 코루틴도 중단합니다.
    /// </summary>
    public void DestroyAllGoals()
    {
        if (_runner == null || !_runner.IsServer)
        {
            _spawnedGoals.Clear();
            _isSpawningActive = false;
            StopAllSpawnCoroutines();
            return;
        }

        // 스폰 중단 플래그 설정
        _isSpawningActive = false;
        
        // 모든 실행 중인 스폰 코루틴 중단
        StopAllSpawnCoroutines();
        
        Debug.Log($"[GoalSpawner-{_spawnerIndex}] DestroyAllGoals: Stopping {_activeSpawnCoroutines.Count} coroutines, removing {_spawnedGoals.Count} goals");

        // 역순 순회 + 예외 처리 적용
        for (int i = _spawnedGoals.Count - 1; i >= 0; i--)
        {
            try
            {
                var goalNO = _spawnedGoals[i];

                if (goalNO == null)
                {
                    _spawnedGoals.RemoveAt(i);
                    continue;
                }

                if (goalNO.IsValid)
                {
                    _runner.Despawn(goalNO);
                }
                
                _spawnedGoals.RemoveAt(i);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GoalSpawner-{_spawnerIndex}] Error destroying goal at index {i}: {e.Message}");
            }
        }
        _spawnedGoals.Clear();
    }
    
    /// <summary>
    /// 모든 실행 중인 스폰 코루틴을 중단합니다.
    /// </summary>
    private void StopAllSpawnCoroutines()
    {
        foreach (var coroutine in _activeSpawnCoroutines)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }
        _activeSpawnCoroutines.Clear();
    }

    // --- Helper Methods ---

    private bool IsValidWaveData(WaveData waveData)
    {
        if (waveData == null || waveData.goalSpawnDataList == null || waveData.goalSpawnDataList.Count == 0)
        {
            Debug.LogWarning($"[GoalSpawner-{_spawnerIndex}] Invalid WaveData received.");
            return false;
        }
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.4f);

        if (_spawnTransforms != null)
        {
            foreach (var point in _spawnTransforms)
            {
                if (point == null) continue;

                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(point.position, 0.25f);
                Gizmos.DrawLine(transform.position, point.position);

                // 충돌 체크 범위 시각화
                Gizmos.color = new Color(0, 1, 0, 0.3f); // 반투명 초록
                Gizmos.DrawWireSphere(point.position, _checkRadius);
            }
        }
    }
#endif
}