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

    // ==========================================
    // 로컬 플레이어의 정보를 메모리에 저장 (휘발성)
    // 에디터와 빌드 프로그램은 이 메모리를 공유하지 않으므로 안전합니다.
    // 게임이 종료되면 사라지는 임시 저장소입니다.
    // ==========================================
    public static string MyLocalNickname = "Player";
    public static int MyLocalCharacterIndex = 0;

    // 현재 게임 상태 (GameState enum이 별도로 정의되어 있다고 가정)
    public GameStateType State { get; private set; } = GameStateType.Lobby;
    public ICanvas Canvas { get; private set; }
    public ISceneManager CurrentSceneManager { get; private set; }

    [Header("Manager & UI Prefabs")]
    [Tooltip("타이틀 씬이 아닌 경우 자동으로 생성할 MainGameManager 프리팹")]
    [SerializeField] private GameObject _mainGameManagerPrefab;
    
    [Tooltip("타이틀 씬이 아닌 경우 자동으로 생성할 MainCanvas 프리팹")]
    [SerializeField] private GameObject _mainCanvasPrefab;
    
    [Header("Core Manager Prefabs")]
    [Tooltip("GameDataManager 프리팹 (씬에 없으면 자동 생성)")]
    [SerializeField] private GameObject _gameDataManagerPrefab;
    
    [Tooltip("FusionManager 프리팹 (씬에 없으면 자동 생성)")]
    [SerializeField] private GameObject _fusionManagerPrefab;

    [Tooltip("VisualManager 프리팹 (씬에 없으면 자동 생성)")]
    [SerializeField] private GameObject _visualManagerPrefab;
    
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

        // 핵심 매니저들을 순서대로 초기화
        InitializeCoreManagers();
        
        SetupNetworkEvents();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    /// <summary>
    /// 핵심 매니저들(GameDataManager, FusionManager)을 순서대로 초기화합니다.
    /// </summary>
    private void InitializeCoreManagers()
    {
        // 1. GameDataManager 초기화 (가장 먼저)
        EnsureGameDataManager();
        
        // 2. FusionManager 초기화 (GameDataManager 이후)
        EnsureFusionManager();

        // 3. VisualManager 초기화 (시각 효과 전역 관리)
        EnsureVisualManager();
    }
    
    /// <summary>
    /// GameDataManager가 존재하는지 확인하고 없으면 생성한 후 초기화합니다.
    /// </summary>
    private void EnsureGameDataManager()
    {
        // 씬에서 찾기
        if (GameDataManager.Instance == null)
        {
            GameDataManager existingManager = FindAnyObjectByType<GameDataManager>();
            if (existingManager != null)
            {
                // 씬에 있으면 Awake가 호출되어 싱글톤이 설정됨
                // 강제로 Awake 호출 (이미 호출되었을 수도 있지만 안전하게)
                if (GameDataManager.Instance == null)
                {
                    // GameObject를 활성화하여 Awake가 호출되도록 함
                    if (!existingManager.gameObject.activeSelf)
                    {
                        existingManager.gameObject.SetActive(true);
                    }
                }
            }
            else if (_gameDataManagerPrefab != null)
            {
                // 프리팹으로 생성 (Awake가 자동 호출됨)
                GameObject managerObj = Instantiate(_gameDataManagerPrefab);
                managerObj.name = "GameDataManager";
                DontDestroyOnLoad(managerObj);
            }
            else
            {
                Debug.LogError("[GameManager] GameDataManager not found in scene and prefab is not set!");
                return;
            }
        }
        
        // 초기화 (이미 초기화되었으면 스킵)
        if (GameDataManager.Instance != null && !GameDataManager.Instance.IsInitialized)
        {
            GameDataManager.Instance.Initialize();
        }
    }
    
    /// <summary>
    /// FusionManager가 존재하는지 확인하고 없으면 생성한 후 초기화합니다.
    /// </summary>
    private void EnsureFusionManager()
    {
        // 씬에서 찾기
        if (FusionManager.Instance == null)
        {
            FusionManager existingManager = FindAnyObjectByType<FusionManager>();
            if (existingManager != null)
            {
                // 씬에 있으면 Awake가 호출되어 싱글톤이 설정됨
                // 강제로 Awake 호출 (이미 호출되었을 수도 있지만 안전하게)
                if (FusionManager.Instance == null)
                {
                    // GameObject를 활성화하여 Awake가 호출되도록 함
                    if (!existingManager.gameObject.activeSelf)
                    {
                        existingManager.gameObject.SetActive(true);
                    }
                }
            }
            else if (_fusionManagerPrefab != null)
            {
                // 프리팹으로 생성 (Awake가 자동 호출됨)
                GameObject managerObj = Instantiate(_fusionManagerPrefab);
                managerObj.name = "FusionManager";
                DontDestroyOnLoad(managerObj);
            }
            else
            {
                Debug.LogError("[GameManager] FusionManager not found in scene and prefab is not set!");
                return;
            }
        }
        
        // 초기화 (이미 초기화되었으면 스킵)
        if (FusionManager.Instance != null && !FusionManager.Instance.IsInitialized)
        {
            FusionManager.Instance.Initialize();
        }
    }

    /// <summary>
    /// VisualManager가 존재하는지 확인하고 없으면 생성합니다.
    /// </summary>
    private void EnsureVisualManager()
    {
        // 이미 싱글톤이 존재하면 아무 것도 하지 않음
        if (VisualManager.Instance != null)
        {
            return;
        }

        // 씬에서 찾기
        VisualManager existingManager = FindAnyObjectByType<VisualManager>();
        if (existingManager != null)
        {
            // Awake에서 싱글톤이 설정되도록 활성화 보장
            if (!existingManager.gameObject.activeSelf)
            {
                existingManager.gameObject.SetActive(true);
            }
            return;
        }

        // 프리팹으로 생성
        if (_visualManagerPrefab != null)
        {
            GameObject managerObj = Instantiate(_visualManagerPrefab);
            managerObj.name = "VisualManager";
            DontDestroyOnLoad(managerObj);
        }
        else
        {
            Debug.LogWarning("[GameManager] VisualManager not found in scene and prefab is not set. Explosion range visuals may not work correctly.");
        }
    }
    
    void Start()
    {
        CreateLoadingPanelIfNeeded();
        FindCanvas();
        
        // 핵심 매니저들 초기화 완료 후 씬 매니저 초기화
        StartCoroutine(InitializeSceneManagerAfterCoreManagers());
    }
    
    /// <summary>
    /// 핵심 매니저들(GameDataManager, FusionManager) 초기화 완료 후 씬 매니저를 초기화합니다.
    /// </summary>
    private IEnumerator InitializeSceneManagerAfterCoreManagers()
    {
        // 즉시 확인 (이미 초기화되어 있을 수 있음)
        bool allInitialized = CheckCoreManagersInitialized();
        
        if (!allInitialized)
        {
            // 핵심 매니저들이 초기화될 때까지 대기
            float timeout = 5f;
            float timer = 0f;
            
            while (timer < timeout)
            {
                allInitialized = CheckCoreManagersInitialized();
                
                if (allInitialized)
                {
                    break;
                }
                
                yield return new WaitForSeconds(0.1f);
                timer += 0.1f;
            }
        }
        
        // 초기화 완료 확인 및 에러 로깅
        if (GameDataManager.Instance == null || !GameDataManager.Instance.IsInitialized)
        {
            Debug.LogError("[GameManager] GameDataManager initialization failed or timed out!");
        }
        
        if (FusionManager.Instance == null || !FusionManager.Instance.IsInitialized)
        {
            Debug.LogError("[GameManager] FusionManager initialization failed or timed out!");
        }
        
        // 핵심 매니저들 초기화 완료 후 씬 매니저 초기화
        FindSceneManager();
    }
    
    /// <summary>
    /// 핵심 매니저들이 모두 초기화되었는지 확인합니다.
    /// </summary>
    private bool CheckCoreManagersInitialized()
    {
        // GameDataManager 초기화 확인
        if (GameDataManager.Instance == null || !GameDataManager.Instance.IsInitialized)
        {
            return false;
        }
        
        // FusionManager 초기화 확인
        if (FusionManager.Instance == null || !FusionManager.Instance.IsInitialized)
        {
            return false;
        }
        
        return true;
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
        }
    }

    void OnDestroy()
    {
        RemoveNetworkEvents();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Title")
        {
            EnsureMainGameComponents();
        }
        
        FindCanvas();
        
        // 핵심 매니저들 초기화 완료 후 씬 매니저 초기화
        StartCoroutine(InitializeSceneManagerAfterCoreManagers());
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
        }
    }

    /// <summary>
    /// 현재 씬에서 ICanvas 인터페이스를 구현한 캔버스 오브젝트를 안전하게 찾습니다.
    /// (ICanvas가 MonoBehaviour를 상속한 컴포넌트에 구현되어야 합니다.)
    /// 주의: Canvas 초기화는 씬 매니저(TitleGameManager, MainGameManager)가 담당합니다.
    /// 이 메서드는 참조만 설정하며, 실제 초기화는 씬 매니저가 수행합니다.
    /// </summary>
    private void FindCanvas()
    {
        // TitleCanvas 또는 MainCanvas를 직접 찾기 (FindObjectOfType 사용)
        ICanvas foundCanvas = null;
        
        TitleCanvas titleCanvas = FindObjectOfType<TitleCanvas>();
        if (titleCanvas != null)
        {
            foundCanvas = titleCanvas as ICanvas;
        }
        
        if (foundCanvas == null)
        {
            MainCanvas mainCanvas = FindObjectOfType<MainCanvas>();
            if (mainCanvas != null)
            {
                foundCanvas = mainCanvas as ICanvas;
            }
        }

        if (foundCanvas == null)
        {
            Debug.LogWarning("[GameManager] Canvas with ICanvas interface not found. Scene manager will initialize it when ready.");
            Canvas = null;
            return;
        }

        // Canvas 참조만 설정 (초기화는 씬 매니저가 담당)
        Canvas = foundCanvas;
    }
    
    /// <summary>
    /// 현재 씬에서 ISceneManager 인터페이스를 구현한 씬 매니저 오브젝트를 안전하게 찾습니다.
    /// (ISceneManager가 MonoBehaviour를 상속한 컴포넌트에 구현되어야 합니다.)
    /// </summary>
    private void FindSceneManager()
    {
        GameObject sceneManagerObj = GameObject.Find("SceneManager");
        
        if (sceneManagerObj == null)
        {
            // SceneManager가 없을 수도 있으므로 경고만 출력
            Debug.LogWarning("[GameManager] SceneManager GameObject not found in scene.");
            CurrentSceneManager = null;
            return;
        }
        
        ISceneManager foundSceneManager = sceneManagerObj.GetComponent<ISceneManager>();
        
        if (foundSceneManager == null)
        {
            Debug.LogError("[GameManager] The found SceneManager object does not implement ISceneManager.");
            CurrentSceneManager = null;
            return;
        }

        CurrentSceneManager = foundSceneManager;
        
        // 초기화
        CurrentSceneManager.Initialize(this, GameDataManager.Instance);
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

    public void SetGameState(GameStateType newState)
    {
        State = newState;
    }

    private void OnPlayerJoined(PlayerRef player, NetworkRunner runner)
    {
    }

    private void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        _playerData.Remove(player);
    }

    private void OnShutdown(NetworkRunner runner)
    {
        if (runner == FusionManager.LocalRunner)
        {
            FusionManager.LocalRunner = null;
        }
        
        SetGameState(GameStateType.Lobby);
        _playerData.Clear();
    }

    private void OnDisconnected(NetworkRunner runner)
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
        // 딕셔너리에서 찾기 (유효성 확인)
        if (_playerData.TryGetValue(player, out PlayerData data))
        {
            // NetworkObject가 유효한지 확인
            if (data != null && data.Object != null && data.Object.IsValid)
            {
                return data;
            }
            else
            {
                // 유효하지 않으면 딕셔너리에서 제거
                _playerData.Remove(player);
            }
        }

        // NetworkRunner에서 찾기
        if (runner != null)
        {
            NetworkObject playerObject = runner.GetPlayerObject(player);
            if (playerObject != null && playerObject.IsValid && playerObject.TryGetComponent(out data))
            {
                _playerData[player] = data;
                return data;
            }
        }
        return null;
    }

    public void SetPlayerData(PlayerRef player, PlayerData data)
    {
        if (data == null)
        {
            _playerData.Remove(player);
        }
        else
        {
            _playerData[player] = data;
        }
    }
    
    // ========== UI & Utility (이하 동일) ==========

    public void ExitGame()
    {
        Application.Quit();
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
        AsyncOperation asyncLoad = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
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