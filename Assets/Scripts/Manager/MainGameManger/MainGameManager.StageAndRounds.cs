using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스테이지, 라운드, 웨이브 및 목표 진행 관련 기능을 모은 partial 클래스입니다.
/// </summary>
public partial class MainGameManager
{
    /// <summary>
    /// 스테이지 데이터를 기반으로 적 스폰을 초기화합니다.
    /// StageData가 이미 로드되어 있다면 재로딩하지 않습니다.
    /// </summary>
    private void InitializeStageEnemySpawning()
    {
        // StageData가 이미 로드되어 있다면 재로딩하지 않습니다.
        if (_currentStageData != null) return;
        
        string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        StageData stageData = GetStageDataBySceneName(currentSceneName);
        
        if (stageData == null)
        {
            Debug.LogWarning($"[MainGameManager] Stage data not found for scene: {currentSceneName}. Enemy spawning will be skipped.");
            return;
        }
        
        _currentStageData = stageData;
    }
    
    /// <summary>
    /// 씬 이름으로 StageData를 가져옵니다.
    /// 이미 로드된 경우 캐시된 데이터를 반환합니다.
    /// </summary>
    /// <param name="sceneName">씬 이름</param>
    /// <returns>StageData or null</returns>
    private StageData GetStageDataBySceneName(string sceneName)
    {
        // 이미 로드된 경우 바로 반환 (캐시 활용)
        // 주의: StageData에 SceneName 필드가 있다면 더 정확한 검증 가능
        if (_currentStageData != null)
        {
            // 현재 씬 이름과 일치하는지 확인 (씬 전환 시 재로드 필요)
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentSceneName == sceneName)
            {
                return _currentStageData;
            }
        }
        
        if (GameDataManager.Instance == null)
        {
            Debug.LogError("[MainGameManager] GameDataManager.Instance is null!");
            return null;
        }
        
