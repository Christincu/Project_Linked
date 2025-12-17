using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement; // SceneManager 사용을 위해 추가
using Fusion;
using Fusion.Sockets; // NetDisconnectReason 사용을 위해 추가
using WaveGoalTypeEnum = WaveGoalType;
using System.Threading.Tasks;

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

    [Header("Prefabs")]
    [SerializeField] private GameObject _mainCameraPrefab;
    [SerializeField] private GameObject _canvasPrefab;

    [System.NonSerialized]
    private NetworkRunner _runner;
    private ChangeDetector _changeDetector;

    private MainCanvas _mainCanvas;
    private MainCameraController _mainCameraController;


    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnregisterEvents();
    }
    
    /// <summary>
    /// ISceneManager 인터페이스 구현: GameManager에서 호출되는 초기화 메서드
    /// </summary>
    public void OnInitialize(GameManager gameManager, GameDataManager gameDataManager)
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

        MainCanvas canvas = FindObjectOfType<MainCanvas>();
        if(canvas == null)
        {
            canvas = Instantiate(_canvasPrefab).GetComponent<MainCanvas>();
        }
        _mainCanvas = canvas;

        MainCameraController camera = FindObjectOfType<MainCameraController>();
        if(camera == null)
        {
            camera = Instantiate(_mainCameraPrefab).GetComponent<MainCameraController>();
        }
        _mainCameraController = camera;

        if (_isTestMode)
        {
            StartTestSession();
        }
    }

    /// <summary>
    /// Unity Update - 로컬 입력 처리를 위해 사용 (테스트 모드)
    /// </summary>
    private void Update()
    {
        if (_isTestMode)
        {
            HandleTestModeInput();
        }
    }

    public override void Spawned()
    {
        base.Spawned();
        
        _runner = Runner;
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

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

    private void UnregisterEvents()
    {
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoinedDuringGame;
        FusionManager.OnPlayerLeftEvent -= OnPlayerLeft;
        FusionManager.OnShutdownEvent -= OnNetworkShutdown;
        FusionManager.OnDisconnectedEvent -= OnDisconnected;
    }

    public void SetStageData(StageData stageData) => _currentStageData = stageData;
    public StageData GetCurrentStageData() => _currentStageData;
    public int GetCurrentRoundIndex() => RoundIndex;
}