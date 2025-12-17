using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using System;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
    public static string MyLocalNickname = "Player";
    public static int MyLocalCharacterIndex = 0;

    public GameStateType State { get; private set; } = GameStateType.Lobby;
    public ICanvas Canvas { get; private set; }
    public ISceneGameManager CurrentSceneManager { get; private set; }

    [Header("Core Manager Prefabs")]
    [SerializeField] private GameObject _gameDataManagerPrefab;
    [SerializeField] private GameObject _fusionManagerPrefab;
    [SerializeField] private GameObject _visualManagerPrefab;

    [Header("UI & Scene Prefabs")]
    [SerializeField] private GameObject _mainGameManagerPrefab;
    [SerializeField] private GameObject _warningPanelPrefab;
    [SerializeField] private GameObject _loadingPanelPrefab;

    private Dictionary<PlayerRef, PlayerData> _playerData = new Dictionary<PlayerRef, PlayerData>();

    private GameDataManager _gameDataManager;
    private FusionManager _fusionManager;
    private VisualManager _visualManager;

    private LoadingPanel _loadingPanel;

    public void SetGameState(GameStateType newState) => State = newState;
    public void ExitGame() => Application.Quit();
    public void StartLoadingScreen() => LoadingPanel.Show();
    public void FinishLoadingScreen() => LoadingPanel.Hide();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        InitializeCoreManagers();
        SetupNetworkEvents();
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (LoadingPanel.Instance == null && _loadingPanelPrefab != null)
        {
            GameObject obj = Instantiate(_loadingPanelPrefab);
            _loadingPanel = obj.GetComponent<LoadingPanel>();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            RemoveNetworkEvents();
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    private void InitializeCoreManagers()
    {
        if(_gameDataManagerPrefab != null && GameDataManager.Instance == null)
        {
            GameObject obj = Instantiate(_gameDataManagerPrefab);
            _gameDataManager = obj.GetComponent<GameDataManager>();
            _gameDataManager.OnInitialize(this);
        }
        else
        {
            Debug.LogWarning("GameDataManager prefab is not assigned or instance already exists.");
        }

        if (_fusionManagerPrefab != null && FusionManager.Instance == null)
        {
            GameObject obj = Instantiate(_fusionManagerPrefab);
            _fusionManager = obj.GetComponent<FusionManager>();
            _fusionManager.OnInitialize(this);
        }
        else
        {
            Debug.LogWarning("FusionManager prefab is not assigned or instance already exists.");
        }

        if (_visualManagerPrefab != null && VisualManager.Instance == null)
        {
            GameObject obj = Instantiate(_visualManagerPrefab);
            _visualManager = obj.GetComponent<VisualManager>();
            _visualManager.OnInitialize(this);
        }
        else
        {
            Debug.LogWarning("VisualManager prefab is not assigned or instance already exists.");
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        GameObject obj = GameObject.Find("SceneGameManager");
        CurrentSceneManager = obj.GetComponent<ISceneGameManager>();
        CurrentSceneManager.OnInitialize(this, _gameDataManager);

        GameObject canvasObj = GameObject.Find("Canvas");
        if (canvasObj != null && canvasObj.TryGetComponent(out ICanvas canvas))
        {
            Canvas = canvas;
            Canvas.OnInitialize(this, _gameDataManager);
        }
    }

    private void SetupNetworkEvents()
    {
        FusionManager.OnPlayerJoinedEvent += OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent += OnPlayerLeft;
        FusionManager.OnShutdownEvent += OnNetworkShutdown;
        FusionManager.OnDisconnectedEvent += OnNetworkShutdown;
    }

    private void RemoveNetworkEvents()
    {
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent -= OnPlayerLeft;
        FusionManager.OnShutdownEvent -= OnNetworkShutdown;
        FusionManager.OnDisconnectedEvent -= OnNetworkShutdown;
    }

    private void OnPlayerJoined(PlayerRef player, NetworkRunner runner)
    {
        if (!_playerData.ContainsKey(player))
        {
            _playerData[player] = null;
        }
    }

    private void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        _playerData.Remove(player);
    }

    private void OnNetworkShutdown(NetworkRunner runner)
    {
        if (runner == FusionManager.LocalRunner)
        {
            FusionManager.LocalRunner = null;
        }

        SetGameState(GameStateType.Lobby);
        _playerData.Clear();
    }

    public PlayerData GetPlayerData(PlayerRef player, NetworkRunner runner)
    {
        if (_playerData.TryGetValue(player, out PlayerData data))
        {
            if (data != null && data.Object != null && data.Object.IsValid) return data;
            _playerData.Remove(player);
        }

        if (runner != null)
        {
            var playerObject = runner.GetPlayerObject(player);
            if (playerObject != null && playerObject.TryGetComponent(out data))
            {
                _playerData[player] = data;
                return data;
            }
        }
        return null;
    }

    public void SetPlayerData(PlayerRef player, PlayerData data)
    {
        if (data == null) _playerData.Remove(player);
        else _playerData[player] = data;
    }

    public void ShowWarningPanel(string text)
    {
        if (Canvas == null || _warningPanelPrefab == null) return;
        
        var panelObj = Instantiate(_warningPanelPrefab, Canvas.CanvasTransform);
        if (panelObj.TryGetComponent(out WarningPanel panel))
        {
            panel.Initialize(text);
        }
    }

    public void LoadSceneWithLoading(string sceneName)
    {
        LoadingPanel.ShowDuring(LoadSceneRoutine(sceneName));
    }

    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        while (!op.isDone)
        {
            if (op.progress >= 0.9f)
            {
                op.allowSceneActivation = true;
            }
            yield return null;
        }

        yield return new WaitForSeconds(0.5f);
    }
}