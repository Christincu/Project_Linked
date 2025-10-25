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

    // 현재 게임 상태
    public GameState State { get; private set; } = GameState.Lobby;
    public ICanvas Canvas { get; private set; }

    [SerializeField] private GameObject _mainCanvasPrefab;
    [SerializeField] private GameObject _warningPanel;
    [SerializeField] private GameObject _loadingPanelPrefab;
    
    private Dictionary<PlayerRef, PlayerData> _playerData = new Dictionary<PlayerRef, PlayerData>();
    private LoadingPanel _loadingPanel;

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
        }

        // FusionHelper의 이벤트에 연결
        SetupNetworkEvents();

        FindCanvas();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        // 이벤트 연결 해제
        RemoveNetworkEvents();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        FindCanvas();
    }

    private void FindCanvas()
    {
        GameObject canvasObject = GameObject.Find("Canvas");
        if (canvasObject == null)
        {
            canvasObject = Instantiate(_mainCanvasPrefab);
        }
        
        Canvas = canvasObject.GetComponent<ICanvas>();
        if (Canvas == null)
        {
            Debug.LogWarning("GameManager: ICanvas component not found on Canvas object");
            return;
        }
        
        Canvas.Initialize(this, GameDataManager.Instance);
        Debug.Log($"GameManager: Canvas initialized - {Canvas.GetType().Name}");
    }

    // 네트워크 이벤트 연결
    private void SetupNetworkEvents()
    {
        FusionManager.OnPlayerJoinedEvent += OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent += OnPlayerLeft;
        FusionManager.OnShutdownEvent += OnShutdown;
        FusionManager.OnDisconnectedEvent += OnDisconnected;
    }

    // 네트워크 이벤트 연결 해제
    private void RemoveNetworkEvents()
    {
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent -= OnPlayerLeft;
        FusionManager.OnShutdownEvent -= OnShutdown;
        FusionManager.OnDisconnectedEvent -= OnDisconnected;
    }

    // 게임 상태 변경
    public void SetGameState(GameState newState)
    {
        State = newState;
        Debug.Log($"Game state changed: {State}");
    }

    // 플레이어 접속 시 호출
    private void OnPlayerJoined(PlayerRef player, NetworkRunner runner)
    {
        Debug.Log($"GameManager: Player {player} joined");
        // 여기서 플레이어 데이터 초기화 등 처리
    }

    // 플레이어 나감 시 호출
    private void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        Debug.Log($"GameManager: Player {player} left");
        // 플레이어 데이터 제거
        if (_playerData.ContainsKey(player))
        {
            _playerData.Remove(player);
        }
    }

    // 네트워크 종료 시 호출
    private void OnShutdown(NetworkRunner runner)
    {
        Debug.Log("GameManager: Network session shutdown");
        
        // LocalRunner 정리
        if (runner == FusionManager.LocalRunner)
        {
            FusionManager.LocalRunner = null;
        }
        
        SetGameState(GameState.Lobby);
        _playerData.Clear();
    }

    // 연결 끊김 시 호출
    private void OnDisconnected(NetworkRunner runner)
    {
        Debug.Log("GameManager: Disconnected from server");
        
        // LocalRunner 정리
        if (runner == FusionManager.LocalRunner)
        {
            FusionManager.LocalRunner = null;
        }
        
        SetGameState(GameState.Lobby);
        _playerData.Clear();
    }

    // 플레이어 데이터 가져오기
    public PlayerData GetPlayerData(PlayerRef player, NetworkRunner runner)
    {
        // First try to get from dictionary
        if (_playerData.ContainsKey(player))
        {
            return _playerData[player];
        }

        // Fallback: get from NetworkRunner's player object
        if (runner != null)
        {
            NetworkObject playerObject = runner.GetPlayerObject(player);
            if (playerObject != null)
            {
                PlayerData playerData = playerObject.GetComponent<PlayerData>();
                if (playerData != null)
                {
                    // Cache it for next time
                    _playerData[player] = playerData;
                    return playerData;
                }
            }
        }

        return null;
    }

    // 플레이어 데이터 설정
    public void SetPlayerData(PlayerRef player, PlayerData data)
    {
        _playerData[player] = data;
    }

    // 게임 종료
    public void ExitGame()
    {
        Application.Quit();
    }

    public void ShowWarningPanel(string warningText)
    {
        if (Canvas == null)
        {
            Debug.LogError("[GameManager] Cannot show warning panel - Canvas is null. Message: {warningText}");
            return;
        }
        
        if (_warningPanel == null)
        {
            Debug.LogError("[GameManager] Warning panel prefab is null. Message: {warningText}");
            return;
        }
        
        GameObject warningPanel = Instantiate(_warningPanel, Canvas.CanvasTransform);
        warningPanel.GetComponent<WarningPanel>().Initialize(warningText);
    }

    /// <summary>
    /// 로딩 화면을 시작합니다 (페이드 인).
    /// </summary>
    public void StartLoadingScreen()
    {
        if (_loadingPanel == null)
        {
            CreateLoadingPanel();
        }

        if (_loadingPanel != null)
        {
            _loadingPanel.gameObject.SetActive(true);
            _loadingPanel.Open();
            Debug.Log("[GameManager] Loading screen started (Fade In)");
        }
        else
        {
            Debug.LogError("[GameManager] Failed to create loading panel");
        }
    }

    /// <summary>
    /// 로딩 화면을 종료합니다 (페이드 아웃).
    /// </summary>
    public void FinishLoadingScreen()
    {
        if (_loadingPanel != null)
        {
            _loadingPanel.Close();
            Debug.Log("[GameManager] Loading screen finished (Fade Out)");
            
            // 페이드 아웃 애니메이션 후 비활성화
            StartCoroutine(DisableLoadingPanelAfterDelay(1f));
        }
    }

    /// <summary>
    /// 로딩 패널을 생성합니다.
    /// </summary>
    private void CreateLoadingPanel()
    {
        if (_loadingPanelPrefab == null)
        {
            Debug.LogError("[GameManager] Loading panel prefab is null!");
            return;
        }

        // Canvas를 부모로 하여 로딩 패널 생성
        Transform parent = Canvas != null ? Canvas.CanvasTransform : transform;
        GameObject panelObj = Instantiate(_loadingPanelPrefab, parent);
        _loadingPanel = panelObj.GetComponent<LoadingPanel>();

        if (_loadingPanel == null)
        {
            Debug.LogError("[GameManager] LoadingPanel component not found on prefab!");
        }
        else
        {
            panelObj.SetActive(false); // 처음에는 비활성화
            DontDestroyOnLoad(panelObj); // 씬 전환 시에도 유지
            Debug.Log("[GameManager] Loading panel created");
        }
    }

    /// <summary>
    /// 일정 시간 후 로딩 패널을 비활성화합니다.
    /// </summary>
    private IEnumerator DisableLoadingPanelAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (_loadingPanel != null)
        {
            _loadingPanel.gameObject.SetActive(false);
            Debug.Log("[GameManager] Loading panel disabled");
        }
    }

    /// <summary>
    /// 씬 로드와 함께 로딩 화면을 표시합니다.
    /// </summary>
    public void LoadSceneWithLoading(string sceneName)
    {
        StartCoroutine(LoadSceneAsync(sceneName));
    }

    /// <summary>
    /// 비동기로 씬을 로드합니다.
    /// </summary>
    private IEnumerator LoadSceneAsync(string sceneName)
    {
        // 로딩 시작
        StartLoadingScreen();

        // 페이드 인 애니메이션 대기
        yield return new WaitForSeconds(0.5f);

        // 씬 로드 시작
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;

        // 로딩 진행
        while (!asyncLoad.isDone)
        {
            // 로딩 진행률 확인 (0.0 ~ 0.9)
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            
            // 90% 도달 시 씬 활성화
            if (asyncLoad.progress >= 0.9f)
            {
                asyncLoad.allowSceneActivation = true;
            }

            yield return null;
        }

        // 로딩 완료
        yield return new WaitForSeconds(0.5f);
        FinishLoadingScreen();
    }
}