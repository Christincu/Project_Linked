using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class EnemySpawner : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("스포너 인덱스 (StageData의 EnemySpawnData.spawnerIndex와 매칭)")]
    [SerializeField] private int _spawnerIndex = 0;

    [Header("Settings")]
    [Tooltip("적 프리팹 (NetworkObject 컴포넌트 필수)")]
    [SerializeField] private NetworkPrefabRef _enemyPrefab;
    
    [Header("Spawn Area")]
    [Tooltip("스폰 가능한 위치 목록")]
    [SerializeField] private List<Transform> _spawnTransforms = new List<Transform>();

    [Header("Collision Prevention")]
    [Tooltip("스폰 시 겹침을 방지할 레이어 (Enemy, Goal 등)")]
    [SerializeField] private LayerMask _collisionLayerMask;
    [Tooltip("겹침 확인 반경")]
    [SerializeField] private float _checkRadius = 0.5f;
    [Tooltip("빈 공간 찾기 최대 시도 횟수")]
    [SerializeField] private int _maxSpawnAttempts = 10;

    // State
    private NetworkRunner _runner;
    private readonly List<NetworkObject> _spawnedEnemies = new List<NetworkObject>();
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
    /// 외부 호출용: 웨이브 데이터를 받아 적 스폰 시작
    /// </summary>
    public void SpawnWave(WaveData waveData)
    {
        // 1. 기본 유효성 검사
        if (!IsValidWaveData(waveData)) return;
        
        // 2. 러너 상태 확인 (서버만 스폰 가능)
        if (!InitializeRunner() || !_runner.IsServer) return;

        // 3. 해당 스포너의 데이터만 필터링하여 스폰 진행
        _isSpawningActive = true; // 스폰 활성화
        foreach (var spawnData in waveData.enemySpawnDataList)
        {
            if (spawnData.spawnerIndex == _spawnerIndex)
            {
                Coroutine spawnCoroutine = StartCoroutine(SpawnRoutine(spawnData));
                _activeSpawnCoroutines.Add(spawnCoroutine);
            }
        }
    }

    /// <summary>
    /// 실제 스폰을 수행하는 코루틴
    /// </summary>
    private IEnumerator SpawnRoutine(EnemySpawnData spawnData)
    {
        try
        {
            // 초기 지연
            if (spawnData.spawnDelay > 0)
            {
                yield return new WaitForSeconds(spawnData.spawnDelay);
                
                // 지연 후 스폰 활성 상태 확인
                if (!_isSpawningActive) yield break;
            }

            if (spawnData.enemyData == null)
            {
                Debug.LogError($"[EnemySpawner-{_spawnerIndex}] EnemyData is missing!");
                yield break;
            }

            // ★ 핵심: 이번 웨이브 루프 동안 예약된 위치들을 기억함 (물리 업데이트 딜레이 해결)
            List<Vector2> reservedPositions = new List<Vector2>();

            for (int i = 0; i < spawnData.enemyCount; i++)
            {
                // 스폰이 중단되었는지 확인 (라운드 종료 체크)
                if (!_isSpawningActive)
                {
                    yield break;
                }

                // Runner가 끊겼으면 중단
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
                Vector2 spawnPos = GetValidSpawnPosition(reservedPositions);
                
                // 2. 위치 예약 (다음 루프에서 이 자리를 피함)
                reservedPositions.Add(spawnPos);

                // 3. 스폰 실행
                SpawnSingleEnemy(spawnData.enemyData, spawnPos);

                // 4. 간격 대기
                if (i < spawnData.enemyCount - 1 && spawnData.spawnInterval > 0)
                {
                    yield return new WaitForSeconds(spawnData.spawnInterval);
                    
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
    
    private void SpawnSingleEnemy(EnemyData enemyData, Vector2 position, System.Action<NetworkObject> onSpawned = null)
    {
        var enemyObj = _runner.Spawn(_enemyPrefab, position, Quaternion.identity, null, (runner, obj) =>
        {
            if (obj.TryGetComponent(out EnemyController controller))
            {
                int index = FindEnemyIndex(enemyData);
                if (index != -1) controller.SetEnemyIndex(index);
            }
        });

        if (enemyObj != null)
        {
            _spawnedEnemies.Add(enemyObj);
            onSpawned?.Invoke(enemyObj);
        }
    }

    /// <summary>
    /// 충돌하지 않는 유효한 스폰 위치를 반환합니다.
    /// </summary>
    /// <param name="reservedPositions">현재 프레임/웨이브에서 이미 선점된 위치들</param>
    private Vector2 GetValidSpawnPosition(List<Vector2> reservedPositions)
    {
        // 스폰 포인트가 없으면 현재 위치 반환
        if (_spawnTransforms == null || _spawnTransforms.Count == 0) return transform.position;

        // 유효한 Transform만 추리기
        var validPoints = _spawnTransforms.FindAll(t => t != null);
        if (validPoints.Count == 0) return transform.position;

        for (int i = 0; i < _maxSpawnAttempts; i++)
        {
            Vector2 candidatePos = validPoints[Random.Range(0, validPoints.Count)].position;

            // 1차 검사: 물리 충돌 (이미 존재하는 오브젝트)
            Collider2D hit = Physics2D.OverlapCircle(candidatePos, _checkRadius, _collisionLayerMask);
            if (hit != null) continue; // 충돌체 있으면 재시도

            // 2차 검사: 예약 목록 (방금 스폰 명령을 내려서 아직 물리 생성이 안 된 오브젝트들)
            bool isReserved = false;
            foreach (var pos in reservedPositions)
            {
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

        // 모든 시도 실패 시 랜덤 반환 (혹은 로그 남기기)
        return validPoints[Random.Range(0, validPoints.Count)].position;
    }

    /// <summary>
    /// 이 스포너가 생성한 모든 적을 제거합니다. (서버에서만 동작)
    /// 모든 실행 중인 스폰 코루틴도 중단합니다.
    /// </summary>
    public void KillAllEnemies()
    {
        if (_runner == null || !_runner.IsServer) return;
        
        // 스폰 중단 플래그 설정
        _isSpawningActive = false;
        
        // 모든 실행 중인 스폰 코루틴 중단
        StopAllSpawnCoroutines();
        
        Debug.Log($"[EnemySpawner-{_spawnerIndex}] KillAllEnemies: Stopping {_activeSpawnCoroutines.Count} coroutines, removing {_spawnedEnemies.Count} enemies");

        // 역순으로 순회해야 리스트 요소가 제거되어도 인덱스 에러가 안 남
        for (int i = _spawnedEnemies.Count - 1; i >= 0; i--)
        {
            try
            {
                var enemyObj = _spawnedEnemies[i];
                
                // null이거나 이미 파괴된 객체는 리스트에서 그냥 제거
                if (enemyObj == null) 
                {
                    _spawnedEnemies.RemoveAt(i);
                    continue;
                }

                // Fusion 객체가 유효한 경우에만 Despawn
                if (enemyObj.IsValid)
                {
                    _runner.Despawn(enemyObj);
                }
                
                // Despawn 호출 후 리스트에서 제거
                _spawnedEnemies.RemoveAt(i);
            }
            catch (System.Exception e)
            {
                // 특정 적을 지우다 에러가 나도, 다음 적은 지워야 하므로 로그만 찍고 계속 진행
                Debug.LogWarning($"[EnemySpawner-{_spawnerIndex}] Error killing enemy at index {i}: {e.Message}");
            }
        }
        
        // 혹시 남은 게 있다면 클리어
        _spawnedEnemies.Clear();
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
        if (waveData == null || waveData.enemySpawnDataList == null || waveData.enemySpawnDataList.Count == 0)
        {
            Debug.LogWarning($"[EnemySpawner-{_spawnerIndex}] Invalid WaveData received.");
            return false;
        }
        return true;
    }

    private int FindEnemyIndex(EnemyData data)
    {
        if (GameDataManager.Instance == null || data == null) return -1;
        
        var allEnemies = GameDataManager.Instance.EnemyService.GetAllEnemies();
        for (int i = 0; i < allEnemies.Count; i++)
        {
            if (allEnemies[i].code == data.code) return i;
        }
        return -1;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.4f);

        if (_spawnTransforms != null)
        {
            foreach (var point in _spawnTransforms)
            {
                if (point == null) continue;
                
                // 스폰 포인트 위치
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(point.position, 0.2f);
                Gizmos.DrawLine(transform.position, point.position);

                // 충돌 체크 범위 시각화 (중요)
                Gizmos.color = new Color(1, 0, 0, 0.3f); // 반투명 빨강
                Gizmos.DrawWireSphere(point.position, _checkRadius);
            }
        }
    }
#endif
}