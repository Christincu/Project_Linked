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
    
    // 다른 클래스에서 쉽게 접근할 수 있도록 static으로 만듦
    public static NetworkRunner LocalRunner;
    
    // 플레이어 데이터 프리팹 참조 (나중에 만들 예정)
    public NetworkPrefabRef PlayerDataPrefab;
    
    // 이벤트들 - C# Action 사용 (간단한 방식)
    public static Action<PlayerRef, NetworkRunner> OnPlayerJoinedEvent;
    public static Action<PlayerRef, NetworkRunner> OnPlayerLeftEvent;
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
        Debug.Log($"플레이어 {player}가 접속했습니다!");
        
        // 로컬 플레이어인 경우 저장
        if (runner.LocalPlayer == player)
        {
            LocalRunner = runner;
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
        Debug.Log($"플레이어 {player}가 나갔습니다!");
        OnPlayerLeftEvent?.Invoke(player, runner);
    }
    
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log("네트워크 세션이 종료되었습니다!");
        OnShutdownEvent?.Invoke(runner);
    }
    
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log("서버와 연결이 끊어졌습니다!");
        OnDisconnectedEvent?.Invoke(runner);
    }
    
    // 연결 요청 처리
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        // 모든 연결 요청을 승인
        request.Accept();
    }
    
    // 사용하지 않는 콜백들 (빈 구현)
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
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