        return GameDataManager.Instance.StageService.GetStageBySceneName(sceneName);
    }
    
    /// <summary>
    /// 특정 라운드를 시작합니다.
    /// </summary>
    /// <param name="roundIndex">시작할 라운드 인덱스 (StageData.roundDataList의 인덱스)</param>
    /// <param name="enemySpawners">사용할 EnemySpawner 리스트 (RoundTrigger에서 전달)</param>
    /// <param name="goalSpawners">사용할 GoalSpawner 리스트 (Collect 목표용, null이면 사용 안 함)</param>
    public void StartRound(int roundIndex, List<EnemySpawner> enemySpawners, List<GoalSpawner> goalSpawners = null)
    {
        // StageData가 초기화되지 않은 경우 재시도
        if (_currentStageData == null)
        {
            InitializeStageEnemySpawning();
            if (_currentStageData == null)
            {
                Debug.LogError($"[MainGameManager] Stage data is null. Cannot start round {roundIndex}.");
                return;
            }
        }
        
        // roundDataList 검사
        var roundDataList = _currentStageData.roundDataList;
        if (roundDataList == null || roundDataList.Count == 0)
        {
            Debug.LogError($"[MainGameManager] Stage '{_currentStageData.stageName}' has no round data!");
            return;
        }
        
        // 라운드 인덱스 유효성 검사 및 RoundData 가져오기
        if (roundIndex < 0 || roundIndex >= roundDataList.Count)
        {
            Debug.LogError($"[MainGameManager] Invalid round index: {roundIndex} (Total rounds: {roundDataList.Count})");
            return;
        }
        
        RoundData roundData = roundDataList[roundIndex];
        if (roundData == null)
        {
            Debug.LogError($"[MainGameManager] Round data at index {roundIndex} is null!");
            return;
        }
        
        if (roundData.waveDataList == null || roundData.waveDataList.Count == 0)
        {
            Debug.LogWarning($"[MainGameManager] Round {roundIndex} has no wave data!");
            return;
        }
        
        // Enemy Spawner 유효성 검사
        if (enemySpawners == null || enemySpawners.Count == 0)
        {
            Debug.LogWarning($"[MainGameManager] No enemy spawners available for round {roundIndex}!");
            return;
        }
        
        // 현재 라운드/웨이브 인덱스 및 사용 중인 스포너/문 저장
        _currentRoundIndex = roundIndex;
        _currentWaveIndex = 0;
        
        // 네트워크 변수 업데이트 (서버만)
        if (_runner != null && _runner.IsServer && Object != null && Object.IsValid)
        {
            NetworkRoundIndex = roundIndex;
            NetworkWaveIndex = 0;
        }
        _currentRoundEnemySpawners = enemySpawners != null ? new List<EnemySpawner>(enemySpawners) : new List<EnemySpawner>();
        _currentRoundGoalSpawners = goalSpawners != null ? new List<GoalSpawner>(goalSpawners) : new List<GoalSpawner>();

        // 현재 라운드에 연결된 문 컨트롤러들 수집
        var currentRoundDoors = new List<RoundDoorNetworkController>();
        if (_roundTriggers != null)
        {
            foreach (var trigger in _roundTriggers)
            {
                if (trigger != null && trigger.RoundIndex == roundIndex)
                {
                    var doors = trigger.DoorObjects;
                    if (doors == null) continue;

                    foreach (var door in doors)
                    {
                        if (door != null && !currentRoundDoors.Contains(door))
                        {
                            currentRoundDoors.Add(door);
                        }
                    }
                }
            }
        }

        // 모든 문을 열린 상태로 시작 (네트워크 컨트롤러 기준)
        _isMapDoorClosed = false;
        foreach (var door in currentRoundDoors)
        {
            if (door != null)
            {
                // 서버에서만 Networked 상태를 변경 (클라이언트는 Spawned 기본값 사용)
                if (FusionManager.LocalRunner == null || FusionManager.LocalRunner.IsServer)
                {
                    door.SetClosed(false);
                }
            }
        }
        // 메인 게임 매니저 쪽에서 현재 라운드의 문들을 추적할 수 있도록 필드 업데이트
        _currentRoundDoorObjects = currentRoundDoors;

        // 이전 웨이브 진행 상태 초기화 (웨이브가 시작되기 전에 초기화하는 것이 명확합니다)
        _activeWaves.Clear();

        // 첫 번째 웨이브 시작
        StartCurrentWave();
    }
    
    /// <summary>
    /// 현재 라운드에서 _currentWaveIndex에 해당하는 웨이브를 시작합니다.
    /// 모든 웨이브를 완료했다면 라운드를 종료합니다.
    /// </summary>
    private void StartCurrentWave()
    {
        // StageData 유효성 검사 (StartRound에서 로드되었어야 함)
        if (_currentStageData == null)
        {
            Debug.LogWarning("[MainGameManager] StageData is null. Attempting re-initialization.");
            InitializeStageEnemySpawning();
            if (_currentStageData == null)
            {
                Debug.LogError("[MainGameManager] StartCurrentWave failed: StageData is still null.");
                return;
            }
        }
        
        // 라운드/웨이브 데이터 유효성 검사
        var roundDataList = _currentStageData.roundDataList;
        if (_currentRoundIndex < 0 || _currentRoundIndex >= roundDataList.Count)
        {
            Debug.LogError($"[MainGameManager] Invalid round index ({_currentRoundIndex}) in StartCurrentWave.");
            return;
        }

        RoundData roundData = roundDataList[_currentRoundIndex];
        if (roundData == null || roundData.waveDataList == null || roundData.waveDataList.Count == 0)
        {
            Debug.LogWarning($"[MainGameManager] Round {_currentRoundIndex} has no valid wave data.");
            OnRoundCompleted();
            return;
        }

        // 모든 웨이브를 완료한 경우 라운드 종료
        if (_currentWaveIndex < 0 || _currentWaveIndex >= roundData.waveDataList.Count)
        {
            OnRoundCompleted();
            return;
        }

        // WaveData 가져오기 및 유효성 검사
        WaveData currentWave = roundData.waveDataList[_currentWaveIndex];
        if (currentWave == null)
        {
            Debug.LogWarning($"[MainGameManager] Wave data at index {_currentWaveIndex} is null. Skipping to next wave.");
            _currentWaveIndex++;
            StartCurrentWave(); // 재귀 호출로 다음 웨이브 시도
            return;
        }

        _activeWaves.Clear();
        
        // 네트워크 변수 업데이트 (서버만, 웨이브 인덱스)
        if (_runner != null && _runner.IsServer && Object != null && Object.IsValid)
        {
            NetworkWaveIndex = _currentWaveIndex;
        }
        
        StartWave(currentWave, _currentRoundEnemySpawners, _currentRoundGoalSpawners);
    }
    
    /// <summary>
    /// 특정 웨이브를 시작합니다.
    /// </summary>
    /// <param name="waveData">시작할 웨이브 데이터</param>
    /// <param name="enemySpawners">사용할 EnemySpawner 리스트</param>
    /// <param name="goalSpawners">사용할 GoalSpawner 리스트 (Collect 목표일 때 사용, null이면 사용 안 함)</param>
    public void StartWave(WaveData waveData, List<EnemySpawner> enemySpawners, List<GoalSpawner> goalSpawners = null)
    {
        if (waveData == null)
        {
            Debug.LogError("[MainGameManager] Wave data is null!");
            return;
        }
        
        int waveId = waveData.GetHashCode();
        if (waveData.waveGoalCount > 0 && !_activeWaves.ContainsKey(waveId))
        {
            _activeWaves[waveId] = new WaveProgress(waveData);
            UpdateWaveGoalUI(waveData, _activeWaves[waveId]);
        }

        if (waveData.enemySpawnDataList != null && waveData.enemySpawnDataList.Count > 0 &&
            enemySpawners != null && enemySpawners.Count > 0)
        {
            foreach (var spawnData in waveData.enemySpawnDataList)
            {
                if (spawnData.spawnerIndex >= 0 && spawnData.spawnerIndex < enemySpawners.Count)
                {
                    EnemySpawner spawner = enemySpawners[spawnData.spawnerIndex];
                    if (spawner != null)
                    {
                        WaveData singleSpawnWave = new WaveData
                        {
                            waveGoalType = waveData.waveGoalType,
                            waveGoalCount = waveData.waveGoalCount,
                            enemySpawnDataList = new List<EnemySpawnData> { spawnData }
                        };
                        spawner.SpawnWave(singleSpawnWave);
                    }
                    else
                    {
                        Debug.LogWarning($"[MainGameManager] EnemySpawner at index {spawnData.spawnerIndex} is null!");
                    }
                }
                else
                {
                    Debug.LogWarning($"[MainGameManager] Invalid spawnerIndex {spawnData.spawnerIndex} (Available spawners: {enemySpawners.Count})");
                }
            }
        }

        // Collect 목표 타입인 경우 GoalSpawner들에 목표 오브젝트 스폰 요청
        if (waveData.waveGoalType == WaveGoalType.Collect && goalSpawners != null && goalSpawners.Count > 0)
        {
            foreach (var goalSpawner in goalSpawners)
            {
                if (goalSpawner != null)
                {
                    goalSpawner.SpawnGoals(waveData);
                }
            }
        }
    }

    /// <summary>
    /// Kill/Collect 타입 웨이브의 목표 진행 카운트를 증가시킵니다. (외부에서 호출 예정)
    /// - Kill: 적 처치 시 1 증가
    /// - Collect: 목표 오브젝트 수집 시 1 증가
    /// Survive 타입은 시간을 기반으로 자동 처리됩니다.
    /// </summary>
    /// <param name="waveData">진행을 업데이트할 웨이브 데이터</param>
    /// <param name="amount">증가할 양 (기본값: 1)</param>
    public void AddWaveGoalProgress(WaveData waveData, int amount = 1)
    {
        if (waveData == null)
        {
            Debug.LogWarning("[MainGameManager] AddWaveGoalProgress called with null WaveData");
            return;
        }
        
        // Survive 타입은 이 메서드 대신 시간으로만 진행합니다.
        if (waveData.waveGoalType == WaveGoalType.Survive)
        {
            Debug.LogWarning("[MainGameManager] AddWaveGoalProgress called for Survive wave. Survive goals are time-based.");
            return;
        }
        
        int waveId = waveData.GetHashCode();
        
        if (!_activeWaves.ContainsKey(waveId))
        {
            Debug.LogWarning("[MainGameManager] Wave is not being tracked!");
            return;
        }
        
        WaveProgress progress = _activeWaves[waveId];
        if (progress.isCompleted)
        {
            return; // 이미 완료된 웨이브
        }
        
        progress.currentGoalCount += amount;
        UpdateWaveGoalUI(waveData, progress);
        
        // 목표 달성 확인
        if (progress.currentGoalCount >= progress.waveData.waveGoalCount)
        {
            OnWaveGoalCompleted(waveData);
        }
    }

    /// <summary>
    /// 적이 사망했을 때 호출됩니다.
    /// 현재 활성화된 Kill 타입 웨이브들의 진행도를 1 증가시킵니다.
    /// </summary>
    public void OnEnemyKilled()
    {
        if (_activeWaves == null || _activeWaves.Count == 0) return;

        List<WaveData> killWaves = new List<WaveData>();

        foreach (var kvp in _activeWaves)
        {
            WaveProgress progress = kvp.Value;
            if (progress == null || progress.isCompleted) continue;

            WaveData waveData = progress.waveData;
            if (waveData == null) continue;

            if (waveData.waveGoalType == WaveGoalType.Kill)
            {
                killWaves.Add(waveData);
            }
        }

        foreach (var waveData in killWaves)
        {
            AddWaveGoalProgress(waveData, 1);
        }
    }
    
    /// <summary>
    /// 웨이브 목표가 완료되었을 때 호출됩니다.
    /// </summary>
    /// <param name="waveData">완료된 웨이브 데이터</param>
    private void OnWaveGoalCompleted(WaveData waveData)
    {
        if (waveData == null)
        {
            return;
        }
        
        int waveId = waveData.GetHashCode();
        
        if (!_activeWaves.ContainsKey(waveId))
        {
            return;
        }
        
        WaveProgress progress = _activeWaves[waveId];
        progress.isCompleted = true;
        
        CleanupCurrentWaveObjects();
        _currentWaveIndex++;
        
        // 다음 웨이브 시작 (또는 라운드 완료)
        StartCurrentWave();
    }
    
    /// <summary>
    /// Survive 타입 웨이브의 생존 시간 목표를 갱신합니다. (Update에서 deltaTime으로 호출)
    /// </summary>
    /// <param name="deltaTime">경과 시간 (초)</param>
    private void UpdateSurviveWaveGoals(float deltaTime)
    {
        if (_activeWaves == null || _activeWaves.Count == 0) return;

        List<WaveData> completedWaves = null;

        foreach (var kvp in _activeWaves)
        {
            WaveProgress progress = kvp.Value;
            if (progress == null || progress.isCompleted) continue;

            WaveData waveData = progress.waveData;
            if (waveData == null || waveData.waveGoalType != WaveGoalType.Survive) continue;

            progress.elapsedTime += deltaTime;
            UpdateWaveGoalUI(waveData, progress);

            if (progress.elapsedTime >= waveData.waveGoalCount)
            {
                progress.isCompleted = true;
                
                if (completedWaves == null) completedWaves = new List<WaveData>();
                completedWaves.Add(waveData);
            }
        }

        if (completedWaves != null && completedWaves.Count > 0)
        {
            CleanupCurrentWaveObjects();
            _currentWaveIndex++;
            StartCurrentWave();
        }
    }
    
    /// <summary>
    /// 라운드가 완료되었을 때 호출됩니다.
    /// </summary>
    private void OnRoundCompleted()
    {
        CleanupCurrentWaveObjects();

        _currentRoundEnemySpawners.Clear();
        _currentRoundGoalSpawners.Clear();

        // 웨이브 추적 초기화
        _activeWaves.Clear();
        
        // 문 열기
        OpenMapDoor();
        
        // 라운드 인덱스 리셋
        _currentRoundIndex = -1;
        _currentWaveIndex = -1;
        
        // 네트워크 변수 리셋 (서버만)
        if (_runner != null && _runner.IsServer && Object != null && Object.IsValid)
        {
            ResetNetworkedWaveVariables();
        }
        
        // UI 초기화
        if (GameManager.Instance?.Canvas is MainCanvas canvas)
        {
            canvas.SetWaveText(string.Empty);
            canvas.SetGoalText(string.Empty);
        }

    }
    
    /// <summary>
    /// 현재 웨이브/라운드 및 목표 상태를 UI(MainCanvas)로 전달합니다.
    /// </summary>
    private void UpdateWaveGoalUI(WaveData waveData, WaveProgress progress)
    {
        if (GameManager.Instance?.Canvas is not MainCanvas canvas || waveData == null || progress == null)
        {
            return;
        }

        if (_currentStageData == null)
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            _currentStageData = GetStageDataBySceneName(sceneName);
        }

        int roundIndex = _currentRoundIndex;
        int waveIndex = -1;

        if (_currentStageData != null &&
            roundIndex >= 0 && roundIndex < _currentStageData.roundDataList.Count)
        {
            var roundData = _currentStageData.roundDataList[roundIndex];
            if (roundData != null && roundData.waveDataList != null)
            {
                waveIndex = roundData.waveDataList.IndexOf(waveData);
            }
        }

        string waveLabel = (roundIndex >= 0 && waveIndex >= 0)
            ? $"Wave {waveIndex + 1}"
            : "Wave";

        canvas.SetWaveText(waveLabel);

        // 목표 텍스트 구성
        string goalText = string.Empty;
        switch (waveData.waveGoalType)
        {
            case WaveGoalType.Kill:
                goalText = $"Kill {progress.currentGoalCount}/{waveData.waveGoalCount}";
                break;
            case WaveGoalType.Collect:
                goalText = $"Collect {progress.currentGoalCount}/{waveData.waveGoalCount}";
                break;
            case WaveGoalType.Survive:
                int elapsedSec = Mathf.FloorToInt(progress.elapsedTime);
                goalText = $"Survive {elapsedSec}/{waveData.waveGoalCount} sec";
                break;
            default:
                goalText = string.Empty;
                break;
        }

        canvas.SetGoalText(goalText);
        
        if (_runner != null && _runner.IsServer && _runner.IsRunning)
        {
            UpdateNetworkedWaveVariables(
                roundIndex,
                waveIndex,
                waveData.waveGoalType,
                progress.currentGoalCount,
                waveData.waveGoalCount,
                progress.elapsedTime
            );
        }
    }
    

    /// <summary>
    /// 현재 라운드에서 생성된 적 및 목표 오브젝트를 정리합니다.
    /// </summary>
    private void CleanupCurrentWaveObjects()
    {
        if (_currentRoundEnemySpawners != null)
        {
            foreach (var spawner in _currentRoundEnemySpawners)
            {
                if (spawner != null)
                {
                    spawner.KillAllEnemies();
                }
            }
        }

        if (_currentRoundGoalSpawners != null)
        {
            foreach (var goalSpawner in _currentRoundGoalSpawners)
            {
                if (goalSpawner != null)
                {
                    goalSpawner.DestroyAllGoals();
                }
            }
        }
    }
}


