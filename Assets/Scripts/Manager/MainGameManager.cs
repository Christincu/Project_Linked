using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Fusion;

public class MainGameManager : MonoBehaviour
{
    [Header("Player Settings")]
    [SerializeField] private NetworkPrefabRef PlayerPrefab;
    
    [Header("Spawn Settings")]
    [SerializeField] private Vector2[] spawnPositions = new Vector2[]
    {
        new Vector2(0, 2),
        new Vector2(0, -2),
        new Vector2(-2, 0),
        new Vector2(2, 0)
    };
    
    // Singleton instance
    public static MainGameManager Instance { get; private set; }
    
    // 생성된 플레이어 오브젝트 추적
    private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    private NetworkRunner _runner;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void Start()
    {
        // Main 씬 진입 시 플레이어 스폰
        SpawnAllPlayers(FusionManager.LocalRunner);
        
        // 플레이어 참여 이벤트 구독 (게임 중 참여하는 플레이어 처리)
        FusionManager.OnPlayerJoinedEvent += OnPlayerJoinedDuringGame;
    }
    
    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        
        // 이벤트 구독 해제
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoinedDuringGame;
    }
    
    // 게임 중 플레이어 참여 처리
    private void OnPlayerJoinedDuringGame(PlayerRef player, NetworkRunner runner)
    {
        // Main 씬에서 참여한 경우 즉시 스폰
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Main")
        {
            if (runner.IsServer && !_spawnedPlayers.ContainsKey(player))
            {
                int spawnIndex = _spawnedPlayers.Count;
                Debug.Log($"Player {player} joined during game, spawning...");
                SpawnPlayer(runner, player, spawnIndex);
            }
        }
    }
    
    // 모든 플레이어 스폰
    private void SpawnAllPlayers(NetworkRunner runner)
    {
        // 서버에서만 플레이어 오브젝트 생성
        if (!runner.IsServer)
        {
            Debug.Log("Not server, skipping player spawn");
            return;
        }
        
        Debug.Log($"Spawning players for {runner.ActivePlayers.Count()} active players");
        
        int spawnIndex = 0;
        foreach (var player in runner.ActivePlayers)
        {
            SpawnPlayer(runner, player, spawnIndex);
            spawnIndex++;
        }
    }
    
    // 개별 플레이어 스폰
    private void SpawnPlayer(NetworkRunner runner, PlayerRef player, int spawnIndex)
    {
        // 이미 스폰된 플레이어는 스킵
        if (_spawnedPlayers.ContainsKey(player))
        {
            Debug.Log($"Player {player} already spawned, skipping");
            return;
        }
        
        // 스폰 위치 결정
        Vector2 spawnPosition = spawnPositions[spawnIndex % spawnPositions.Length];
        
        // InitialPlayerData 가져오기
        InitialPlayerData initialData = null;
        if (GameDataManager.Instance != null)
        {
            initialData = GameDataManager.Instance.InitialPlayerData;
        }
        
        // 플레이어 오브젝트 생성
        NetworkObject playerObject = runner.Spawn(
            PlayerPrefab, 
            spawnPosition, 
            Quaternion.identity, 
            player
        );
        
        if (playerObject != null)
        {
            _spawnedPlayers[player] = playerObject;
            
            // PlayerController 설정
            PlayerController playerController = playerObject.GetComponent<PlayerController>();
            if (playerController != null)
            {
                // PlayerState 이벤트 구독 (선택적)
                var state = playerController.State;
                if (state != null)
                {
                    state.OnHealthChanged += (current, max) => OnPlayerHealthChanged(player, current, max);
                    state.OnDeath += (killer) => OnPlayerDied(player, killer);
                    state.OnRespawned += () => OnPlayerRespawned(player);
                }
                
                // PlayerData에서 캐릭터 인덱스 가져오기
                PlayerData playerData = GameManager.Instance?.GetPlayerData(player, runner);
                if (playerData != null)
                {
                    int characterIndex = playerData.CharacterIndex;
                    playerController.SetCharacterIndex(characterIndex);
                    
                    // PlayerData에 실제 플레이어 오브젝트 연결 (중요!)
                    if (runner.IsServer)
                    {
                        playerData.PlayerInstance = playerObject;
                        Debug.Log($"PlayerData.PlayerInstance set for player {player}");
                    }
                    
                    Debug.Log($"Player {player} spawned with character index {characterIndex} at position {spawnPosition}");
                }
                else
                {
                    Debug.LogWarning($"PlayerData not found for player {player}, using default settings");
                }
            }
            else
            {
                Debug.LogError($"PlayerController not found on spawned player object!");
            }
        }
        else
        {
            Debug.LogError($"Failed to spawn player {player}");
        }
    }
    
    // 플레이어 제거 처리
    public void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        if (_spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
        {
            // PlayerData 정리
            if (runner.IsServer)
            {
                PlayerData playerData = GameManager.Instance?.GetPlayerData(player, runner);
                if (playerData != null)
                {
                    playerData.PlayerInstance = null;
                    Debug.Log($"PlayerData.PlayerInstance cleared for player {player}");
                }
            }
            
            // 플레이어 오브젝트 제거
            if (playerObject != null && runner.IsServer)
            {
                runner.Despawn(playerObject);
            }
            _spawnedPlayers.Remove(player);
            Debug.Log($"Player {player} despawned");
        }
    }

    #region Public Methods
    /// <summary>
    /// 로컬 플레이어의 PlayerController를 가져옵니다.
    /// </summary>
    public PlayerController GetLocalPlayer()
    {
        if (_runner == null) return null;

        var localPlayerRef = _runner.LocalPlayer;
        if (_spawnedPlayers.TryGetValue(localPlayerRef, out var playerObj))
        {
            return playerObj.GetComponent<PlayerController>();
        }

        return null;
    }

    /// <summary>
    /// 특정 PlayerRef의 PlayerController를 가져옵니다.
    /// </summary>
    public PlayerController GetPlayer(PlayerRef playerRef)
    {
        if (_spawnedPlayers.TryGetValue(playerRef, out var playerObj))
        {
            return playerObj.GetComponent<PlayerController>();
        }

        return null;
    }

    /// <summary>
    /// 모든 플레이어의 PlayerController를 가져옵니다.
    /// </summary>
    public List<PlayerController> GetAllPlayers()
    {
        var players = new List<PlayerController>();
        foreach (var kvp in _spawnedPlayers)
        {
            var controller = kvp.Value.GetComponent<PlayerController>();
            if (controller != null)
            {
                players.Add(controller);
            }
        }
        return players;
    }
    #endregion

    #region Event Handlers
    private void OnPlayerHealthChanged(PlayerRef player, float current, float max)
    {
        Debug.Log($"[MainGameManager] Player {player} HP changed: {current}/{max}");
        // UI 업데이트 등 추가 로직
    }

    private void OnPlayerDied(PlayerRef player, PlayerRef killer)
    {
        Debug.Log($"[MainGameManager] Player {player} died. Killer: {killer}");
        // 추가 게임 로직 (점수, 통계 등)
    }

    private void OnPlayerRespawned(PlayerRef player)
    {
        Debug.Log($"[MainGameManager] Player {player} respawned");
        // 추가 리스폰 로직
    }
    #endregion
}
