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
public partial class MainGameManager : NetworkBehaviour, ISceneGameManager
{
    public static MainGameManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private bool _isTestMode = false;
    public bool IsTestMode => _isTestMode;

    [Header("References")]
    [SerializeField] private NetworkPrefabRef _playerPrefab;
    [SerializeField] private StageData _currentStageData;
    [SerializeField] private List<RoundTrigger> _roundTriggers = new List<RoundTrigger>();
    
    [Header("Prefabs")]
    [Tooltip("메인 카메라 프리팹 (MainCameraController 컴포넌트 포함, 없으면 자동 생성)")]
    [SerializeField] private GameObject _mainCameraPrefab;
    [Tooltip("Canvas 프리팹 (타이틀 씬이 아닐 때 자동 생성)")]
    [SerializeField] private GameObject _canvasPrefab;
    
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
    
    private int _currentRoundIndex = -1;
    private int _currentWaveIndex = -1;
    
    private List<EnemySpawner> _currentRoundEnemySpawners = new List<EnemySpawner>();
    private List<GoalSpawner> _currentRoundGoalSpawners = new List<GoalSpawner>();
    private List<RoundDoorNetworkController> _currentRoundDoorObjects = new List<RoundDoorNetworkController>();
    private List<GameObject> _currentRoundEndActiveObject = new List<GameObject>();
    
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

        if (_isTestMode)
        {
            _ = StartTestSession();
        }
    }

    #endregion
    
    #region ISceneManager Implementation
    
    /// <summary>
    /// ISceneManager 인터페이스 구현: GameManager에서 호출되는 초기화 메서드
    /// MainGameManager는 NetworkBehaviour이므로 실제 초기화는 Spawned()에서 진행됩니다.
    /// 하지만 Canvas는 네트워크와 무관하므로 여기서 초기화합니다.
    /// </summary>
    public void Initialize(GameManager gameManager, GameDataManager gameDataManager)
    {
        // 필수 컴포넌트 자동 생성 (타이틀 씬 제외)
        EnsureRequiredComponents();
        
        // MainCanvas 찾기 및 초기화 (씬 매니저가 직접 초기화)
        InitializeMainCanvas(gameManager, gameDataManager);
        
        Debug.Log("[MainGameManager] ISceneManager.Initialize called. Canvas initialized. Full game initialization will happen in Spawned().");
    }
    
    /// <summary>
    /// 필수 컴포넌트(카메라, Canvas)가 없을 경우 자동 생성합니다.
    /// </summary>
    private void EnsureRequiredComponents()
    {
        // 타이틀 씬인지 확인
        string currentSceneName = SceneManager.GetActiveScene().name;
        bool isTitleScene = currentSceneName == "Title";
        
        // 1. 메인 카메라 확인 및 생성
        EnsureMainCamera();
        
        // 2. Canvas 확인 및 생성 (타이틀 씬 제외)
        if (!isTitleScene)
        {
            EnsureCanvas();
        }
    }
    
    /// <summary>
    /// MainCameraController가 붙어있는 메인 카메라가 없을 경우 생성합니다.
    /// </summary>
    private void EnsureMainCamera()
    {
        // MainCameraController가 붙어있는 카메라 찾기
        MainCameraController existingController = FindObjectOfType<MainCameraController>();
        
        if (existingController != null)
        {
            // 이미 존재하면 태그 확인 및 설정
            Camera cam = existingController.GetComponent<Camera>();
            if (cam != null && !cam.CompareTag("MainCamera"))
            {
                cam.tag = "MainCamera";
            }
            return;
        }
        
        // 프리팹이 있으면 프리팹으로 생성
        if (_mainCameraPrefab != null)
        {
            GameObject cameraObj = Instantiate(_mainCameraPrefab);
            cameraObj.name = "Main Camera";
            
            // 태그 설정
            cameraObj.tag = "MainCamera";
            
            // MainCameraController 확인 및 추가
            if (!cameraObj.TryGetComponent(out MainCameraController _))
            {
                cameraObj.AddComponent<MainCameraController>();
            }
            
            Debug.Log("[MainGameManager] Main Camera created from prefab.");
            return;
        }
        
        // 프리팹이 없으면 기본 카메라 생성
        GameObject newCamera = new GameObject("Main Camera");
        newCamera.tag = "MainCamera";
        
        Camera camera = newCamera.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        
        // MainCameraController 추가
        newCamera.AddComponent<MainCameraController>();
        
        Debug.Log("[MainGameManager] Main Camera created (default settings).");
    }
    
    /// <summary>
    /// Canvas가 없을 경우 Canvas 프리팹을 생성합니다. (타이틀 씬 제외)
    /// </summary>
    private void EnsureCanvas()
    {
        // MainCanvas 찾기 (MainCanvas는 Canvas를 상속받거나 포함함)
        MainCanvas existingCanvas = FindObjectOfType<MainCanvas>();
        
        if (existingCanvas != null)
        {
            return; // 이미 MainCanvas가 존재함
        }
        
        // 일반 Canvas도 확인 (MainCanvas가 아닌 Canvas가 있을 수 있음)
        Canvas generalCanvas = FindObjectOfType<Canvas>();
        if (generalCanvas != null)
        {
            // MainCanvas 컴포넌트가 있는지 다시 확인
            if (generalCanvas.GetComponent<MainCanvas>() == null)
            {
                Debug.LogWarning("[MainGameManager] Canvas found but it's not a MainCanvas. Consider using MainCanvas prefab.");
            }
            return; // Canvas가 이미 존재함
        }
        
        // 프리팹이 없으면 경고 후 종료
        if (_canvasPrefab == null)
        {
            Debug.LogWarning("[MainGameManager] Canvas prefab is not assigned! Please assign Canvas.prefab in inspector.");
            return;
        }
        
        // 프리팹으로 Canvas 생성
        GameObject canvasObj = Instantiate(_canvasPrefab);
        canvasObj.name = "Canvas";
        
        Debug.Log("[MainGameManager] Canvas created from prefab.");
    }
    
    /// <summary>
    /// MainCanvas를 찾아서 초기화합니다.
    /// </summary>
    private void InitializeMainCanvas(GameManager gameManager, GameDataManager gameDataManager)
    {
        // FindObjectOfType 사용 (GameObject.Find보다 효율적)
        MainCanvas mainCanvas = FindObjectOfType<MainCanvas>();
        
        if (mainCanvas != null)
        {
            // ICanvas 인터페이스를 통해 초기화
            ICanvas canvas = mainCanvas as ICanvas;
            if (canvas != null)
            {
                canvas.OnInitialize(gameManager, gameDataManager);
                Debug.Log("[MainGameManager] MainCanvas initialized successfully.");
            }
            else
            {
                Debug.LogError("[MainGameManager] MainCanvas does not implement ICanvas!");
            }
        }
        else
        {
            Debug.LogWarning("[MainGameManager] MainCanvas not found. Some UI features may not work.");
        }
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