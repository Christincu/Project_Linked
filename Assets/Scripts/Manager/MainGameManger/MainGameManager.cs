using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

/// <summary>
/// 메인 게임 씬의 게임 관리자입니다.
/// 테스트 모드(로컬 다중 플레이어 시뮬레이션)와 실제 네트워크 모드를 모두 처리합니다.
/// </summary>
public partial class MainGameManager : NetworkBehaviour
{
    [Header("Mode & Settings")]
    [SerializeField] private bool _isTestMode = false;
    
    [Header("Player Prefabs & Data")]
    [SerializeField] private NetworkPrefabRef _playerPrefab;
    [SerializeField] private int _firstCharacterIndex = 0;
    [SerializeField] private int _secondCharacterIndex = 1;
    
    [Header("Spawn Settings")]
    [SerializeField] private Vector2[] _spawnPositions = new Vector2[]
    {
        new Vector2(0, 2),
        new Vector2(0, -2)
    };
    
    [Header("Stage Settings")]
    [Tooltip("현재 스테이지 데이터 (StageData)")]
    [SerializeField] private StageData _currentStageData;
    [SerializeField] private List<RoundTrigger> _roundTriggers = new List<RoundTrigger>();

    public static MainGameManager Instance { get; private set; }
    
    private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    [System.NonSerialized] private NetworkRunner _runner;
    
    public static int SelectedSlot = 0;
    private NetworkObject _playerObj1;
    private NetworkObject _playerObj2;
    public bool IsTestMode => _isTestMode;

    private bool _isMapDoorClosed = false;
    private Dictionary<int, WaveProgress> _activeWaves = new Dictionary<int, WaveProgress>();
    
    [Networked] public int NetworkRoundIndex { get; private set; } = -1;
    [Networked] public int NetworkWaveIndex { get; private set; } = -1;
    [Networked] public int NetworkWaveGoalType { get; private set; } = -1;
    [Networked] public int NetworkWaveCurrentGoal { get; private set; } = 0;
    [Networked] public int NetworkWaveTotalGoal { get; private set; } = 0;
    [Networked] public float NetworkWaveElapsedTime { get; private set; } = 0f;
    
    private ChangeDetector _changeDetector;
    
    private int _currentRoundIndex = -1;
    private int _currentWaveIndex = -1;
    
    private int _prevNetworkRoundIndex = -1;
    private int _prevNetworkWaveIndex = -1;
    private int _prevNetworkWaveGoalType = -1;
    private int _prevNetworkWaveCurrentGoal = 0;
    private int _prevNetworkWaveTotalGoal = 0;
    private float _prevNetworkWaveElapsedTime = 0f;
    private List<EnemySpawner> _currentRoundEnemySpawners = new List<EnemySpawner>();
    private List<GoalSpawner> _currentRoundGoalSpawners = new List<GoalSpawner>();
    private List<RoundDoorNetworkController> _currentRoundDoorObjects = new List<RoundDoorNetworkController>();
    
    private class WaveProgress
    {
        public WaveData waveData;
        public int currentGoalCount = 0;
        public float elapsedTime = 0f;
        public bool isCompleted = false;
        
        public WaveProgress(WaveData wave)
        {
            waveData = wave;
            currentGoalCount = 0;
            elapsedTime = 0f;
            isCompleted = false;
        }
    }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    public override void Spawned()
    {
        base.Spawned();
        
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        _runner = Runner;
        _currentRoundIndex = NetworkRoundIndex;
        _currentWaveIndex = NetworkWaveIndex;
    }
    
    async void Start()
    {
        RegisterEvents();
        _ = BarrierVisualizationManager.Instance;
        
        if (_isTestMode)
        {
            await StartTestSession();
        }
        else
        {
            StartCoroutine(Co_InitializeGameSession());
        }
    }
    
    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();
        
        bool hasChanges = false;
        
        if (_prevNetworkRoundIndex != NetworkRoundIndex)
        {
            _currentRoundIndex = NetworkRoundIndex;
            _prevNetworkRoundIndex = NetworkRoundIndex;
            hasChanges = true;
        }
        
        if (_prevNetworkWaveIndex != NetworkWaveIndex)
        {
            _currentWaveIndex = NetworkWaveIndex;
            _prevNetworkWaveIndex = NetworkWaveIndex;
            hasChanges = true;
        }
        
        if (_prevNetworkWaveGoalType != NetworkWaveGoalType ||
            _prevNetworkWaveCurrentGoal != NetworkWaveCurrentGoal ||
            _prevNetworkWaveTotalGoal != NetworkWaveTotalGoal ||
            Mathf.Abs(_prevNetworkWaveElapsedTime - NetworkWaveElapsedTime) > 0.1f)
        {
            _prevNetworkWaveGoalType = NetworkWaveGoalType;
            _prevNetworkWaveCurrentGoal = NetworkWaveCurrentGoal;
            _prevNetworkWaveTotalGoal = NetworkWaveTotalGoal;
            _prevNetworkWaveElapsedTime = NetworkWaveElapsedTime;
            hasChanges = true;
        }
        
        if (hasChanges)
        {
            UpdateUIFromNetworkedVariables();
        }
    }
    
    void Update()
    {
        if (_isTestMode)
        {
            HandleTestModeInput();
        }

        if (_activeWaves != null && _activeWaves.Count > 0 && Runner != null && Runner.IsServer)
        {
            UpdateSurviveWaveGoals(Time.deltaTime);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoinedDuringGame;
        FusionManager.OnPlayerLeftEvent -= OnPlayerLeft;
        FusionManager.OnShutdownEvent -= OnNetworkShutdown;
        FusionManager.OnDisconnectedEvent -= OnDisconnected;
    }

    /// <summary>
    /// 현재 스테이지 데이터를 설정합니다.
    /// </summary>
    public void SetStageData(StageData stageData)
    {
        _currentStageData = stageData;
    }
    
    /// <summary>
    /// 현재 스테이지 데이터를 가져옵니다.
    /// </summary>
    public StageData GetCurrentStageData() => _currentStageData;
    
    /// <summary>
    /// 현재 진행 중인 라운드 인덱스를 반환합니다.
    /// </summary>
    public int GetCurrentRoundIndex() => Runner != null && Runner.IsServer ? NetworkRoundIndex : _currentRoundIndex;
}
