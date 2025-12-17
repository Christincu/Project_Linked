using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using WaveGoalTypeEnum = WaveGoalType;

public partial class MainGameManager
{
    private class WaveProgress
    {
        public WaveData waveData;
        public int currentGoalCount = 0;
        public float elapsedTime = 0f;
        public bool isCompleted = false;

        public WaveProgress() { }
        public WaveProgress(WaveData data)
        {
            waveData = data;
            currentGoalCount = 0;
            elapsedTime = 0f;
            isCompleted = false;
        }
    }

    [SerializeField] private StageData _currentStageData;
    [SerializeField] private List<RoundTrigger> _roundTriggers = new List<RoundTrigger>();

    [Networked] public int RoundIndex { get; private set; } = -1;
    [Networked] public int WaveIndex { get; private set; } = -1;
    [Networked] public int WaveGoalType { get; private set; } = -1;
    [Networked] public int WaveCurrentGoal { get; private set; } = 0;
    [Networked] public int WaveTotalGoal { get; private set; } = 0;
    [Networked] public float WaveElapsedTime { get; private set; } = 0f;

    private Dictionary<int, WaveProgress> _activeWaves = new Dictionary<int, WaveProgress>();

    private int _currentRoundIndex = -1;
    private int _currentWaveIndex = -1;

    private List<EnemySpawner> _currentRoundEnemySpawners = new List<EnemySpawner>();
    private List<GoalSpawner> _currentRoundGoalSpawners = new List<GoalSpawner>();
    private List<RoundDoorNetworkController> _currentRoundDoorObjects = new List<RoundDoorNetworkController>();
    private List<GameObject> _currentRoundEndActiveObject = new List<GameObject>();

    /// <summary>
    /// 스테이지 데이터를 기반으로 적 스폰을 초기화합니다.
    /// </summary>
    private void InitializeStageEnemySpawning()
    {
        if (_currentStageData != null) return;

        string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        StageData stageData = GetStageDataBySceneName(currentSceneName);

        if (stageData == null)
        {
            Debug.LogWarning($"[MainGameManager] Stage data not found for scene: {currentSceneName}.");
            return;
        }

        _currentStageData = stageData;
    }

    private StageData GetStageDataBySceneName(string sceneName)
    {
        if (_currentStageData != null)
        {
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentSceneName == sceneName) return _currentStageData;
        }

        if (GameDataManager.Instance == null) return null;

