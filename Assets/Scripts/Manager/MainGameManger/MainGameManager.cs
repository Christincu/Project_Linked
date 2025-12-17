using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using Fusion.Sockets;
using WaveGoalTypeEnum = WaveGoalType; // enum 정의가 어딘가에 있다고 가정

/// <summary>
/// 게임의 전반적인 상태(스테이지, 웨이브, 플레이어 관리)를 총괄하는 매니저입니다.
/// (Core 로직: 상태 변수, 생명주기, 변경 감지)
/// </summary>
public partial class MainGameManager : NetworkBehaviour, ISceneGameManager
{
    [Header("Settings")]
    [SerializeField] private bool _isTestMode = false;
    public bool IsTestMode => _isTestMode;

    [Header("Prefabs")]
    [SerializeField] private GameObject _mainCameraPrefab;
    [SerializeField] private GameObject _canvasPrefab;
    [System.NonSerialized] private NetworkRunner _runner;

    private ChangeDetector _changeDetector;
    private MainCanvas _mainCanvas;
    private MainCameraController _mainCameraController;
    private CinemachineCameraController _cinemachineCameraController;

    private GameManager _gameManager;
    private GameDataManager _gameDataManager;

    // 플레이어 스폰 관리
    private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();

    private void OnDestroy()
    {
        UnregisterEvents();
    }

    /// <summary>
    /// ISceneManager 인터페이스: 씬 로드 직후 호출 (MonoBehaviour Awake/Start 단계)
    /// </summary>
    public void OnInitialize(GameManager gameManager, GameDataManager gameDataManager)
    {
        _gameManager = gameManager;
        _gameDataManager = gameDataManager;

        SetupDependencies();

        if (_isTestMode)
        {
            if (FusionManager.LocalRunner != null && !FusionManager.LocalRunner.IsShutdown)
            {
                Debug.Log("[MainGameManager] active Runner found (Running or Starting). Skipping StartTestSession.");
                _runner = FusionManager.LocalRunner;
            }
            else
            {
                StartTestSession();
            }
        }
    }

    private void SetupDependencies()
    {
        _mainCanvas = FindObjectOfType<MainCanvas>();
        if (_mainCanvas == null && _canvasPrefab != null)
            _mainCanvas = Instantiate(_canvasPrefab).GetComponent<MainCanvas>();
        _mainCanvas.OnInitialize(_gameManager, _gameDataManager);
        _mainCanvas.SetMainGameManager(this);

        _mainCameraController = FindObjectOfType<MainCameraController>();
        if (_mainCameraController == null && _mainCameraPrefab != null)
            _mainCameraController = Instantiate(_mainCameraPrefab).GetComponent<MainCameraController>();
        if (_mainCameraController != null)
            _mainCameraController.OnInitialize(this);

        _cinemachineCameraController = FindObjectOfType<CinemachineCameraController>();
        if (_cinemachineCameraController != null)
            _cinemachineCameraController.OnInitialize(this);

        InitializeSceneObjects();
    }

    private void Update()
    {
        if (_isTestMode)
        {
            HandleTestModeInput();
        }
    }

    public override void Spawned()
    {
        Debug.Log("[MainGameManager] Spawned()");
        base.Spawned();

        if (_isTestMode)
        {
            _runner = FusionManager.LocalRunner;
        }
        else
        {
            _runner = Runner;
        }

        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        RegisterEvents();

        if (_isTestMode && _runner != null && _runner.IsRunning && _runner.IsServer)
        {
            Debug.Log("[MainGameManager] Spawning test players in Spawned()");
            SpawnTestPlayers();
        }
        else if (!_isTestMode && _runner != null && _runner.IsServer)
        {
            StartCoroutine(Co_InitializeGameSession());
        }
    }

