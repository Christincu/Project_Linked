using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using System.Threading.Tasks;

public class TitleGameManager : MonoBehaviour
{
    private TitleCanvas _titleCanvas;
    private string _playerNickname = "Player";
    private string _roomName = "TestRoom";
    private bool _isConnecting = false;

    void OnDestroy()
    {
        RemoveNetworkEvents();
    }

    public void Initialize(TitleCanvas titleCanvas)
    {
        _titleCanvas = titleCanvas;

        // 상태 초기화 (씬 재진입 시 대비)
        _isConnecting = false;

        SetupNetworkEvents();

        // 메모리에서 닉네임 로드
        _playerNickname = GameManager.MyLocalNickname;
        if (string.IsNullOrEmpty(_playerNickname))
        {
            _playerNickname = "Player";
        }
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
        // 이미 연결 중이면 차단
        if (_isConnecting) return;

        // 이미 연결되어 있으면 경고
        if (FusionManager.LocalRunner != null)
        {
            GameManager.Instance?.ShowWarningPanel("이미 방에 연결되어 있습니다.");
            _titleCanvas?.SetButtonsInteractable(true);
            return;
        }

        _isConnecting = true;

        _roomName = string.IsNullOrEmpty(roomName) ? $"Room_{Random.Range(1000, 9999)}" : roomName;
        _playerNickname = playerNickname;

        GameManager.MyLocalNickname = _playerNickname;

        StartHost();
    }

    public void JoinRoom(string roomName, string playerNickname)
    {
        // 이미 연결 중이면 차단
        if (_isConnecting) return;

        // 이미 연결되어 있으면 경고
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

        _isConnecting = true;

        _roomName = roomName;
        _playerNickname = playerNickname;

        GameManager.MyLocalNickname = _playerNickname;

        StartClient();
    }

    public void StartGame()
    {
        if (FusionManager.LocalRunner != null && FusionManager.LocalRunner.IsServer)
        {
            FusionManager.LocalRunner.LoadScene("Main");
        }
    }
    
    /// <summary>
    /// 챕터 씬을 로드합니다. (호스트만 실행 가능)
    /// </summary>
    public void LoadChapterScene(string sceneName)
    {
        if (FusionManager.LocalRunner != null && FusionManager.LocalRunner.IsServer)
        {
            FusionManager.LocalRunner.LoadScene(sceneName);
        }
        else
        {
            Debug.LogWarning("[TitleGameManager] Only host can load chapter scenes!");
        }
    }

    public void LeaveRoom()
    {
        // 비동기 종료
        LeaveRoomAsync();
    }

    public bool IsConnecting => _isConnecting;
    public string RoomName => _roomName;

    // ========== Network Connection Methods ==========

