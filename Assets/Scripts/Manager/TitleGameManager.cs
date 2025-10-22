using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using System.Threading.Tasks;

public class TitleGameManager : MonoBehaviour
{
    public static TitleGameManager Instance { get; private set; }
    
    private TitleCanvas _titleCanvas;
    private string _playerNickname = "Player";
    private string _roomName = "TestRoom";
    private bool _isConnecting = false;

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
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            RemoveNetworkEvents();
        }
    }

    public void Initialize(TitleCanvas titleCanvas)
    {
        _titleCanvas = titleCanvas;
        
        // 상태 초기화 (씬 재진입 시 대비)
        _isConnecting = false;
        
        // Setup network events
        SetupNetworkEvents();
        
        // Load saved nickname
        _playerNickname = PlayerPrefs.GetString("PlayerNick", "Player");
    }

    // ========== Network Events ==========
    
    private bool _eventsSetup = false;
    
    private void SetupNetworkEvents()
    {
        if (_eventsSetup) return;
        
        FusionManager.OnPlayerJoinedEvent += OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent += OnPlayerLeft;
        FusionManager.OnPlayerChangeCharacterEvent += OnPlayerChangeCharacter;
        FusionManager.OnShutdownEvent += OnShutdown;
        FusionManager.OnDisconnectedEvent += OnDisconnected;
        PlayerData.OnPlayerDataSpawned += OnPlayerDataSpawned;
        
        _eventsSetup = true;
    }
    
    private void RemoveNetworkEvents()
    {
        if (!_eventsSetup) return;
        
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent -= OnPlayerLeft;
        FusionManager.OnPlayerChangeCharacterEvent -= OnPlayerChangeCharacter;
        FusionManager.OnShutdownEvent -= OnShutdown;
        FusionManager.OnDisconnectedEvent -= OnDisconnected;
        PlayerData.OnPlayerDataSpawned -= OnPlayerDataSpawned;
        
        _eventsSetup = false;
    }

    // ========== Public Methods (Called by UI) ==========
    
    public void CreateRoom(string roomName, string playerNickname)
    {
        // 이미 연결 중이거나 연결되어 있으면 차단
        if (_isConnecting)
        {
            _titleCanvas?.SetButtonsInteractable(true);
            return;
        }
        
        if (FusionManager.LocalRunner != null)
        {
            GameManager.Instance?.ShowWarningPanel("이미 방에 연결되어 있습니다.");
            _titleCanvas?.SetButtonsInteractable(true);
            return;
        }
        
        // 즉시 연결 중 플래그 설정
        _isConnecting = true;
        
        _roomName = string.IsNullOrEmpty(roomName) ? $"Room_{Random.Range(1000, 9999)}" : roomName;
        _playerNickname = playerNickname;
        
        // Save nickname
        PlayerPrefs.SetString("PlayerNick", _playerNickname);
        
        StartHost();
    }
    
    public void JoinRoom(string roomName, string playerNickname)
    {
        // 이미 연결 중이거나 연결되어 있으면 차단
        if (_isConnecting)
        {
            _titleCanvas?.SetButtonsInteractable(true);
            return;
        }
        
        if (FusionManager.LocalRunner != null)
        {
            GameManager.Instance?.ShowWarningPanel("이미 방에 연결되어 있습니다.");
            _titleCanvas?.SetButtonsInteractable(true);
            return;
        }
        
        if (string.IsNullOrEmpty(roomName))
        {
            GameManager.Instance?.ShowWarningPanel("방 이름을 입력해주세요.");
            _titleCanvas?.SetButtonsInteractable(true);
            return;
        }
        
        // 즉시 연결 중 플래그 설정
        _isConnecting = true;
        
        _roomName = roomName;
        _playerNickname = playerNickname;
        
        // Save nickname
        PlayerPrefs.SetString("PlayerNick", _playerNickname);
        
        StartClient();
    }
    
    public void StartGame()
    {
        if (FusionManager.LocalRunner != null && FusionManager.LocalRunner.IsServer)
        {
            FusionManager.LocalRunner.LoadScene("Main");
        }
    }
    
    public void LeaveRoom()
    {
        LeaveRoomAsync();
    }
    
    public bool IsConnecting => _isConnecting;
    public string RoomName => _roomName;

    // ========== Network Connection Methods ==========
    
    private async void StartHost()
    {
        try
        {
            // 기존 NetworkRunner GameObject 찾아서 정리
            GameObject existingRunner = GameObject.Find("NetworkRunner");
            if (existingRunner != null)
            {
                Destroy(existingRunner);
                await Task.Delay(100);
            }
            
            // 기존 NetworkRunner가 있으면 먼저 정리
            if (FusionManager.LocalRunner != null)
            {
                await FusionManager.LocalRunner.Shutdown();
                FusionManager.LocalRunner = null;
                await Task.Delay(500);
            }
            
            // Create NetworkRunner on separate object
            GameObject networkObject = new GameObject("NetworkRunner");
            DontDestroyOnLoad(networkObject);
            
            var runner = networkObject.AddComponent<NetworkRunner>();
            
            // Use singleton FusionManager as callback handler
            if (FusionManager.Instance == null)
            {
                var fusionManagerObj = new GameObject("FusionManager");
                fusionManagerObj.AddComponent<FusionManager>();
            }
            
            runner.AddCallbacks(FusionManager.Instance);
            runner.ProvideInput = true;

            var result = await runner.StartGame(new StartGameArgs()
            {
                GameMode = GameMode.Host,
                SessionName = _roomName,
                PlayerCount = 4,
                SceneManager = networkObject.AddComponent<NetworkSceneManagerDefault>()
            });
            
            if (result.Ok)
            {
                _titleCanvas?.ShowLobbyPanel();
            }
            else
            {
                string errorMessage = $"방 생성 실패: {GetShutdownReasonMessage(result.ShutdownReason)}";
                Debug.LogWarning($"[TitleGameManager] Host start failed: {result.ShutdownReason}");
                GameManager.Instance?.ShowWarningPanel(errorMessage);
                
                // 실패 시 LocalRunner와 GameObject 정리
                FusionManager.LocalRunner = null;
                if (networkObject != null)
                    Destroy(networkObject);
            }
        }
        catch (System.Exception e)
        {
            string errorMessage = $"방 생성 오류: {e.Message}";
            Debug.LogError($"[TitleGameManager] Host start error: {e.Message}");
            GameManager.Instance?.ShowWarningPanel(errorMessage);
            
            // 예외 발생 시 LocalRunner 정리
            FusionManager.LocalRunner = null;
        }
        finally
        {
            _isConnecting = false;
            _titleCanvas?.SetButtonsInteractable(true);
        }

        _titleCanvas?.UpdateLobbyUI();
    }
    
    private async void StartClient()
    {
        try
        {
            // 기존 NetworkRunner GameObject 찾아서 정리
            GameObject existingRunner = GameObject.Find("NetworkRunner");
            if (existingRunner != null)
            {
                Destroy(existingRunner);
                await Task.Delay(100);
            }
            
            // 기존 NetworkRunner가 있으면 먼저 정리
            if (FusionManager.LocalRunner != null)
            {
                await FusionManager.LocalRunner.Shutdown();
                FusionManager.LocalRunner = null;
                await Task.Delay(500);
            }
            
            // Create NetworkRunner on separate object
            GameObject networkObject = new GameObject("NetworkRunner");
            DontDestroyOnLoad(networkObject);
            
            var runner = networkObject.AddComponent<NetworkRunner>();
            
            // Use singleton FusionManager as callback handler
            if (FusionManager.Instance == null)
            {
                var fusionManagerObj = new GameObject("FusionManager");
                fusionManagerObj.AddComponent<FusionManager>();
            }
            
            runner.AddCallbacks(FusionManager.Instance);
            runner.ProvideInput = true;

            var result = await runner.StartGame(new StartGameArgs()
            {
                GameMode = GameMode.Client,
                SessionName = _roomName,
                SceneManager = networkObject.AddComponent<NetworkSceneManagerDefault>()
            });
            
            if (result.Ok)
            {
                _titleCanvas?.ShowLobbyPanel();
            }
            else
            {
                string errorMessage = $"방 접속 실패: {GetShutdownReasonMessage(result.ShutdownReason)}";
                Debug.LogWarning($"[TitleGameManager] Client connection failed: {result.ShutdownReason}");
                GameManager.Instance?.ShowWarningPanel(errorMessage);
                
                // 실패 시 LocalRunner와 GameObject 정리
                FusionManager.LocalRunner = null;
                if (networkObject != null)
                    Destroy(networkObject);
            }
        }
        catch (System.Exception e)
        {
            string errorMessage = $"방 접속 오류: {e.Message}";
            Debug.LogError($"[TitleGameManager] Client connection error: {e.Message}");
            GameManager.Instance?.ShowWarningPanel(errorMessage);
            
            // 예외 발생 시 LocalRunner 정리
            FusionManager.LocalRunner = null;
        }
        finally
        {
            _isConnecting = false;
            _titleCanvas?.SetButtonsInteractable(true);
        }

        _titleCanvas?.UpdateLobbyUI();
    }
    
    private async void LeaveRoomAsync()
    {
        if (FusionManager.LocalRunner != null)
        {
            await FusionManager.LocalRunner.Shutdown();
            FusionManager.LocalRunner = null;
            await Task.Delay(500);
        }
        
        // NetworkRunner GameObject 정리
        GameObject networkRunnerObj = GameObject.Find("NetworkRunner");
        if (networkRunnerObj != null)
        {
            Destroy(networkRunnerObj);
        }
        
        _isConnecting = false;
        _titleCanvas?.ShowTitlePanel();
        _titleCanvas?.SetButtonsInteractable(true);
    }

    // ========== Network Event Handlers ==========
    
    private void OnPlayerJoined(PlayerRef player, NetworkRunner runner)
    {
        _titleCanvas?.UpdateLobbyUI();
    }
    
    private void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        _titleCanvas?.UpdateLobbyUI();
    }
    
    private void OnPlayerChangeCharacter(PlayerRef player, NetworkRunner runner, int characterIndex)
    {
        // Update UI if it's the local player
        if (runner.LocalPlayer == player && GameDataManager.Instance != null)
        {
            var characterData = GameDataManager.Instance.CharacterService.GetCharacter(characterIndex);
            if (characterData != null)
            {
                _titleCanvas?.UpdateCharacterUI(characterData);
            }
        }
        
        _titleCanvas?.UpdateLobbyUI();
    }
    
    private void OnShutdown(NetworkRunner runner)
    {
        // LocalRunner 정리
        if (runner == FusionManager.LocalRunner)
        {
            FusionManager.LocalRunner = null;
        }
        
        _isConnecting = false;
        _titleCanvas?.ShowTitlePanel();
        _titleCanvas?.SetButtonsInteractable(true);
    }
    
    private void OnDisconnected(NetworkRunner runner)
    {
        // LocalRunner 정리
        if (runner == FusionManager.LocalRunner)
        {
            FusionManager.LocalRunner = null;
        }
        
        _isConnecting = false;
        GameManager.Instance?.ShowWarningPanel("네트워크 연결이 끊어졌습니다.");
        _titleCanvas?.ShowTitlePanel();
        _titleCanvas?.SetButtonsInteractable(true);
    }
    
    private void OnPlayerDataSpawned(PlayerRef player, NetworkRunner runner)
    {
        // Update local player's character UI if it's the local player
        if (runner.LocalPlayer == player)
        {
            var playerData = GameManager.Instance?.GetPlayerData(player, runner);
            if (playerData != null && GameDataManager.Instance != null)
            {
                var characterData = GameDataManager.Instance.CharacterService.GetCharacter(playerData.CharacterIndex);
                if (characterData != null)
                {
                    _titleCanvas?.UpdateCharacterUI(characterData);
                }
            }
        }
        
        _titleCanvas?.UpdateLobbyUI();
    }

    // ========== Helper Methods ==========
    
    private string GetShutdownReasonMessage(ShutdownReason reason)
    {
        switch (reason)
        {
            case ShutdownReason.Ok:
                return "정상 종료";
            case ShutdownReason.Error:
                return "알 수 없는 오류";
            case ShutdownReason.ServerInRoom:
                return "서버가 이미 다른 방에 있습니다";
            case ShutdownReason.DisconnectedByPluginLogic:
                return "플러그인 로직에 의해 연결 해제";
            case ShutdownReason.GameClosed:
                return "게임이 종료되었습니다";
            case ShutdownReason.GameNotFound:
                return "방을 찾을 수 없습니다";
            case ShutdownReason.MaxCcuReached:
                return "최대 동시 접속자 수 도달";
            case ShutdownReason.InvalidRegion:
                return "잘못된 리전";
            case ShutdownReason.GameIdAlreadyExists:
                return "방 이름이 이미 존재합니다";
            case ShutdownReason.GameIsFull:
                return "방이 가득 찼습니다";
            case ShutdownReason.InvalidAuthentication:
                return "인증 실패";
            case ShutdownReason.CustomAuthenticationFailed:
                return "사용자 인증 실패";
            case ShutdownReason.AuthenticationTicketExpired:
                return "인증 티켓 만료";
            case ShutdownReason.PhotonCloudTimeout:
                return "클라우드 연결 시간 초과";
            default:
                return reason.ToString();
        }
    }
}
