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
    
    // 게임 상태 정의
    public enum GameState
    {
        Lobby,      // 로비 상태
        Playing,    // 게임 중
        Loading     // 로딩 중
    }
    
    // 현재 게임 상태
    public GameState State { get; private set; } = GameState.Lobby;
    
    // 플레이어 데이터 저장용 딕셔너리
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
    }
    
    void OnDestroy()
    {
        // 이벤트 연결 해제
        RemoveNetworkEvents();
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
        Debug.Log($"게임 상태 변경: {State}");
    }
    
    // 플레이어 접속 시 호출
    private void OnPlayerJoined(PlayerRef player, NetworkRunner runner)
    {
        Debug.Log($"GameManager: 플레이어 {player} 접속 처리");
        // 여기서 플레이어 데이터 초기화 등 처리
    }
    
    // 플레이어 나감 시 호출
    private void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        Debug.Log($"GameManager: 플레이어 {player} 나감 처리");
        // 플레이어 데이터 제거
        if (_playerData.ContainsKey(player))
        {
            _playerData.Remove(player);
        }
    }
    
    // 네트워크 종료 시 호출
    private void OnShutdown(NetworkRunner runner)
    {
        Debug.Log("GameManager: 네트워크 세션 종료");
        SetGameState(GameState.Lobby);
        _playerData.Clear();
    }
    
    // 연결 끊김 시 호출
    private void OnDisconnected(NetworkRunner runner)
    {
        Debug.Log("GameManager: 서버 연결 끊김");
        SetGameState(GameState.Lobby);
        _playerData.Clear();
    }
    
    // 플레이어 데이터 가져오기
    public PlayerData GetPlayerData(PlayerRef player, NetworkRunner runner)
    {
        if (_playerData.ContainsKey(player))
        {
            return _playerData[player];
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
}