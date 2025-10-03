using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using System.Threading.Tasks;

public class TitleCanvas : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject titlePanel;      // Title screen
    [SerializeField] private GameObject lobbyPanel;    // Lobby screen
    
    [Header("Lobby UI")]
    [SerializeField] private TextMeshProUGUI playerListText;    // Player list
    [SerializeField] private TextMeshProUGUI roomNameText;     // Room name
    [SerializeField] private Button startGameButton;            // Start game button
    [SerializeField] private Button leaveRoomButton;            // Leave room button
    
    [Header("Room Setup UI")]
    [SerializeField] private TMP_InputField nicknameInput;     // Nickname input
    [SerializeField] private TMP_InputField roomNameInput;     // Room name input
    [SerializeField] private Button createRoomButton;          // Create room button
    [SerializeField] private Button joinRoomButton;            // Join room button
    
    
    private string _playerNickname = "Player";
    private string _roomName = "TestRoom";
    
    void Start()
    {
        // Initial UI setup
        ShowTitlePanel();
        
        // Setup network events
        SetupNetworkEvents();
        
        // Auto assign button events
        SetupButtonEvents();
        
        // Load saved nickname
        _playerNickname = PlayerPrefs.GetString("PlayerNick", "Player");
        nicknameInput.text = _playerNickname;
    }
    
    // Auto assign button events
    private void SetupButtonEvents()
    {
        // Create room button
        if (createRoomButton != null)
            createRoomButton.onClick.AddListener(OnCreateRoomButton);
        
        // Join room button
        if (joinRoomButton != null)
            joinRoomButton.onClick.AddListener(OnJoinRoomButton);
        
        // Start game button
        if (startGameButton != null)
            startGameButton.onClick.AddListener(OnStartGameButton);
        
        // Leave room button
        if (leaveRoomButton != null)
            leaveRoomButton.onClick.AddListener(OnLeaveRoomButton);
    }
    
    void OnDestroy()
    {
        // Remove event connections
        RemoveNetworkEvents();
        RemoveButtonEvents();
    }
    
    // Remove button event connections
    private void RemoveButtonEvents()
    {
        // Create room button
        if (createRoomButton != null)
            createRoomButton.onClick.RemoveListener(OnCreateRoomButton);
        
        // Join room button
        if (joinRoomButton != null)
            joinRoomButton.onClick.RemoveListener(OnJoinRoomButton);
        
        // Start game button
        if (startGameButton != null)
            startGameButton.onClick.RemoveListener(OnStartGameButton);
        
        // Leave room button
        if (leaveRoomButton != null)
            leaveRoomButton.onClick.RemoveListener(OnLeaveRoomButton);
    }
    
    // Setup network events
    private void SetupNetworkEvents()
    {
        FusionManager.OnPlayerJoinedEvent += OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent += OnPlayerLeft;
        FusionManager.OnShutdownEvent += OnShutdown;
        PlayerData.OnPlayerDataSpawned += OnPlayerDataSpawned;
    }
    
    // Remove network event connections
    private void RemoveNetworkEvents()
    {
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent -= OnPlayerLeft;
        FusionManager.OnShutdownEvent -= OnShutdown;
        PlayerData.OnPlayerDataSpawned -= OnPlayerDataSpawned;
    }
    
    // ========== UI Panel Switching ==========
    
    public void ShowTitlePanel()
    {
        titlePanel.SetActive(true);
        lobbyPanel.SetActive(false);
    }
    
    public void ShowLobbyPanel()
    {
        titlePanel.SetActive(false);
        lobbyPanel.SetActive(true);
        
        // Immediate UI update (show room name before connection)
        UpdateLobbyUI();
    }
    
    // ========== Button Events ==========
    
    // Create room button
    public void OnCreateRoomButton()
    {
        _roomName = roomNameInput.text;
        _playerNickname = nicknameInput.text;
        
        if (string.IsNullOrEmpty(_roomName))
        {
            _roomName = "Room_" + Random.Range(1000, 9999);
        }
        
        // Save nickname
        PlayerPrefs.SetString("PlayerNick", _playerNickname);
        
        Debug.Log($"Creating room: {_roomName}");
        StartHost();
    }
    
    // Join room button
    public void OnJoinRoomButton()
    {
        _roomName = roomNameInput.text;
        _playerNickname = nicknameInput.text;
        
        if (string.IsNullOrEmpty(_roomName))
        {
            Debug.LogWarning("Please enter room name!");
            return;
        }
        
        // Save nickname
        PlayerPrefs.SetString("PlayerNick", _playerNickname);
        
        Debug.Log($"Joining room: {_roomName}");
        StartClient();
    }
    
    // Start game button (host only)
    public void OnStartGameButton()
    {
        if (FusionManager.LocalRunner != null && FusionManager.LocalRunner.IsServer)
        {
            Debug.Log("Starting game!");
            // Load game scene here
            // SceneManager.LoadScene("GameScene");
        }
    }
    
    // Leave room button
    public void OnLeaveRoomButton()
    {
        Debug.Log("Leaving room");
        LeaveRoom();
    }
    
    // Exit game button
    public void OnExitGameButton()
    {
        GameManager.Instance?.ExitGame();
    }
    
    // ========== Network Functions ==========
    
    // Start host (create room)
    private async void StartHost()
    {
        try
        {
            // Create NetworkRunner on separate object (prevent TitleCanvas duplication)
            GameObject networkObject = new GameObject("NetworkRunner");
            DontDestroyOnLoad(networkObject);
            
            var runner = networkObject.AddComponent<NetworkRunner>();
            
            // Use singleton FusionManager as callback handler
            if (FusionManager.Instance == null)
            {
                var fusionManagerObj = new GameObject("FusionManager");
                fusionManagerObj.AddComponent<FusionManager>();
            }
            
            // Register FusionManager as callback handler for NetworkRunner
            runner.AddCallbacks(FusionManager.Instance);
            
            var result = await runner.StartGame(new StartGameArgs()
            {
                GameMode = GameMode.Host,
                SessionName = _roomName,
                PlayerCount = 4, // Max 4 players
                SceneManager = networkObject.AddComponent<NetworkSceneManagerDefault>()
            });
            
            if (result.Ok)
            {
                Debug.Log("Host started successfully!");
                ShowLobbyPanel();
            }
            else
            {
                Debug.LogError($"Host start failed: {result.ShutdownReason}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Host start error: {e.Message}");
        }

        UpdateLobbyUI();
    }
    
    // Start client (join room)
    private async void StartClient()
    {
        try
        {
            // Create NetworkRunner on separate object (prevent TitleCanvas duplication)
            GameObject networkObject = new GameObject("NetworkRunner");
            DontDestroyOnLoad(networkObject);
            
            var runner = networkObject.AddComponent<NetworkRunner>();
            
            // Use singleton FusionManager as callback handler
            if (FusionManager.Instance == null)
            {
                var fusionManagerObj = new GameObject("FusionManager");
                fusionManagerObj.AddComponent<FusionManager>();
            }
            
            // Register FusionManager as callback handler for NetworkRunner
            runner.AddCallbacks(FusionManager.Instance);
            
            var result = await runner.StartGame(new StartGameArgs()
            {
                GameMode = GameMode.Client,
                SessionName = _roomName,
                SceneManager = networkObject.AddComponent<NetworkSceneManagerDefault>()
            });
            
            if (result.Ok)
            {
                Debug.Log("Client connected successfully!");
                ShowLobbyPanel();
            }
            else
            {
                Debug.LogError($"Client connection failed: {result.ShutdownReason}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Client connection error: {e.Message}");
        }

        UpdateLobbyUI();
    }
    
    // Leave room
    private async void LeaveRoom()
    {
        if (FusionManager.LocalRunner != null)
        {
            await FusionManager.LocalRunner.Shutdown();
        }
        ShowTitlePanel();
    }
    
    // ========== Network Event Handling ==========
    
    private void OnPlayerJoined(PlayerRef player, NetworkRunner runner)
    {
        Debug.Log($"Player joined: {player}");
        UpdateLobbyUI();
    }
    
    private void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        Debug.Log($"Player left: {player}");
        UpdateLobbyUI();
    }
    
    private void OnShutdown(NetworkRunner runner)
    {
        Debug.Log("Network session ended");
        ShowTitlePanel();
    }
    
    private void OnPlayerDataSpawned(PlayerRef player, NetworkRunner runner)
    {
        Debug.Log($"Player data spawned: {player}");
        UpdateLobbyUI();
    }
    
    // ========== UI Update ==========
    
    private void UpdateLobbyUI()
    {
        Debug.Log($"UpdateLobbyUI - FusionManager.LocalRunner: {FusionManager.LocalRunner}");
        
        // Show room name (even before network connection)
        if (FusionManager.LocalRunner != null)
        {
            Debug.Log($"LocalRunner found - ActivePlayers: {FusionManager.LocalRunner.ActivePlayers}");
            
            // When connected: use session name
            roomNameText.text = $"Room: {FusionManager.LocalRunner.SessionInfo.Name}";
            
            // Update player list
            string playerList = "";
            foreach (var player in FusionManager.LocalRunner.ActivePlayers)
            {
                var playerData = GameManager.Instance?.GetPlayerData(player, FusionManager.LocalRunner);
                string nick = playerData?.Nick.ToString() ?? $"Player_{player.AsIndex}";
                string isLocal = (player == FusionManager.LocalRunner.LocalPlayer) ? " (You)" : "";
                string isHost = (player == FusionManager.LocalRunner.LocalPlayer && FusionManager.LocalRunner.IsServer) ? " [Host]" : "";
                playerList += $"{nick}{isLocal}{isHost}\n";
            }
            playerListText.text = playerList;
            
            // Start game button (host only)
            startGameButton.gameObject.SetActive(FusionManager.LocalRunner.IsServer);
        }
        else
        {
            Debug.Log("LocalRunner is null - showing connecting...");
            // Before connection: use local variable
            roomNameText.text = $"Room: {_roomName}";
            playerListText.text = "Connecting...";
            startGameButton.gameObject.SetActive(false);
        }
    }
}
