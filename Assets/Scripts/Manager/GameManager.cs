using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using System.Threading.Tasks;
using System;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    // 현재 게임 상태 (GameState enum이 별도로 정의되어 있다고 가정)
    public GameState State { get; private set; } = GameState.Lobby;
    public ICanvas Canvas { get; private set; }

    [Header("Manager & UI Prefabs")]
    [Tooltip("타이틀 씬이 아닌 경우 자동으로 생성할 MainGameManager 프리팹")]
    [SerializeField] private GameObject _mainGameManagerPrefab;
    
    [Tooltip("타이틀 씬이 아닌 경우 자동으로 생성할 MainCanvas 프리팹")]
    [SerializeField] private GameObject _mainCanvasPrefab;
    
    [SerializeField] private GameObject _warningPanelPrefab;
    [SerializeField] private GameObject _loadingPanelPrefab;
    
    private Dictionary<PlayerRef, PlayerData> _playerData = new Dictionary<PlayerRef, PlayerData>();

    void Awake()
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

        SetupNetworkEvents();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    void Start()
    {
        CreateLoadingPanelIfNeeded();
        FindCanvas();
    }
    
    /// <summary>
    /// LoadingPanel이 없으면 생성합니다. (외부에서도 호출 가능)
    /// </summary>
    public void CreateLoadingPanelIfNeeded()
    {
        if (LoadingPanel.Instance == null && _loadingPanelPrefab != null)
        {
            GameObject panelObj = Instantiate(_loadingPanelPrefab);
            panelObj.name = "LoadingPanel";
            Debug.Log("[GameManager] LoadingPanel created");
        }
    }

    void OnDestroy()
    {
        RemoveNetworkEvents();
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Title")
        {
            EnsureMainGameComponents();
        }
        
        FindCanvas();
    }
    
    /// <summary>
    /// 메인 게임 씬에 필요한 컴포넌트들이 있는지 확인하고 없으면 생성합니다.
    /// </summary>
    private void EnsureMainGameComponents()
    {
        if (MainGameManager.Instance == null)
        {
            if (_mainGameManagerPrefab == null)
            {
                Debug.LogError("[GameManager] MainGameManagerPrefab is not set!");
                return;
            }
            
            GameObject managerObj = Instantiate(_mainGameManagerPrefab);
            managerObj.name = "MainGameManager";
            Debug.Log("[GameManager] MainGameManager created");
        }
        
        if (FindAnyObjectByType<UnityEngine.Canvas>() == null)
        {
            if (_mainCanvasPrefab == null)
            {
                Debug.LogError("[GameManager] MainCanvasPrefab is not set!");
                return;
            }
            
            GameObject canvasObj = Instantiate(_mainCanvasPrefab);
            canvasObj.name = "Canvas";
            Debug.Log("[GameManager] MainCanvas created");
        }
        
        EnsureMainCamera();
    }
    
    /// <summary>
    /// Main Camera에 MainCameraController가 있는지 확인하고 없으면 추가합니다.
    /// </summary>
    private void EnsureMainCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("[GameManager] Main Camera not found in scene!");
            return;
        }
        
        MainCameraController cameraController = mainCamera.GetComponent<MainCameraController>();
        if (cameraController == null)
        {
            mainCamera.gameObject.AddComponent<MainCameraController>();
            Debug.Log("[GameManager] MainCameraController added to Main Camera");
        }
    }

    /// <summary>
    /// 현재 씬에서 ICanvas 인터페이스를 구현한 캔버스 오브젝트를 안전하게 찾습니다.
    /// (ICanvas가 MonoBehaviour를 상속한 컴포넌트에 구현되어야 합니다.)
    /// </summary>
    private void FindCanvas()
    {
        ICanvas foundCanvas = GameObject.Find("Canvas").GetComponent<ICanvas>();

        if (foundCanvas == null)
        {
            Debug.LogError("GameManager: The found Canvas object does not implement ICanvas.");
            Canvas = null;
            return;
        }

        Canvas = foundCanvas;
        
        // 3. 초기화
        Canvas.Initialize(this, GameDataManager.Instance);
        Debug.Log($"GameManager: Canvas initialized - {Canvas.GetType().Name} in scene {SceneManager.GetActiveScene().name}");
    }

    // ========== Network Event Setup (이하 동일) ==========
    private void SetupNetworkEvents()
    {
        FusionManager.OnPlayerJoinedEvent += OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent += OnPlayerLeft;
        FusionManager.OnShutdownEvent += OnShutdown;
        FusionManager.OnDisconnectedEvent += OnDisconnected;
    }

    private void RemoveNetworkEvents()
    {
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent -= OnPlayerLeft;
        FusionManager.OnShutdownEvent -= OnShutdown;
        FusionManager.OnDisconnectedEvent -= OnDisconnected;
    }

    // ========== Game State & Data Management (이하 동일) ==========

    public void SetGameState(GameState newState)
    {
        State = newState;
        Debug.Log($"Game state changed: {State}");
    }

    private void OnPlayerJoined(PlayerRef player, NetworkRunner runner)
    {
        Debug.Log($"GameManager: Player {player} joined");
    }

    private void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        Debug.Log($"GameManager: Player {player} left");
        _playerData.Remove(player);
    }

    private void OnShutdown(NetworkRunner runner)
    {
        Debug.Log("GameManager: Network session shutdown");
        
        if (runner == FusionManager.LocalRunner)
        {
            FusionManager.LocalRunner = null;
        }
        
        SetGameState(GameState.Lobby);
        _playerData.Clear();
    }

    private void OnDisconnected(NetworkRunner runner)
    {
        Debug.Log("GameManager: Disconnected from server");
        
        if (runner == FusionManager.LocalRunner)
        {
            FusionManager.LocalRunner = null;
        }
        
        SetGameState(GameState.Lobby);
        _playerData.Clear();
    }

    public PlayerData GetPlayerData(PlayerRef player, NetworkRunner runner)
    {
        if (_playerData.TryGetValue(player, out PlayerData data))
        {
            return data;
        }

        if (runner != null)
        {
            NetworkObject playerObject = runner.GetPlayerObject(player);
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
        _playerData[player] = data;
    }
    
    // ========== UI & Utility (이하 동일) ==========

    public void ExitGame()
    {
        Application.Quit();
        Debug.Log("Application Quit requested.");
    }

    public void ShowWarningPanel(string warningText)
    {
        if (Canvas == null || _warningPanelPrefab == null)
        {
            Debug.LogError($"[GameManager] Cannot show warning panel - Canvas or Prefab is null. Message: {warningText}");
            return;
        }
        
        GameObject warningPanel = Instantiate(_warningPanelPrefab, Canvas.CanvasTransform);
        
        if (warningPanel.TryGetComponent(out WarningPanel panel))
        {
            panel.Initialize(warningText);
        }
        else
        {
            Debug.LogError("[GameManager] WarningPanel component not found on the instantiated prefab.");
            Destroy(warningPanel);
        }
    }

    public void StartLoadingScreen()
    {
        LoadingPanel.Show();
    }

    public void FinishLoadingScreen()
    {
        LoadingPanel.Hide();
    }
    
    public void LoadSceneWithLoading(string sceneName)
    {
        LoadingPanel.ShowDuring(LoadSceneAsync(sceneName));
    }

    private IEnumerator LoadSceneAsync(string sceneName)
    {
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;

        while (!asyncLoad.isDone)
        {
            if (asyncLoad.progress >= 0.9f)
            {
                asyncLoad.allowSceneActivation = true;
            }

            yield return null;
        }

        yield return new WaitForSeconds(0.5f);
    }
}