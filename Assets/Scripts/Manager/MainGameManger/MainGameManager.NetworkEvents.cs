using System.Collections;
using System.Linq;
using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;

/// <summary>
/// 네트워크 이벤트 처리 관련 기능을 모은 partial 클래스입니다.
/// </summary>
public partial class MainGameManager
{
    /// <summary>
    /// 이벤트 구독을 등록합니다.
    /// </summary>
    private void RegisterEvents()
    {
        FusionManager.OnPlayerJoinedEvent += OnPlayerJoinedDuringGame;
        FusionManager.OnPlayerLeftEvent += OnPlayerLeft;
        FusionManager.OnShutdownEvent += OnNetworkShutdown;
        FusionManager.OnDisconnectedEvent += OnDisconnected;
    }
    
    /// <summary>
    /// 플레이어가 떠났을 때 호출됩니다.
    /// </summary>
    public void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        bool wasInDictionary = _spawnedPlayers.ContainsKey(player);
        
        if (runner.IsServer)
        {
            PlayerData playerData = GameManager.Instance?.GetPlayerData(player, runner);
            if (playerData != null && playerData.Object != null && playerData.Object.IsValid)
            {
                runner.Despawn(playerData.Object);
            }
        }
        
        if (_spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
        {
            if (playerObject != null && playerObject.IsValid && runner.IsServer)
            {
                runner.Despawn(playerObject);
            }
            _spawnedPlayers.Remove(player);
        }
        
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetPlayerData(player, null);
        }
        
        if (!_isTestMode && SceneManager.GetActiveScene().name == "Main")
        {
            if (runner != null && runner.IsRunning)
            {
                int remainingPlayers = runner.ActivePlayers.Count();
                if (remainingPlayers < 2)
                {
                    GameManager.Instance?.ShowWarningPanel("상대방이 나갔습니다.");
                }
            }
        }
    }
    
    /// <summary>
    /// 네트워크 세션이 종료되었을 때 호출됩니다 (호스트가 나가거나 세션이 종료됨)
    /// </summary>
    private void OnNetworkShutdown(NetworkRunner runner)
    {
        HandleNetworkDisconnection(runner, "Network shutdown detected");
    }

    /// <summary>
    /// 서버와의 연결이 끊어졌을 때 호출됩니다 (클라이언트 관점)
    /// </summary>
    private void OnDisconnected(NetworkRunner runner)
    {
        HandleNetworkDisconnection(runner, "Disconnected from server");
    }
    
    /// <summary>
    /// 네트워크 연결 종료 공통 처리
    /// </summary>
    private void HandleNetworkDisconnection(NetworkRunner runner, string reason)
    {
        if (!_isTestMode && runner != null && !runner.IsServer)
        {
            GameManager.Instance?.ShowWarningPanel("호스트가 나갔습니다. 타이틀로 돌아갑니다.");
        }
        
        if (!_isTestMode)
        {
            StartCoroutine(ReturnToTitleAfterDelay());
        }
    }

    /// <summary>
    /// 일정 시간 후 타이틀 씬으로 돌아갑니다.
    /// </summary>
    private IEnumerator ReturnToTitleAfterDelay()
    {
        yield return new WaitForSeconds(2f);
        
        if (_runner != null)
        {
            _runner.Shutdown();
        }
        
        SceneManager.LoadScene("Title");
    }
}

