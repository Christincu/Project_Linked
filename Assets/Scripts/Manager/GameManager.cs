using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using System;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    // ==========================================
    // 로컬 플레이어 데이터 (휘발성 메모리)
    // ==========================================
    public static string MyLocalNickname = "Player";
    public static int MyLocalCharacterIndex = 0;

    // ==========================================
    // 상태 및 참조
    // ==========================================
    public GameStateType State { get; private set; } = GameStateType.Lobby;
    public ICanvas Canvas { get; private set; }
    public ISceneGameManager CurrentSceneManager { get; private set; }

    [Header("Core Manager Prefabs")]
    [SerializeField] private GameObject _gameDataManagerPrefab;
    [SerializeField] private GameObject _fusionManagerPrefab;
    [SerializeField] private GameObject _visualManagerPrefab;

    [Header("UI & Scene Prefabs")]
    [SerializeField] private GameObject _mainGameManagerPrefab;
    [SerializeField] private GameObject _mainCanvasPrefab;
    [SerializeField] private GameObject _warningPanelPrefab;
    [SerializeField] private GameObject _loadingPanelPrefab;
    
    // 플레이어 데이터 캐싱
    private Dictionary<PlayerRef, PlayerData> _playerData = new Dictionary<PlayerRef, PlayerData>();

    private void Awake()
    {
        if (!SetupSingleton()) return;

        // 1. 핵심 매니저 생성 및 보장
        InitializeCoreManagers();

        // 2. 네트워크 이벤트 등록
        SetupNetworkEvents();
        
        // 3. 씬 로드 이벤트 등록
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private bool SetupSingleton()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            return true;
        }
        
        Destroy(gameObject);
        return false;
    }

    private void Start()
    {
        CreateLoadingPanelIfNeeded();
        
        // 씬 로드 시점과 동일한 로직 수행 (캔버스 찾기 -> 매니저 대기 -> 씬 매니저 초기화)
        HandleSceneContext(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            RemoveNetworkEvents();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    // ==========================================
    // 1. Core Managers Initialization (Refactored)
    // ==========================================

    private void InitializeCoreManagers()
    {
        // 반복되는 생성 로직을 헬퍼 메서드로 대체
        EnsureManager<GameDataManager>(_gameDataManagerPrefab, "GameDataManager");
        EnsureManager<FusionManager>(_fusionManagerPrefab, "FusionManager");
        EnsureManager<VisualManager>(_visualManagerPrefab, "VisualManager");
    }

    /// <summary>
    /// 매니저가 씬에 존재하는지 확인하고, 없으면 프리팹으로 생성하는 제네릭 헬퍼 메서드
    /// </summary>
    private void EnsureManager<T>(GameObject prefab, string objectName) where T : Component
    {
        // 1. 이미 싱글톤 인스턴스가 존재하는지 확인 (각 클래스의 Instance 프로퍼티 체크는 Reflection이 필요하므로 여기선 FindObject로 대체)
        T existing = FindObjectOfType<T>(); // Unity 2023+라면 FindFirstObjectByType<T>() 권장

        if (existing != null)
        {
            if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
        }
        else if (prefab != null)
        {
            GameObject obj = Instantiate(prefab);
            obj.name = objectName;
            DontDestroyOnLoad(obj);
        }
        else
        {
            Debug.LogError($"[GameManager] Failed to ensure {typeof(T).Name}. Prefab is missing!");
        }
    }

    // ==========================================
    // 2. Scene Flow & Initialization Sequence
    // ==========================================

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HandleSceneContext(scene);
    }

    /// <summary>
    /// 씬 진입 시 공통적으로 수행해야 할 로직 (Start와 OnSceneLoaded에서 공유)
    /// </summary>
    private void HandleSceneContext(Scene scene)
    {
        if (scene.name != "Title")
        {
            EnsureMainGameComponents();
        }

        FindCanvas();

        // 코루틴 시작 전 기존 코루틴이 있다면 정리하는 것이 좋으나, 
        // 씬 로드 시점엔 보통 안전하므로 바로 실행
        StartCoroutine(InitializeSceneFlow());
    }

    /// <summary>
    /// 핵심 매니저 초기화 대기 -> 씬 매니저 연결 흐름 제어
    /// </summary>
    private IEnumerator InitializeSceneFlow()
    {
        // 1. Core Manager들이 초기화될 때까지 대기
        yield return WaitCoreManagersInit();

        // 2. 현재 씬의 로컬 SceneManager 찾기 및 초기화
        InitializeLocalSceneManager();
    }

    private IEnumerator WaitCoreManagersInit()
    {
        float timeout = 5f;
        float timer = 0f;

        // GameDataManager와 FusionManager가 모두 준비될 때까지 대기
        while (!IsCoreReady())
        {
            if (timer > timeout)
            {
                Debug.LogError("[GameManager] Core Managers initialization timed out!");
                yield break;
            }

            yield return null; // 0.1초 대기보다 매 프레임 체크가 반응성이 좋음 (필요시 WaitForSeconds 사용)
            timer += Time.deltaTime;
        }

        // 혹시 모를 초기화 실행 (방어 코드)
        if (!GameDataManager.Instance.IsInitialized) GameDataManager.Instance.Initialize();
        if (!FusionManager.Instance.IsInitialized) FusionManager.Instance.Initialize();
    }

    private bool IsCoreReady()
    {
        return GameDataManager.Instance != null && FusionManager.Instance != null;
        // 각 매니저의 IsInitialized까지 체크하면 더 안전하지만, 
        // Instance가 생성된 시점에서 Awake가 돌았다고 가정
    }

    private void InitializeLocalSceneManager()
    {
        GameObject sceneManagerObj = GameObject.Find("SceneGameManager");
        if (sceneManagerObj != null && sceneManagerObj.TryGetComponent(out ISceneGameManager manager))
        {
            CurrentSceneManager = manager;
            CurrentSceneManager.Initialize(this, GameDataManager.Instance);
            Debug.Log($"[GameManager] Initialized SceneManager: {manager.GetType().Name}");
        }
        else
        {
            // Title 씬 등 SceneManager가 없는 경우도 있으므로 로그 레벨은 상황에 맞게 조절
            CurrentSceneManager = null;
        }
    }

    // ==========================================
    // 3. Main Game Components Setup
    // ==========================================

    private void EnsureMainGameComponents()
    {
        if (MainGameManager.Instance == null && _mainGameManagerPrefab != null)
        {
            var obj = Instantiate(_mainGameManagerPrefab);
            obj.name = "MainGameManager";
        }

        if (FindObjectOfType<Canvas>() == null && _mainCanvasPrefab != null)
        {
            var obj = Instantiate(_mainCanvasPrefab);
            obj.name = "Canvas";
        }

        EnsureMainCamera();
    }

    private void EnsureMainCamera()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            if (!mainCam.TryGetComponent(out MainCameraController _))
            {
                mainCam.gameObject.AddComponent<MainCameraController>();
            }
        }
    }

    private void FindCanvas()
    {
        // 인터페이스 탐색 최적화
        Canvas = FindObjectOfType<TitleCanvas>() as ICanvas ?? FindObjectOfType<MainCanvas>() as ICanvas;

        if (Canvas == null)
        {
            Debug.LogWarning("[GameManager] Valid ICanvas not found in this scene.");
        }
    }

    // ==========================================
    // 4. Network Event Handling
    // ==========================================

    private void SetupNetworkEvents()
    {
        FusionManager.OnPlayerJoinedEvent += OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent += OnPlayerLeft;
        FusionManager.OnShutdownEvent += OnNetworkShutdown; // 이름 변경: OnShutdown -> OnNetworkShutdown
        FusionManager.OnDisconnectedEvent += OnNetworkShutdown; // 로직이 같다면 통합
    }

    private void RemoveNetworkEvents()
    {
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent -= OnPlayerLeft;
        FusionManager.OnShutdownEvent -= OnNetworkShutdown;
        FusionManager.OnDisconnectedEvent -= OnNetworkShutdown;
    }

    private void OnPlayerJoined(PlayerRef player, NetworkRunner runner) { /* 필요시 구현 */ }

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
        
        ResetGameSession();
    }

    private void ResetGameSession()
    {
        SetGameState(GameStateType.Lobby);
        _playerData.Clear();
    }

    // ==========================================
    // 5. Data Access & Helpers
    // ==========================================

    public void SetGameState(GameStateType newState) => State = newState;

    public PlayerData GetPlayerData(PlayerRef player, NetworkRunner runner)
    {
        // 1. 캐시 확인
        if (_playerData.TryGetValue(player, out PlayerData data))
        {
            if (data != null && data.Object != null && data.Object.IsValid) return data;
            _playerData.Remove(player); // 유효하지 않으면 제거
        }

        // 2. 러너에서 직접 탐색 (캐시 미스)
        if (runner != null)
        {
            var playerObject = runner.GetPlayerObject(player);
            if (playerObject != null && playerObject.TryGetComponent(out data))
            {
                _playerData[player] = data; // 캐시 갱신
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

    // ==========================================
    // 6. UI & Loading
    // ==========================================

    public void ExitGame() => Application.Quit();

    public void ShowWarningPanel(string text)
    {
        if (Canvas == null || _warningPanelPrefab == null) return;
        
        var panelObj = Instantiate(_warningPanelPrefab, Canvas.CanvasTransform);
        if (panelObj.TryGetComponent(out WarningPanel panel))
        {
            panel.Initialize(text);
        }
    }

    public void StartLoadingScreen() => LoadingPanel.Show();
    public void FinishLoadingScreen() => LoadingPanel.Hide();

    private void CreateLoadingPanelIfNeeded()
    {
        if (LoadingPanel.Instance == null && _loadingPanelPrefab != null)
        {
            var obj = Instantiate(_loadingPanelPrefab);
            obj.name = "LoadingPanel";
            // LoadingPanel 내부에서 DontDestroyOnLoad 처리한다고 가정
        }
    }

    public void LoadSceneWithLoading(string sceneName)
    {
        LoadingPanel.ShowDuring(LoadSceneRoutine(sceneName));
    }

    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        AsyncOperation op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        while (!op.isDone)
        {
            if (op.progress >= 0.9f)
            {
                op.allowSceneActivation = true;
            }
            yield return null;
        }
        // 씬 전환 후 약간의 딜레이
        yield return new WaitForSeconds(0.5f);
    }
}