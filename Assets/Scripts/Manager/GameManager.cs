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

    [SerializeField] private GameObject _warningPanel;
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
            Debug.LogWarning("GameManager: Canvas object not found in scene");
            return;
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
            Debug.LogError($"GameManager: Cannot show warning panel - Canvas is null. Message: {warningText}");
            return;
        }
        
        if (_warningPanel == null)
        {
            Debug.LogError($"GameManager: Warning panel prefab is null. Message: {warningText}");
            return;
        }
        
        GameObject warningPanel = Instantiate(_warningPanel, Canvas.CanvasTransform);
        warningPanel.GetComponent<WarningPanel>().Initialize(warningText);
    }
}