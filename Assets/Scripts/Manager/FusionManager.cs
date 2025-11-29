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

    /// <summary>
    /// 초기화 완료 여부를 나타냅니다.
    /// </summary>
    public bool IsInitialized { get; private set; }

    // Singleton initialization
    void Awake()
    {
        // 싱글톤 인스턴스만 설정하고, 초기화는 GameManager에서 제어
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    /// <summary>
    /// FusionManager를 초기화합니다. (GameManager에서 호출)
    /// </summary>
    public void Initialize()
    {
        if (IsInitialized)
        {
            Debug.LogWarning("[FusionManager] Already initialized. Skipping.");
            return;
        }
        
        IsInitialized = true;
    }

    void Start()
    {
    }

    // 네트워크 콜백들 - Photon Fusion이 자동으로 호출해줌
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner.LocalPlayer == player)
        {
            LocalRunner = runner;
        }

        // ConnectionToken에서 닉네임 읽기 (StartGameArgs.ConnectionToken으로 전달된 데이터)
        string playerNickname = $"Player_{player.AsIndex}";
        
        try
        {
            byte[] connectionToken = runner.GetPlayerConnectionToken(player);
            if (connectionToken != null && connectionToken.Length > 0)
            {
                playerNickname = System.Text.Encoding.UTF8.GetString(connectionToken);
            }
            else if (runner.LocalPlayer == player)
            {
                string savedNick = GameManager.MyLocalNickname;
                if (!string.IsNullOrEmpty(savedNick))
                {
                    playerNickname = savedNick;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[FusionManager] Failed to get ConnectionToken for player {player}: {e.Message}");
            if (runner.LocalPlayer == player)
            {
                string savedNick = GameManager.MyLocalNickname;
                if (!string.IsNullOrEmpty(savedNick))
                {
                    playerNickname = savedNick;
                }
            }
        }

        // 서버에서만 플레이어 데이터 생성 (테스트 모드 제외)
        bool isTestMode = MainGameManager.Instance != null && MainGameManager.Instance.IsTestMode;
        
        if (runner.IsServer && !isTestMode)
        {
            if (runner.GetPlayerObject(player) != null)
            {
                SetNicknameToPlayerData(runner, player, playerNickname);
                OnPlayerJoinedEvent?.Invoke(player, runner);
                return;
            }

            PlayerData existingData = GameManager.Instance?.GetPlayerData(player, runner);
            if (existingData != null && existingData.Object != null && existingData.Object.IsValid)
            {
                SetNicknameToPlayerData(runner, player, playerNickname);
                OnPlayerJoinedEvent?.Invoke(player, runner);
                return;
            }

            if (PlayerDataPrefab != null)
            {
                NetworkObject playerDataObj = runner.Spawn(PlayerDataPrefab, inputAuthority: player);
                
                if (playerDataObj != null && playerDataObj.IsValid && playerDataObj.TryGetComponent(out PlayerData playerData))
                {
                    if (playerData.Object.HasInputAuthority && playerData.Object.InputAuthority == player)
                    {
                        playerData.SetNickname(playerNickname);
                    }
                    else
                    {
                        StartCoroutine(SetNicknameWhenReady(runner, player, playerNickname, playerData));
                    }
                }
                else
                {
                    StartCoroutine(SetNicknameWhenReady(runner, player, playerNickname, null));
                }
            }
            else
            {
                Debug.LogWarning("[FusionManager] PlayerDataPrefab is null! Cannot spawn PlayerData.");
            }
        }

        // 이벤트 발생
        OnPlayerJoinedEvent?.Invoke(player, runner);
    }
    
    /// <summary>
    /// PlayerData가 준비되면 닉네임을 설정합니다. (최소 대기 시간)
    /// </summary>
    private IEnumerator SetNicknameWhenReady(NetworkRunner runner, PlayerRef player, string nickname, PlayerData playerData)
    {
        // 최대 10프레임 대기 (약 0.17초 @ 60fps)
        int maxAttempts = 10;
        int attempts = 0;
        
        while (attempts < maxAttempts)
        {
            // PlayerData가 아직 전달되지 않았으면 찾기
            if (playerData == null)
            {
                NetworkObject playerDataObj = runner.GetPlayerObject(player);
                if (playerDataObj != null && playerDataObj.IsValid && playerDataObj.TryGetComponent(out playerData))
                {
                    // 찾았음
                }
            }
            
            if (playerData != null && playerData.Object != null && playerData.Object.IsValid)
            {
                if (playerData.Object.HasInputAuthority && playerData.Object.InputAuthority == player)
                {
                    playerData.SetNickname(nickname);
                    yield break;
                }
            }
            
            yield return null; // 한 프레임 대기
            attempts++;
        }
        
        Debug.LogWarning($"[FusionManager] Failed to set nickname for player {player} after {maxAttempts} attempts. PlayerData.Spawned() will handle it from memory.");
    }
    
    /// <summary>
    /// 기존 PlayerData에 닉네임을 설정합니다.
    /// </summary>
    private void SetNicknameToPlayerData(NetworkRunner runner, PlayerRef player, string nickname)
    {
        PlayerData playerData = GameManager.Instance?.GetPlayerData(player, runner);
        if (playerData != null && playerData.Object != null && playerData.Object.IsValid)
        {
            if (playerData.Object.HasInputAuthority && playerData.Object.InputAuthority == player)
            {
                playerData.SetNickname(nickname);
            }
            else if (runner.LocalPlayer == player)
            {
                playerData.SetNickname(nickname);
            }
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        OnPlayerLeftEvent?.Invoke(player, runner);
    }

    public void OnPlayerChangeCharacter(NetworkRunner runner, PlayerRef player, int characterIndex)
    {
        OnPlayerChangeCharacterEvent?.Invoke(player, runner, characterIndex);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        OnShutdownEvent?.Invoke(runner);
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        OnDisconnectedEvent?.Invoke(runner);
    }

    // 연결 요청 처리
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        // 모든 연결 요청을 승인
        // ConnectionToken은 OnPlayerJoined에서 runner.GetPlayerConnectionToken(player)로 읽을 수 있습니다.
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

        // [테스트 모드 전용] ControlledSlot 설정
        // 주의: 일반적으로 Input은 순수한 입력(키 누름, 마우스 움직임)만 포함해야 하며,
        // 어떤 슬롯을 제어할지는 PlayerRef와 Game State에 의해 결정되어야 합니다.
        // 하지만 테스트 모드에서 여러 캐릭터를 제어하기 위한 임시 방편으로 유지합니다.
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
        OnSceneLoadDoneEvent?.Invoke(runner);
    }
    public void OnSceneLoadStart(NetworkRunner runner) 
    {
        LoadingPanel.Show();
    }
}