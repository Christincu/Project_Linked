using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using System.Threading.Tasks;

/// <summary>
/// 타이틀 씬의 로직을 담당하는 매니저입니다.
/// UI 인터랙션 처리 및 Fusion 네트워크 연결(방 생성/참가) 요청을 수행합니다.
/// </summary>
public class TitleGameManager : MonoBehaviour, ISceneGameManager
{
    private TitleCanvas _titleCanvas;
    private string _currentRoomName;
    private bool _isConnecting = false;
    private bool _isTestMode = false;

    public bool IsTestMode => _isTestMode;
    public bool IsConnecting => _isConnecting;
    public string RoomName => _currentRoomName;

    void OnDestroy()
    {
        UnregisterNetworkEvents();
    }

    /// <summary>
    /// GameManager에서 호출하는 초기화 진입점입니다.
    /// </summary>
    public void OnInitialize(GameManager gameManager, GameDataManager gameDataManager)
    {
        _isConnecting = false;
        _titleCanvas = FindObjectOfType<TitleCanvas>();
        _titleCanvas.SetTitleGameManager(this);
        RegisterNetworkEvents();
    }

    /// <summary>
    /// 방 생성 버튼 클릭 시 호출됩니다. (Host 모드 시작)
    /// </summary>
    public void CreateRoom(string roomName, string nickname)
    {
        string finalRoomName = string.IsNullOrEmpty(roomName)
            ? $"Room_{Random.Range(1000, 9999)}"
            : roomName;
        
        ConnectToSession(GameMode.Host, finalRoomName, nickname);
    }

    /// <summary>
    /// 방 참가 버튼 클릭 시 호출됩니다. (Client 모드 시작)
    /// </summary>
    public void JoinRoom(string roomName, string nickname)
    {
        if (string.IsNullOrEmpty(roomName))
        {
            GameManager.Instance?.ShowWarningPanel("방 이름을 입력해주세요.");
            return;
        }

        ConnectToSession(GameMode.Client, roomName, nickname);
    }

    /// <summary>
    /// 로비 대기실에서 게임 시작 버튼을 누를 때 호출됩니다. (Host 전용)
    /// 메인 게임 씬을 로드합니다.
    /// </summary>
    public void StartGame()
    {
        if (FusionManager.LocalRunner != null && FusionManager.LocalRunner.IsServer)
        {
            FusionManager.LocalRunner.LoadScene("Main");
        }
    }

    /// <summary>
    /// 특정 챕터 씬을 로드합니다. (호스트만 실행 가능)
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

    /// <summary>
    /// 방 나가기 버튼 클릭 시 호출됩니다.
    /// </summary>
    public void LeaveRoom()
    {
        _ = LeaveRoomAsync();
    }

    /// <summary>
    /// FusionManager를 통해 실제 세션 연결을 요청합니다.
    /// </summary>
    private async void ConnectToSession(GameMode mode, string roomName, string nickname)
    {
        _isConnecting = true;
        _currentRoomName = roomName;
        _titleCanvas.SetButtonsInteractable(false);

        GameManager.MyLocalNickname = nickname;

        string currentSceneName = SceneManager.GetActiveScene().name;
        await FusionManager.Instance.StartGameSession(mode, roomName, currentSceneName);

        _isConnecting = false;
        _titleCanvas.SetButtonsInteractable(true);
    }

    private async Task LeaveRoomAsync()
    {
        _titleCanvas.SetButtonsInteractable(false);

        if (FusionManager.LocalRunner != null)
        {
            await FusionManager.LocalRunner.Shutdown();
        }
    }

    private void RegisterNetworkEvents()
    {
        FusionManager.OnPlayerJoinedEvent += OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent += OnPlayerLeft;
        FusionManager.OnPlayerChangeCharacterEvent += OnPlayerChangeCharacter;
        FusionManager.OnShutdownEvent += OnShutdown;
        FusionManager.OnDisconnectedEvent += OnDisconnected;
        PlayerData.OnPlayerDataSpawned += OnPlayerDataSpawned;
    }

    private void UnregisterNetworkEvents()
    {
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoined;
        FusionManager.OnPlayerLeftEvent -= OnPlayerLeft;
        FusionManager.OnPlayerChangeCharacterEvent -= OnPlayerChangeCharacter;
        FusionManager.OnShutdownEvent -= OnShutdown;
        FusionManager.OnDisconnectedEvent -= OnDisconnected;
        PlayerData.OnPlayerDataSpawned -= OnPlayerDataSpawned;
    }

    private void OnPlayerJoined(PlayerRef player, NetworkRunner runner)
    {
        _titleCanvas.ShowLobbyPanel();
        _titleCanvas.UpdateLobbyUI();
    }

    private void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        _titleCanvas.UpdateLobbyUI();
    }

    private void OnPlayerChangeCharacter(PlayerRef player, NetworkRunner runner, int characterIndex)
    {
        if (runner.LocalPlayer == player && GameDataManager.Instance != null)
        {
            var characterData = GameDataManager.Instance.CharacterService.GetCharacter(characterIndex);
            if (characterData != null)
            {
                _titleCanvas.UpdateCharacterUI(characterData);
            }
        }
        _titleCanvas.UpdateLobbyUI();
    }

    private void OnPlayerDataSpawned(PlayerRef player, NetworkRunner runner)
    {
        if (runner.LocalPlayer == player)
        {
            var playerData = GameManager.Instance?.GetPlayerData(player, runner);
            if (playerData != null && GameDataManager.Instance != null)
            {
                var characterData = GameDataManager.Instance.CharacterService.GetCharacter(playerData.CharacterIndex);
                if (characterData != null)
                    _titleCanvas.UpdateCharacterUI(characterData);
            }
        }
        _titleCanvas.UpdateLobbyUI();
    }

    private void OnShutdown(NetworkRunner runner) => HandleDisconnection("네트워크 세션이 종료되었습니다.");
    private void OnDisconnected(NetworkRunner runner) => HandleDisconnection("서버와의 연결이 끊어졌습니다.");

    /// <summary>
    /// 연결 종료 시 UI를 타이틀 화면으로 복구합니다.
    /// </summary>
    private void HandleDisconnection(string message)
    {
        if (SceneManager.GetActiveScene().name == "Title")
        {
            _isConnecting = false;
            GameManager.Instance.ShowWarningPanel(message);

            if (_titleCanvas != null)
            {
                _titleCanvas.ShowTitlePanel();
                _titleCanvas.SetButtonsInteractable(true);
            }
        }
    }
}