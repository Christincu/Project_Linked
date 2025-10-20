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
            // 실제 멀티 환경에서도 로컬 입력을 러너에 제공
            runner.ProvideInput = true;
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
    
    public void OnInput(NetworkRunner runner, NetworkInput input) {

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
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
}