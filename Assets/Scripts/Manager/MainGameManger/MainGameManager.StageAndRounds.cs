using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using WaveGoalTypeEnum = WaveGoalType;

public partial class MainGameManager
{
    // ============================================================================================
    // [Door & Wave Logic Merged]
    // 문 관리와 웨이브 진행 로직 통합 (검색 로직 제거 버전)
    // ============================================================================================

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
    public void StartRound(int roundIndex, List<EnemySpawner> enemySpawners, List<GoalSpawner> goalSpawners = null, List<GameObject> doors = null)
    {
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

        // 3. [문 로직 수정] 검색 로직 삭제, 전달받은 리스트만 사용
        _currentRoundDoorObjects.Clear();

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

        // 4. 문 닫기 실행 (즉시 실행)
        CloseMapDoor();

        // 5. 웨이브 시작
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
        // 1. 오브젝트 정리
        CleanupCurrentWaveObjects();
        _currentRoundEnemySpawners.Clear();
        _currentRoundGoalSpawners.Clear();
        _activeWaves.Clear();

        // 2. [문 로직] 문 열기 (즉시 실행)
        OpenMapDoor();
        _currentRoundDoorObjects.Clear();

        // 3. 상태 리셋
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
        if (_currentRoundEnemySpawners != null)
        {
            foreach (var spawner in _currentRoundEnemySpawners) spawner?.KillAllEnemies();
        }

        if (_currentRoundGoalSpawners != null)
        {
            foreach (var spawner in _currentRoundGoalSpawners) spawner?.DestroyAllGoals();
        }
    }
}