    private async void StartHost()
    {
        GameObject networkObject = null;
        try
        {
            // 기존 LocalRunner가 있으면 종료
            if (FusionManager.LocalRunner != null)
            {
                await FusionManager.LocalRunner.Shutdown();
                FusionManager.LocalRunner = null;
            }

            // NetworkRunner 생성 및 설정
            networkObject = new GameObject("NetworkRunner");
            DontDestroyOnLoad(networkObject);

            var runner = networkObject.AddComponent<NetworkRunner>();

            // FusionManager 싱글턴 보장
            if (FusionManager.Instance == null)
            {
                new GameObject("FusionManager").AddComponent<FusionManager>();
            }

            runner.AddCallbacks(FusionManager.Instance);
            runner.ProvideInput = true;

            // ✅ Physics2D 시뮬레이션을 위한 컴포넌트 추가 (필수!)
            var physicsSimulator = networkObject.AddComponent<Fusion.Addons.Physics.RunnerSimulatePhysics2D>();
            
            // ClientPhysicsSimulation을 SimulateAlways로 설정 (가장 부드러운 움직임)
            physicsSimulator.ClientPhysicsSimulation = Fusion.Addons.Physics.ClientPhysicsSimulation.SimulateAlways;
            Debug.Log("[TitleGameManager] RunnerSimulatePhysics2D added to NetworkRunner (Host) - ClientPhysicsSimulation: SimulateAlways");

            // 닉네임을 byte[]로 인코딩하여 ConnectionToken에 설정
            byte[] connectionToken = System.Text.Encoding.UTF8.GetBytes(_playerNickname);
            
            var result = await runner.StartGame(new StartGameArgs()
            {
                GameMode = GameMode.Host,
                SessionName = _roomName,
                PlayerCount = 2,
                ConnectionToken = connectionToken, // 닉네임 데이터 전달
                SceneManager = networkObject.AddComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = networkObject.AddComponent<NetworkObjectProviderDefault>()
            });

            if (result.Ok)
            {
                _titleCanvas?.ShowLobbyPanel();
            }
            else
            {
                string errorMessage = $"방 생성 실패: {Messages.GetShutdownReasonMessage(result.ShutdownReason)}";
                Debug.LogWarning($"[TitleGameManager] Host start failed: {result.ShutdownReason}");
                GameManager.Instance?.ShowWarningPanel(errorMessage);

                // 실패 시 정리
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

            // 예외 발생 시 정리
            FusionManager.LocalRunner = null;
            if (networkObject != null)
                Destroy(networkObject);
        }
        finally
        {
            _isConnecting = false;
            _titleCanvas?.SetButtonsInteractable(true);
            _titleCanvas?.UpdateLobbyUI();
        }
    }

    private async void StartClient()
    {
        GameObject networkObject = null;
        try
        {
            // 기존 LocalRunner가 있으면 종료
            if (FusionManager.LocalRunner != null)
            {
                await FusionManager.LocalRunner.Shutdown();
                FusionManager.LocalRunner = null;
            }

            // NetworkRunner 생성 및 설정
            networkObject = new GameObject("NetworkRunner");
            DontDestroyOnLoad(networkObject);

            var runner = networkObject.AddComponent<NetworkRunner>();

            // FusionManager 싱글턴 보장
            if (FusionManager.Instance == null)
            {
                new GameObject("FusionManager").AddComponent<FusionManager>();
            }

            runner.AddCallbacks(FusionManager.Instance);
            runner.ProvideInput = true;

            // ✅ Physics2D 시뮬레이션을 위한 컴포넌트 추가 (필수!)
            var physicsSimulator = networkObject.AddComponent<Fusion.Addons.Physics.RunnerSimulatePhysics2D>();
            
            // ClientPhysicsSimulation을 SimulateAlways로 설정 (가장 부드러운 움직임)
            physicsSimulator.ClientPhysicsSimulation = Fusion.Addons.Physics.ClientPhysicsSimulation.SimulateAlways;
            Debug.Log("[TitleGameManager] RunnerSimulatePhysics2D added to NetworkRunner (Client) - ClientPhysicsSimulation: SimulateAlways");

            // 닉네임을 byte[]로 인코딩하여 ConnectionToken에 설정
            byte[] connectionToken = System.Text.Encoding.UTF8.GetBytes(_playerNickname);
            
            var result = await runner.StartGame(new StartGameArgs()
            {
                GameMode = GameMode.Client,
                SessionName = _roomName,
                ConnectionToken = connectionToken, // 닉네임 데이터 전달
                SceneManager = networkObject.AddComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = networkObject.AddComponent<NetworkObjectProviderDefault>()
            });

            if (result.Ok)
            {
                _titleCanvas?.ShowLobbyPanel();
            }
            else
            {
                string errorMessage = $"방 접속 실패: {Messages.GetShutdownReasonMessage(result.ShutdownReason)}";
                Debug.LogWarning($"[TitleGameManager] Client connection failed: {result.ShutdownReason}");
                GameManager.Instance?.ShowWarningPanel(errorMessage);

                // 실패 시 정리
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

            // 예외 발생 시 정리
            FusionManager.LocalRunner = null;
            if (networkObject != null)
                Destroy(networkObject);
        }
        finally
        {
            _isConnecting = false;
            _titleCanvas?.SetButtonsInteractable(true);
            _titleCanvas?.UpdateLobbyUI();
        }
    }

    private async void LeaveRoomAsync()
    {
        // LocalRunner 종료
        if (FusionManager.LocalRunner != null)
        {
            await FusionManager.LocalRunner.Shutdown();
            FusionManager.LocalRunner = null;
        }

        // 혹시 남아있을 수 있는 NetworkRunner GameObject 정리
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
        // 로컬 플레이어일 경우 캐릭터 UI 업데이트
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
        // 종료된 러너가 현재 LocalRunner였다면 초기화
        if (runner == FusionManager.LocalRunner)
        {
            FusionManager.LocalRunner = null;
        }

        // 타이틀 씬에 있을 때만 UI 업데이트
        string currentSceneName = SceneManager.GetActiveScene().name;
        if (currentSceneName == "Title")
        {
            _isConnecting = false;
            
            // TitleCanvas가 존재하고 파괴되지 않았는지 확인
            if (_titleCanvas != null && _titleCanvas.gameObject != null)
            {
                _titleCanvas.ShowTitlePanel();
                _titleCanvas.SetButtonsInteractable(true);
            }
        }
        else
        {
            // 게임 씬에서는 MainGameManager가 처리하므로 여기서는 아무것도 하지 않음
            _isConnecting = false;
        }
    }

    private void OnDisconnected(NetworkRunner runner)
    {
        // 연결 끊김 처리 (Shutdown과 유사)
        if (runner == FusionManager.LocalRunner)
        {
            FusionManager.LocalRunner = null;
        }

        // 타이틀 씬에 있을 때만 UI 업데이트
        string currentSceneName = SceneManager.GetActiveScene().name;
        if (currentSceneName == "Title")
        {
            _isConnecting = false;
            GameManager.Instance?.ShowWarningPanel("네트워크 연결이 끊어졌습니다.");
            
            // TitleCanvas가 존재하고 파괴되지 않았는지 확인
            if (_titleCanvas != null && _titleCanvas.gameObject != null)
            {
                _titleCanvas.ShowTitlePanel();
                _titleCanvas.SetButtonsInteractable(true);
            }
        }
        else
        {
            // 게임 씬에서는 MainGameManager가 처리하므로 여기서는 아무것도 하지 않음
            _isConnecting = false;
        }
    }

    private void OnPlayerDataSpawned(PlayerRef player, NetworkRunner runner)
    {
        // 로컬 플레이어의 PlayerData가 스폰되면 캐릭터 UI 업데이트
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
}