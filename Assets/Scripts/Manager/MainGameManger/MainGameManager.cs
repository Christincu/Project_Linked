using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement; // SceneManager 사용을 위해 추가
using Fusion;
using Fusion.Sockets; // NetDisconnectReason 사용을 위해 추가
using WaveGoalTypeEnum = WaveGoalType;

/// <summary>
/// 게임의 전반적인 상태(스테이지, 웨이브, 플레이어 관리)를 총괄하는 매니저입니다.
/// (Core 로직: 상태 변수, 생명주기, 변경 감지)
/// </summary>
public partial class MainGameManager : NetworkBehaviour
{
    public static MainGameManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private bool _isTestMode = false;
    public bool IsTestMode => _isTestMode;

    [Header("References")]
    [SerializeField] private NetworkPrefabRef _playerPrefab;
    [SerializeField] private StageData _currentStageData;
    [SerializeField] private List<RoundTrigger> _roundTriggers = new List<RoundTrigger>();
    
    // Spawn Positions
    [SerializeField] private Vector2[] _spawnPositions = new Vector2[]
    {
        new Vector2(0, 2), new Vector2(0, -2)
    };

    // Internal State
    [System.NonSerialized] private NetworkRunner _runner;
    [System.NonSerialized] private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    [System.NonSerialized] private ChangeDetector _changeDetector;
    
    // Test Mode Vars
    public static int SelectedSlot = 0;
    [System.NonSerialized] private NetworkObject _playerObj1;
    [System.NonSerialized] private NetworkObject _playerObj2;

    // ========================================================================
    // Networked Game State
    // ========================================================================
    [Networked] public int RoundIndex { get; private set; } = -1;
    [Networked] public int WaveIndex { get; private set; } = -1;
    
    [Networked] public int WaveGoalType { get; private set; } = -1;
    [Networked] public int WaveCurrentGoal { get; private set; } = 0;
    [Networked] public int WaveTotalGoal { get; private set; } = 0;
    [Networked] public float WaveElapsedTime { get; private set; } = 0f;

    // ========================================================================
    // Local Logic State
    // ========================================================================
    private Dictionary<int, WaveProgress> _activeWaves = new Dictionary<int, WaveProgress>();
    private bool _isMapDoorClosed = false;
    
    private int _currentRoundIndex = -1;
    private int _currentWaveIndex = -1;
    
    private List<EnemySpawner> _currentRoundEnemySpawners = new List<EnemySpawner>();
    private List<GoalSpawner> _currentRoundGoalSpawners = new List<GoalSpawner>();
    private List<RoundDoorNetworkController> _currentRoundDoorObjects = new List<RoundDoorNetworkController>();
    
