using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;

public class FusionManager : MonoBehaviour, INetworkRunnerCallbacks
{
    // Singleton pattern
    public static FusionManager Instance { get; private set; }
    public static NetworkRunner LocalRunner;

    public NetworkPrefabRef PlayerDataPrefab;

    public static Action<PlayerRef, NetworkRunner> OnPlayerJoinedEvent;
    public static Action<PlayerRef, NetworkRunner> OnPlayerLeftEvent;
    public static Action<PlayerRef, NetworkRunner, int> OnPlayerChangeCharacterEvent;
    public static Action<NetworkRunner> OnShutdownEvent;
    public static Action<NetworkRunner> OnDisconnectedEvent;
    public static Action<NetworkRunner> OnSceneLoadDoneEvent;

    // Singleton initialization
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("FusionManager singleton created");
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        Debug.Log($"FusionManager Start - PlayerDataPrefab: {PlayerDataPrefab}");
    }

    // 네트워크 콜백들 - Photon Fusion이 자동으로 호출해줌
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player} has joined!");

        // 로컬 플레이어인 경우 저장
        if (runner.LocalPlayer == player)
        {
            LocalRunner = runner;
            // ProvideInput은 StartGame 전에 이미 설정되어 있음 (TitleCanvas.cs 참조)
            Debug.Log($"LocalRunner set to: {LocalRunner}");
        }

        // 서버에서만 플레이어 데이터 생성
        if (runner.IsServer)
        {
            // 플레이어 데이터 오브젝트 생성
            if (PlayerDataPrefab != null)
            {
                runner.Spawn(PlayerDataPrefab, inputAuthority: player);
                Debug.Log($"PlayerData spawned for player: {player}");
            }
            else
            {
                Debug.LogWarning("PlayerDataPrefab is null! Cannot spawn PlayerData.");
            }
        }

        // 이벤트 발생
        OnPlayerJoinedEvent?.Invoke(player, runner);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player} has left!");

        // MainGameManager에 플레이어 퇴장 알림 (Main 씬에서만)
        if (MainGameManager.Instance != null)
        {
            MainGameManager.Instance.OnPlayerLeft(player, runner);
        }

        OnPlayerLeftEvent?.Invoke(player, runner);
    }

    public void OnPlayerChangeCharacter(NetworkRunner runner, PlayerRef player, int characterIndex)
    {
        Debug.Log($"Player {player} changed character to {characterIndex}!");
        OnPlayerChangeCharacterEvent?.Invoke(player, runner, characterIndex);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log("Network session has been shut down!");
        OnShutdownEvent?.Invoke(runner);
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log("Disconnected from server!");
        OnDisconnectedEvent?.Invoke(runner);
    }

    // 연결 요청 처리
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        // 모든 연결 요청을 승인
        request.Accept();
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        InputData currentInput = new InputData();

        // WASD 키보드 입력
        currentInput.Buttons.Set(InputButton.LEFT, Input.GetKey(KeyCode.A));
        currentInput.Buttons.Set(InputButton.RIGHT, Input.GetKey(KeyCode.D));
        currentInput.Buttons.Set(InputButton.UP, Input.GetKey(KeyCode.W));
        currentInput.Buttons.Set(InputButton.DOWN, Input.GetKey(KeyCode.S));

        // 마우스 버튼
        currentInput.MouseButtons.Set(InputMouseButton.LEFT, Input.GetMouseButton(0));
        currentInput.MouseButtons.Set(InputMouseButton.RIGHT, Input.GetMouseButton(1));

        // 마우스 이동 및 위치
        currentInput.MouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        currentInput.MousePosition = Input.mousePosition;
        currentInput.MouseScroll = Input.mouseScrollDelta.y;

        // 테스트 모드 슬롯 설정 (MainGameManager의 IsTestMode 사용)
        if (MainGameManager.Instance != null && MainGameManager.Instance.IsTestMode)
        {
            currentInput.ControlledSlot = MainGameManager.SelectedSlot;
        }
        else
        {
            currentInput.ControlledSlot = 0;
        }

        input.Set(currentInput);
    }

    // 사용하지 않는 콜백들 (빈 구현)
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) 
    {
        Debug.Log($"[FusionManager] Scene load done! Current scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        
        // 로딩 화면 종료는 MainGameManager에서 플레이어 스폰 완료 후 처리
        
        OnSceneLoadDoneEvent?.Invoke(runner);
    }
    public void OnSceneLoadStart(NetworkRunner runner) 
    {
        Debug.Log($"[FusionManager] Scene load start!");
        
        LoadingPanel.Show();
    }
}