        return GameDataManager.Instance.StageService.GetStageBySceneName(sceneName);
    }

    /// <summary>
    /// 특정 라운드를 시작합니다. (RoundTrigger에서 전달받은 문만 사용)
    /// </summary>
    public void StartRound(int roundIndex, List<EnemySpawner> enemySpawners, List<GoalSpawner> goalSpawners = null, List<GameObject> doors = null, List<GameObject> roundEndActiveObjects = null)
    {
        // 0. 이전 라운드의 스폰 코루틴 및 오브젝트 정리 (중요: 새로운 라운드 시작 전)
        if (_runner != null && _runner.IsServer)
        {
            CleanupCurrentWaveObjects();
        }

        // 1. 데이터 검증
        if (_currentStageData == null) InitializeStageEnemySpawning();
        if (_currentStageData == null) return;

        var roundDataList = _currentStageData.roundDataList;
        if (roundDataList == null || roundIndex < 0 || roundIndex >= roundDataList.Count)
        {
            Debug.LogError($"[MainGameManager] Invalid round index: {roundIndex}");
            return;
        }

        RoundData roundData = roundDataList[roundIndex];
        if (roundData == null || roundData.waveDataList == null || roundData.waveDataList.Count == 0) return;

        // 2. 변수 설정
        _currentRoundIndex = roundIndex;
        _currentWaveIndex = 0;

        // 네트워크 변수 업데이트 (서버)
        if (_runner != null && _runner.IsServer)
        {
            RoundIndex = roundIndex;
            WaveIndex = 0;
        }

        _currentRoundEnemySpawners = enemySpawners ?? new List<EnemySpawner>();
        _currentRoundGoalSpawners = goalSpawners ?? new List<GoalSpawner>();

        // 4. [문 로직 수정] 검색 로직 삭제, 전달받은 리스트만 사용
        _currentRoundDoorObjects.Clear();

        // 5. 라운드 종료 시 활성화할 오브젝트 저장
        _currentRoundEndActiveObject.Clear();
        if (roundEndActiveObjects != null && roundEndActiveObjects.Count > 0)
        {
            foreach (var obj in roundEndActiveObjects)
            {
                if (obj != null)
                {
                    _currentRoundEndActiveObject.Add(obj);
                }
            }
        }

        // 6. [문 로직] 문 닫기 실행 (즉시 실행)
        if (doors != null && doors.Count > 0)
        {
            foreach (var door in doors)
            {
                // 리스트 내부에 null이 섞여있을 수 있으므로 필터링
                if (door != null)
                {
                    _currentRoundDoorObjects.Add(door.GetComponent<RoundDoorNetworkController>());
                }
            }
        }
        else
        {
            Debug.LogWarning($"[MainGameManager] Round {roundIndex} has no doors assigned.");
        }

        CloseMapDoor();

        // 7. 웨이브 시작
        _activeWaves.Clear();
        StartCurrentWave();
    }

    /// <summary>
    /// 현재 라운드의 모든 문을 닫습니다. (즉시 실행)
    /// </summary>
    private void CloseMapDoor()
    {
        if (_currentRoundDoorObjects == null || _currentRoundDoorObjects.Count == 0) return;

        // 서버 권한이 있을 때만 상태 변경
        if (_runner != null && _runner.IsServer)
        {
            int closedCount = 0;
            foreach (var door in _currentRoundDoorObjects)
            {
                if (door != null && door.Object != null && door.Object.IsValid)
                {
                    door.SetClosed(true);
                    closedCount++;
                }
            }
            Debug.Log($"[MainGameManager] Closed {closedCount} doors for Round {_currentRoundIndex}.");
        }
    }

    /// <summary>
    /// 현재 라운드의 모든 문을 엽니다. (즉시 실행)
    /// </summary>
    private void OpenMapDoor()
    {
        if (_currentRoundDoorObjects == null || _currentRoundDoorObjects.Count == 0) return;

        if (_runner != null && _runner.IsServer)
        {
            foreach (var door in _currentRoundDoorObjects)
            {
                if (door != null && door.Object != null && door.Object.IsValid)
                {
                    door.SetClosed(false);
                }
            }
        }
    }

    private void ActiveRoundEndActiveObject(){
        if (_currentRoundEndActiveObject != null && _currentRoundEndActiveObject.Count > 0)
        {
            foreach (var obj in _currentRoundEndActiveObject)
            {
                obj.SetActive(true);
            }
        }
    }

    // 외부 호출용 (이전 코드 호환성 유지)
    public void CloseCurrentRoundDoors() => CloseMapDoor();

    /// <summary>
    /// 현재 라운드에서 _currentWaveIndex에 해당하는 웨이브를 시작합니다.
    /// </summary>
    private void StartCurrentWave()
    {
        if (_currentStageData == null) return;

        var roundDataList = _currentStageData.roundDataList;
        RoundData roundData = roundDataList[_currentRoundIndex];

        // 라운드 종료 체크
        if (_currentWaveIndex >= roundData.waveDataList.Count)
        {
            OnRoundCompleted();
            return;
        }

        WaveData currentWave = roundData.waveDataList[_currentWaveIndex];
        if (currentWave == null)
        {
            _currentWaveIndex++;
            StartCurrentWave();
            return;
        }

        _activeWaves.Clear();

        if (_runner != null && _runner.IsServer)
        {
            WaveIndex = _currentWaveIndex;
        }

        StartWave(currentWave, _currentRoundEnemySpawners, _currentRoundGoalSpawners);
    }

    public void StartWave(WaveData waveData, List<EnemySpawner> enemySpawners, List<GoalSpawner> goalSpawners = null)
    {
        if (waveData == null) return;

        int waveId = waveData.GetHashCode();
        if (waveData.waveGoalCount > 0 && !_activeWaves.ContainsKey(waveId))
        {
            _activeWaves[waveId] = new WaveProgress(waveData);
            UpdateWaveGoalUI(waveData, _activeWaves[waveId]);
        }

        // 적 스폰
        if (waveData.enemySpawnDataList != null)
        {
            foreach (var spawnData in waveData.enemySpawnDataList)
            {
                if (enemySpawners != null && spawnData.spawnerIndex >= 0 && spawnData.spawnerIndex < enemySpawners.Count)
                {
                    enemySpawners[spawnData.spawnerIndex]?.SpawnWave(new WaveData
                    {
                        waveGoalType = waveData.waveGoalType,
                        waveGoalCount = waveData.waveGoalCount,
                        enemySpawnDataList = new List<EnemySpawnData> { spawnData }
                    });
                }
            }
        }

        // 목표 오브젝트 스폰
        if (waveData.waveGoalType == WaveGoalTypeEnum.Collect && goalSpawners != null)
        {
            foreach (var spawner in goalSpawners)
            {
                spawner?.SpawnGoals(waveData);
            }
        }
    }

    public void AddWaveGoalProgress(WaveData waveData, int amount = 1)
    {
        if (waveData == null || waveData.waveGoalType == WaveGoalTypeEnum.Survive) return;

        int waveId = waveData.GetHashCode();
        if (!_activeWaves.ContainsKey(waveId)) return;

        WaveProgress progress = _activeWaves[waveId];
        if (progress.isCompleted) return;

        progress.currentGoalCount += amount;
        UpdateWaveGoalUI(waveData, progress);

        if (progress.currentGoalCount >= progress.waveData.waveGoalCount)
        {
            OnWaveGoalCompleted(waveData);
        }
    }

    public void OnEnemyKilled()
    {
        if (_activeWaves == null) return;

        // [수정] Collection was modified 에러 방지: Dictionary 복사본을 사용하여 순회
        // 여러 적이 동시에 죽을 때 _activeWaves가 수정되는 것을 방지
        var wavesCopy = new List<KeyValuePair<int, WaveProgress>>(_activeWaves);
        
        foreach (var kvp in wavesCopy)
        {
            // 복사본을 순회하지만, 실제 Dictionary에서 해당 항목이 여전히 존재하는지 확인
            if (!_activeWaves.ContainsKey(kvp.Key)) continue;
            
            var progress = _activeWaves[kvp.Key];
            if (!progress.isCompleted && progress.waveData.waveGoalType == WaveGoalTypeEnum.Kill)
            {
                AddWaveGoalProgress(progress.waveData, 1);
            }
        }
    }

    private void OnWaveGoalCompleted(WaveData waveData)
    {
        if (waveData == null) return;

        int waveId = waveData.GetHashCode();
        if (!_activeWaves.ContainsKey(waveId)) return;

        _activeWaves[waveId].isCompleted = true;

        CleanupCurrentWaveObjects();
        _currentWaveIndex++;

        StartCurrentWave();
    }

    private void UpdateSurviveWaveGoals(float deltaTime)
    {
        if (_activeWaves == null) return;

        List<WaveData> completedWaves = null;

        foreach (var kvp in _activeWaves)
        {
            var progress = kvp.Value;
            if (progress.isCompleted || progress.waveData.waveGoalType != WaveGoalTypeEnum.Survive) continue;

            progress.elapsedTime += deltaTime;
            UpdateWaveGoalUI(progress.waveData, progress);

            if (progress.elapsedTime >= progress.waveData.waveGoalCount)
            {
                progress.isCompleted = true;
                if (completedWaves == null) completedWaves = new List<WaveData>();
                completedWaves.Add(progress.waveData);
            }
        }

        if (completedWaves != null)
        {
            foreach (var wave in completedWaves) OnWaveGoalCompleted(wave);
        }
    }

    private void OnRoundCompleted()
    {
        // 서버에서만 오브젝트 정리를 수행하도록 강제
        if (_runner != null && _runner.IsServer)
        {
            CleanupCurrentWaveObjects();
        }

        // 스포너 리스트 비우기
        _currentRoundEnemySpawners.Clear();
        _currentRoundGoalSpawners.Clear();
        _activeWaves.Clear();

        // [문 로직] 문 열기 (즉시 실행)
        OpenMapDoor();
        _currentRoundDoorObjects.Clear();

        // 라운드 종료 시 활성화할 오브젝트 활성화
        ActiveRoundEndActiveObject();
        _currentRoundEndActiveObject.Clear();

        // 상태 리셋
        _currentRoundIndex = -1;
        _currentWaveIndex = -1;

        if (_runner != null && _runner.IsServer)
        {
            ResetNetworkedWaveVariables();
        }

        if (GameManager.Instance?.Canvas is MainCanvas canvas)
        {
            canvas.SetWaveText(string.Empty);
            canvas.SetGoalText(string.Empty);
        }
    }

    private void UpdateWaveGoalUI(WaveData waveData, WaveProgress progress)
    {
        if (GameManager.Instance?.Canvas is not MainCanvas canvas || waveData == null) return;

        if (_runner != null && _runner.IsServer)
        {
            UpdateNetworkedWaveVariables(
                _currentRoundIndex, _currentWaveIndex,
                waveData.waveGoalType, progress.currentGoalCount,
                waveData.waveGoalCount, progress.elapsedTime
            );
        }
    }

    private void CleanupCurrentWaveObjects()
    {
        // 방어 코드: 서버가 아니면 실행 안 함
        if (_runner == null || !_runner.IsServer) return;

        if (_currentRoundEnemySpawners != null)
        {
            // 리스트 복사본을 만들어 순회 (중간에 리스트가 변경되어도 루프가 안 깨지게 함)
            var spawners = new List<EnemySpawner>(_currentRoundEnemySpawners);
            foreach (var spawner in spawners)
            {
                if (spawner != null) spawner.KillAllEnemies();
            }
        }

        if (_currentRoundGoalSpawners != null)
        {
            var spawners = new List<GoalSpawner>(_currentRoundGoalSpawners);
            foreach (var spawner in spawners)
            {
                if (spawner != null) spawner.DestroyAllGoals();
            }
        }
    }
}