    [SerializeField] private int _firstCharacterIndex = 0;
    [SerializeField] private int _secondCharacterIndex = 1;
    
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

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnregisterEvents();
    }

    private void Start()
    {
        RegisterEvents();
        
        _ = BarrierVisualizationManager.Instance;

        // [수정] Start에서는 테스트 모드만 처리합니다.
        // 일반 모드는 Spawned()에서 초기화를 시작합니다.
        if (_isTestMode)
        {
            _ = StartTestSession();
        }
        // 일반 모드는 Spawned()에서 Co_InitializeGameSession_Direct()를 호출합니다.
    }

    #endregion

    #region Unity Update

    /// <summary>
    /// Unity Update - 로컬 입력 처리를 위해 사용 (테스트 모드)
    /// 주의: 네트워크 동기화가 필요한 로직은 FixedUpdateNetwork에서 처리해야 합니다.
    /// </summary>
    private void Update()
    {
        // 테스트 모드 입력 처리 (Unity Update에서 처리해야 Input.GetKeyDown이 제대로 작동함)
        // FixedUpdateNetwork는 네트워크 틱 단위로 실행되므로 입력 반응이 느립니다.
        if (_isTestMode)
        {
            HandleTestModeInput();
        }
    }

    #endregion

    #region Fusion Lifecycle & Logic

    public override void Spawned()
    {
        base.Spawned();
        
        _runner = Runner;
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        if (_roundTriggers == null || _roundTriggers.Count == 0)
        {
            _roundTriggers = new List<RoundTrigger>();
            _roundTriggers.AddRange(FindObjectsOfType<RoundTrigger>());
        }

        // [핵심] Runner가 확실히 존재하는 이 시점에 바로 초기화 코루틴을 시작합니다.
        // 테스트 모드가 아닐 때만 실행 (테스트 모드는 Start()에서 처리)
        if (!_isTestMode)
        {
            StartCoroutine(Co_InitializeGameSession_Direct());
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (_runner != null && _runner.IsServer && _activeWaves.Count > 0)
        {
            UpdateSurviveWaveGoals(_runner.DeltaTime);
        }
        
        // 주의: 테스트 모드 입력 처리는 Unity Update()에서 처리합니다.
        // FixedUpdateNetwork는 네트워크 틱 단위로 실행되므로 Input.GetKeyDown이 제대로 작동하지 않습니다.
    }

    public override void Render()
    {
        if (_changeDetector == null) return;

        bool uiNeedsUpdate = false;

        foreach (var change in _changeDetector.DetectChanges(this))
        {
            switch (change)
            {
                case nameof(RoundIndex):
                case nameof(WaveIndex):
                case nameof(WaveGoalType):
                case nameof(WaveCurrentGoal):
                case nameof(WaveTotalGoal):
                    uiNeedsUpdate = true;
                    break;
            }
        }

        if (uiNeedsUpdate || (WaveElapsedTime > 0 && WaveGoalType >= 0)) 
        {
            if (GameManager.Instance != null && GameManager.Instance.Canvas is MainCanvas canvas)
            {
                UpdateWaveUI(canvas);
            }
        }
    }

    #endregion

    #region UI Synchronization

    private void UpdateWaveUI(MainCanvas canvas)
    {
        if (canvas == null)
        {
            Debug.LogWarning("[MainGameManager] UpdateWaveUI: Canvas is null!");
            return;
        }
        
        string waveInfo = $"Wave: {WaveIndex + 1}";
        canvas.SetWaveText(waveInfo);

        string goalText = "";
        WaveGoalTypeEnum goalTypeEnum = (WaveGoalTypeEnum)this.WaveGoalType;
        switch (goalTypeEnum)
        {
            case WaveGoalTypeEnum.Kill:
                goalText = $"Kill: {WaveCurrentGoal}/{WaveTotalGoal}";
                break;
            case WaveGoalTypeEnum.Survive:
                float remaining = Mathf.Max(0, WaveTotalGoal - WaveElapsedTime);
                goalText = $"Survive: {remaining:F1}s";
                break;
            case WaveGoalTypeEnum.Collect:
                goalText = $"Collect: {WaveCurrentGoal}/{WaveTotalGoal}";
                break;
            default:
                goalText = "Ready";
                break;
        }
        canvas.SetGoalText(goalText);
    }

    #endregion

    #region Event Registration

    // RegisterEvents는 MainGameManager.NetworkEvents.cs에 정의됨
    // OnPlayerLeft, OnNetworkShutdown, OnDisconnected도 NetworkEvents.cs에 정의됨

    private void UnregisterEvents()
    {
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoinedDuringGame;
        FusionManager.OnPlayerLeftEvent -= OnPlayerLeft;
        FusionManager.OnShutdownEvent -= OnNetworkShutdown;
        FusionManager.OnDisconnectedEvent -= OnDisconnected;
    }

    #endregion

    #region Data Accessors

    public void SetStageData(StageData stageData) => _currentStageData = stageData;
    public StageData GetCurrentStageData() => _currentStageData;
    public int GetCurrentRoundIndex() => RoundIndex;

    #endregion
}