    public override void Render()
    {
        if (_changeDetector == null) return;

        bool uiNeedsUpdate = false;

        // 변경 감지 (네트워크 변수가 변했을 때만 UI 업데이트)
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

        // UI 갱신 조건: 데이터가 변경되었거나, 시간(Survive)이 흐르고 있을 때
        if (uiNeedsUpdate || (WaveElapsedTime > 0 && WaveGoalType >= 0))
        {
            if (GameManager.Instance != null && GameManager.Instance.Canvas is MainCanvas canvas)
            {
                UpdateWaveUI(canvas);
            }
            else if (_mainCanvas != null)
            {
                UpdateWaveUI(_mainCanvas);
            }
        }
    }

    private void UpdateWaveUI(MainCanvas canvas)
    {
        if (canvas == null) return;

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

    private void RegisterEvents()
    {
        FusionManager.OnPlayerJoinedEvent += OnPlayerJoinedDuringGame;
        FusionManager.OnPlayerLeftEvent += OnPlayerLeft;
        FusionManager.OnShutdownEvent += OnNetworkShutdown;
        FusionManager.OnDisconnectedEvent += OnDisconnected;
    }

    private void UnregisterEvents()
    {
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoinedDuringGame;
        FusionManager.OnPlayerLeftEvent -= OnPlayerLeft;
        FusionManager.OnShutdownEvent -= OnNetworkShutdown;
        FusionManager.OnDisconnectedEvent -= OnDisconnected;
    }

    /// <summary>
    /// 플레이어가 떠났을 때 호출됩니다.
    /// </summary>
    public void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        bool wasInDictionary = _spawnedPlayers.ContainsKey(player);
        
        if (runner.IsServer)
        {
            PlayerData playerData = GameManager.Instance?.GetPlayerData(player, runner);
            if (playerData != null && playerData.Object != null && playerData.Object.IsValid)
            {
                runner.Despawn(playerData.Object);
            }
        }
        
        if (_spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
        {
            if (playerObject != null && playerObject.IsValid && runner.IsServer)
            {
                runner.Despawn(playerObject);
            }
            _spawnedPlayers.Remove(player);
        }
        
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetPlayerData(player, null);
        }
        
        if (!_isTestMode && SceneManager.GetActiveScene().name == "Main")
        {
            if (runner != null && runner.IsRunning)
            {
                int remainingPlayers = runner.ActivePlayers.Count();
                if (remainingPlayers < 2)
                {
                    GameManager.Instance?.ShowWarningPanel("상대방이 나갔습니다.");
                }
            }
        }
    }
    
    /// <summary>
    /// 네트워크 세션이 종료되었을 때 호출됩니다 (호스트가 나가거나 세션이 종료됨)
    /// </summary>
    private void OnNetworkShutdown(NetworkRunner runner)
    {
        HandleNetworkDisconnection(runner, "Network shutdown detected");
    }

    /// <summary>
    /// 서버와의 연결이 끊어졌을 때 호출됩니다 (클라이언트 관점)
    /// </summary>
    private void OnDisconnected(NetworkRunner runner)
    {
        HandleNetworkDisconnection(runner, "Disconnected from server");
    }
    
    /// <summary>
    /// 네트워크 연결 종료 공통 처리
    /// </summary>
    private void HandleNetworkDisconnection(NetworkRunner runner, string reason)
    {
        if (!_isTestMode && runner != null && !runner.IsServer)
        {
            GameManager.Instance?.ShowWarningPanel("호스트가 나갔습니다. 타이틀로 돌아갑니다.");
        }
        
        if (!_isTestMode)
        {
            StartCoroutine(ReturnToTitleAfterDelay());
        }
    }

    /// <summary>
    /// 일정 시간 후 타이틀 씬으로 돌아갑니다.
    /// </summary>
    private IEnumerator ReturnToTitleAfterDelay()
    {
        yield return new WaitForSeconds(2f);
        
        if (_runner != null)
        {
            _runner.Shutdown();
        }
        
        SceneManager.LoadScene("Title");
    }
}