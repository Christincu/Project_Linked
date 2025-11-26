using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;

/// <summary>
/// 메인 게임 씬의 게임 관리자입니다.
/// 테스트 모드(로컬 다중 플레이어 시뮬레이션)와 실제 네트워크 모드를 모두 처리합니다.
/// </summary>
public partial class MainGameManager : MonoBehaviour
{
    [Header("Mode & Settings")]
    [SerializeField] private bool _isTestMode = false;
    
    [Header("Player Prefabs & Data")]
    [SerializeField] private NetworkPrefabRef _playerPrefab;
    [SerializeField] private int _firstCharacterIndex = 0;
    [SerializeField] private int _secondCharacterIndex = 1;
    
    [Header("Spawn Settings")]
    [SerializeField] private Vector2[] _spawnPositions = new Vector2[]
    {
        new Vector2(0, 2),
        new Vector2(0, -2)
    };
    [SerializeField] private List<EnemySpawner> _enemySpawners = new List<EnemySpawner>();

    [Header("Level Settings")]
    [SerializeField] private GameObject _mapDoorObject;
    [SerializeField] private Collider2D _mapDoorTriggerColider;

    // Singleton instance
    public static MainGameManager Instance { get; private set; }
    
    // 생성된 플레이어 오브젝트 추적
    private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    private NetworkRunner _runner;
    
    // 테스트 모드 전용 필드
    public static int SelectedSlot = 0; // 0: first, 1: second
    private NetworkObject _playerObj1;
    private NetworkObject _playerObj2;
    public bool IsTestMode => _isTestMode;

    // 맵 문 상태
    private bool _isMapDoorClosed = false;
    
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
    
    async void Start()
    {
        // 게임 중 플레이어 참여 이벤트 구독
        FusionManager.OnPlayerJoinedEvent += OnPlayerJoinedDuringGame;
        FusionManager.OnPlayerLeftEvent += OnPlayerLeft;
        FusionManager.OnShutdownEvent += OnNetworkShutdown;
        FusionManager.OnDisconnectedEvent += OnDisconnected;
        
        // BarrierVisualizationManager 초기화 (게임 씬에서만 필요)
        _ = BarrierVisualizationManager.Instance;

        // 맵 문 초기 상태 설정: 시작 시 문은 "열린" 상태로 두고(비활성화),
        // 모든 플레이어가 트리거 안에 들어오면 닫힐 때 활성화되도록 한다.
        InitializeMapDoorState();
        
        if (_isTestMode)
        {
            await StartTestSession();
        }
        else
        {
            StartCoroutine(WaitForRunnerAndSpawn());
        }
    }
    
    void Update()
    {
        if (_isTestMode)
        {
            HandleTestModeInput();
        }

        // 레벨 디자인: 모든 플레이어가 문 트리거에 들어왔는지 확인
        UpdateMapDoorState();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        
        // 이벤트 구독 해제
        FusionManager.OnPlayerJoinedEvent -= OnPlayerJoinedDuringGame;
        FusionManager.OnPlayerLeftEvent -= OnPlayerLeft;
        FusionManager.OnShutdownEvent -= OnNetworkShutdown;
        FusionManager.OnDisconnectedEvent -= OnDisconnected;
    }
    
    // =========================================================
    // 네트워크 모드 로직
    // =========================================================

    /// <summary>
    /// 맵 문과 관련된 초기 상태를 설정합니다.
    /// - 씬 시작 시 문은 열린 상태(비활성화)로 두고
    /// - 모든 플레이어가 트리거에 들어왔을 때 닫히면서 활성화되도록 합니다.
    /// </summary>
    private void InitializeMapDoorState()
    {
        _isMapDoorClosed = false;

        // 문 오브젝트가 설정되어 있다면 비활성화해서 "열린" 상태로 시작
        if (_mapDoorObject != null)
        {
            _mapDoorObject.SetActive(false);
        }
    }

    private IEnumerator WaitForRunnerAndSpawn()
    {
        NetworkRunner runner = FusionManager.LocalRunner ?? FindObjectOfType<NetworkRunner>();
        
        while (runner == null || !runner.IsRunning)
        {
            yield return null;
            runner = FusionManager.LocalRunner ?? FindObjectOfType<NetworkRunner>();
        }
        
        _runner = runner;
        
        yield return new WaitForSeconds(0.5f);
        
        if (runner.IsServer)
        {
            yield return StartCoroutine(SpawnAllPlayersAsync(runner));
        }
        else
        {
            // 클라이언트: 서버가 스폰한 플레이어를 찾아서 딕셔너리에 추가
            yield return new WaitForSeconds(1.0f);
            FindAndRegisterSpawnedPlayers(runner);
        }
        
        InitializeMainCameraForNetworkMode();
        GameManager.Instance?.FinishLoadingScreen();
    }
    
    /// <summary>
    /// 클라이언트에서 이미 스폰된 플레이어를 찾아서 딕셔너리에 등록합니다.
    /// </summary>
    private void FindAndRegisterSpawnedPlayers(NetworkRunner runner)
    {
        PlayerController[] allPlayers = FindObjectsOfType<PlayerController>();
        int newlyRegistered = 0;
        
        foreach (var player in allPlayers)
        {
            if (player.Object != null)
            {
                PlayerRef playerRef = player.Object.InputAuthority;
                
                // PlayerRef.None이 아니고 아직 등록되지 않은 경우에만 추가
                if (playerRef != PlayerRef.None && !_spawnedPlayers.ContainsKey(playerRef))
                {
                    _spawnedPlayers[playerRef] = player.Object;
                    newlyRegistered++;
                    Debug.Log($"[MainGameManager] Client: Registered player {playerRef} (Local: {playerRef == runner.LocalPlayer})");
                }
            }
        }
        
        if (newlyRegistered > 0)
        {
            Debug.Log($"[MainGameManager] Client: Registered {newlyRegistered} new player(s). Total: {_spawnedPlayers.Count}");
        }
    }
    
    private void InitializeMainCameraForNetworkMode()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;
        
        MainCameraController cameraController = mainCamera.GetComponent<MainCameraController>();
        if (cameraController == null) return;
        
        PlayerController localPlayer = GetLocalPlayer();
        if (localPlayer != null)
        {
            cameraController.SetTarget(localPlayer);
            Debug.Log($"[MainGameManager] Camera target set to local player");
        }
    }
    
    // 게임 중 플레이어 참여 처리
    private void OnPlayerJoinedDuringGame(PlayerRef player, NetworkRunner runner)
    {
        if (SceneManager.GetActiveScene().name == "Main")
        {
            if (runner.IsServer && !_spawnedPlayers.ContainsKey(player))
            {
                // 서버: 플레이어 스폰
                int spawnIndex = _spawnedPlayers.Count;
                StartCoroutine(SpawnPlayerAsync(runner, player, spawnIndex));
            }
            else if (!runner.IsServer)
            {
                // 클라이언트: 서버가 스폰한 플레이어가 동기화될 때까지 대기 후 등록
                StartCoroutine(WaitAndRegisterPlayer(runner, player));
            }
        }
    }
    
    /// <summary>
    /// 클라이언트에서 새로 참여한 플레이어가 스폰될 때까지 대기 후 등록합니다.
    /// </summary>
    private IEnumerator WaitAndRegisterPlayer(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[MainGameManager] Client: Waiting for player {player} to spawn...");
        
        // 플레이어가 스폰될 때까지 대기
        int maxAttempts = 50; // 5초 (0.1초 * 50)
        int attempts = 0;
        
        while (attempts < maxAttempts)
        {
            FindAndRegisterSpawnedPlayers(runner);
            
            if (_spawnedPlayers.ContainsKey(player))
            {
                Debug.Log($"[MainGameManager] Client: Player {player} registered successfully");
                yield break;
            }
            
            yield return new WaitForSeconds(0.1f);
            attempts++;
        }
        
        Debug.LogWarning($"[MainGameManager] Client: Timeout waiting for player {player} to spawn");
    }
    
    private IEnumerator SpawnAllPlayersAsync(NetworkRunner runner)
    {
        if (!runner.IsServer) yield break;
        
        int spawnIndex = 0;
        foreach (var player in runner.ActivePlayers)
        {
            // 플레이어가 이미 스폰되었을 수 있으므로 검사
            if (!_spawnedPlayers.ContainsKey(player))
            {
                yield return SpawnPlayerAsync(runner, player, spawnIndex);
            }
            spawnIndex++;
        }
    }
    
    /// <summary>
    /// ScenSpawner에서 스폰 위치를 가져옵니다. 없으면 기본 위치 사용.
    /// </summary>
    private Vector3 GetSceneSpawnPosition(int index)
    {
        if (ScenSpawner.Instance != null)
        {
            Vector3 pos = ScenSpawner.Instance.GetSpawnPosition(index);
            return pos;
        }
        
        Vector3 defaultPos = _spawnPositions[index % _spawnPositions.Length];
        return defaultPos;
    }
    
    private IEnumerator SpawnPlayerAsync(NetworkRunner runner, PlayerRef player, int spawnIndex)
    {
        if (_spawnedPlayers.ContainsKey(player)) yield break;
        
        Vector2 spawnPosition = GetSceneSpawnPosition(spawnIndex);
        
        yield return null;

        NetworkObject playerObject = runner.Spawn(
            _playerPrefab, 
            spawnPosition, 
            Quaternion.identity, 
            player,
            (runner, obj) => 
            {
                Debug.Log($"[MainGameManager] OnBeforeSpawned for player {player}");
            }
        );
        
        if (playerObject != null)
        {
            _spawnedPlayers[player] = playerObject;
            
            if (playerObject.TryGetComponent(out PlayerController playerController))
            {
                // PlayerData에서 캐릭터 인덱스 가져오기 및 설정
                PlayerData playerData = GameManager.Instance?.GetPlayerData(player, runner);
                if (playerData != null)
                {
                    playerController.SetCharacterIndex(playerData.CharacterIndex);
                    // PlayerData에 실제 플레이어 오브젝트 연결 (서버만)
                    if (runner.IsServer)
                    {
                        playerData.PlayerInstance = playerObject;
                    }
                }
                
                // PlayerState 이벤트 구독 (UI 업데이트를 위해)
                if (playerController.State != null)
                {
                    playerController.State.OnHealthChanged += (current, max) => OnPlayerHealthChanged(player, current, max);
                    playerController.State.OnDeath += (killer) => OnPlayerDied(player, killer);
                    playerController.State.OnRespawned += () => OnPlayerRespawned(player);
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

    // =========================================================
    // 공용 메서드 및 이벤트 핸들러
    // =========================================================

    /// <summary>
    /// 맵 문 트리거에 모든 플레이어가 들어왔는지 체크하고, 들어왔다면 문을 닫습니다.
    /// </summary>
    private void UpdateMapDoorState()
    {
        // 문이 이미 닫혔거나, 문/트리거가 설정되지 않았다면 처리하지 않음
        if (_isMapDoorClosed || _mapDoorObject == null || _mapDoorTriggerColider == null)
            return;

        // 현재 씬에 존재하는 모든 플레이어 가져오기
        List<PlayerController> players = GetAllPlayers();
        if (players == null || players.Count == 0)
            return;

        // 한 명이라도 트리거 영역 밖이면 아직 닫지 않음
        foreach (var player in players)
        {
            if (player == null || player.IsDead) continue;

            Vector2 pos = player.transform.position;
            // Collider2D의 OverlapPoint를 사용해 포인트가 트리거 안에 있는지 검사
            if (!_mapDoorTriggerColider.OverlapPoint(pos))
            {
                return; // 아직 모두 들어오지 않음
            }
        }

        // 여기까지 왔으면 "모든 살아있는 플레이어"가 트리거 안에 있음 → 문 닫기
        CloseMapDoor();
    }

    /// <summary>
    /// 맵 문을 닫습니다. (애니메이션/상태 변경은 이 메서드 안에서 처리)
    /// </summary>
    private void CloseMapDoor()
    {
        if (_isMapDoorClosed) return;

        _isMapDoorClosed = true;

        if (_mapDoorObject != null)
        {
            // 기본 구현: 문 오브젝트 비활성화 (필요 시 애니메이션으로 교체 가능)
            _mapDoorObject.SetActive(true);
        }

        Debug.Log("[MainGameManager] All players entered map door trigger. Door closed.");
    }
    
    // 플레이어 제거 처리
    public void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        Debug.Log($"[MainGameManager] OnPlayerLeft - Player {player}, IsServer: {runner.IsServer}");
        
        // 이미 처리된 플레이어인지 확인 (중복 호출 방지)
        if (!_spawnedPlayers.ContainsKey(player))
        {
            Debug.Log($"[MainGameManager] Player {player} already processed, skipping duplicate call");
            return;
        }
        
        // PlayerData 가져오기 및 제거 (서버만) - 먼저 처리
        if (runner.IsServer)
        {
            PlayerData playerData = GameManager.Instance?.GetPlayerData(player, runner);
            if (playerData != null && playerData.Object != null)
            {
                Debug.Log($"[MainGameManager] Despawning PlayerData for player {player}");
                runner.Despawn(playerData.Object);
            }
            else
            {
                Debug.LogWarning($"[MainGameManager] PlayerData not found or already despawned for player {player}");
            }
        }
        
        // PlayerController 제거
        if (_spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
        {
            // 플레이어 오브젝트 제거 (서버만)
            if (playerObject != null && runner.IsServer)
            {
                Debug.Log($"[MainGameManager] Despawning PlayerController for player {player}");
                runner.Despawn(playerObject);
            }
            _spawnedPlayers.Remove(player);
        }
        else
        {
            Debug.LogWarning($"[MainGameManager] PlayerController not found in _spawnedPlayers for player {player}");
        }
        
        // 게임 중 플레이어가 나가면 경고창만 표시 (게임은 계속 진행)
        if (!_isTestMode && SceneManager.GetActiveScene().name == "Main")
        {
            int remainingPlayers = runner.ActivePlayers.Count();
            Debug.Log($"[MainGameManager] Remaining players: {remainingPlayers}");
            if (remainingPlayers < 2)
            {
                GameManager.Instance?.ShowWarningPanel("상대방이 나갔습니다.");
            }
        }
    }
    

    /// <summary>
    /// 로컬 플레이어의 PlayerController를 가져옵니다.
    /// </summary>
    public PlayerController GetLocalPlayer()
    {
        if (_isTestMode)
        {
            return GetSelectedPlayer();
        }
        
        NetworkRunner runner = _runner ?? FusionManager.LocalRunner ?? FindObjectOfType<NetworkRunner>();
        
        if (runner == null)
        {
            return null;
        }

        var localPlayerRef = runner.LocalPlayer;
        
        if (_spawnedPlayers.TryGetValue(localPlayerRef, out var playerObj))
        {
            return playerObj?.GetComponent<PlayerController>();
        }

        // 플레이어를 찾지 못한 경우 한 번 더 시도 (클라이언트 초기화 타이밍 문제 대응)
        FindAndRegisterSpawnedPlayers(runner);
        
        if (_spawnedPlayers.TryGetValue(localPlayerRef, out playerObj))
        {
            // NetworkObject가 파괴되었는지 확인
            if (playerObj == null || !playerObj.IsValid)
            {
                return null;
            }
            
            return playerObj.GetComponent<PlayerController>();
        }

        return null;
    }
    
    /// <summary>
    /// [테스트 모드 전용] 현재 선택된 플레이어의 PlayerController를 가져옵니다.
    /// </summary>
    public PlayerController GetSelectedPlayer()
    {
        var playerObj = SelectedSlot == 0 ? _playerObj1 : _playerObj2;
        
        // NetworkObject가 파괴되었는지 확인
        if (playerObj == null || !playerObj.IsValid)
        {
            return null;
        }
        
        return playerObj.GetComponent<PlayerController>();
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
        // 테스트 모드: 로컬에서 생성한 두 플레이어를 모두 반환
        if (_isTestMode)
        {
            var result = new List<PlayerController>();
            if (_playerObj1 != null && _playerObj1.IsValid)
            {
                var c1 = _playerObj1.GetComponent<PlayerController>();
                if (c1 != null) result.Add(c1);
            }
            if (_playerObj2 != null && _playerObj2.IsValid)
            {
                var c2 = _playerObj2.GetComponent<PlayerController>();
                if (c2 != null) result.Add(c2);
            }
            return result;
        }

        // 네트워크 모드: 서버/클라이언트가 등록한 맵을 기준으로 반환
        return _spawnedPlayers.Values
            .Select(obj => obj != null ? obj.GetComponent<PlayerController>() : null)
            .Where(controller => controller != null)
            .ToList();
    }

    #region Event Handlers
    private void OnPlayerHealthChanged(PlayerRef player, float current, float max)
    {
        // UI 업데이트 등 추가 로직 (MainCanvas가 PlayerState 이벤트를 구독하고 있으므로 여기서는 선택적)
    }

    private void OnPlayerDied(PlayerRef player, PlayerRef killer)
    {
        // 추가 게임 로직 (점수, 통계 등)
    }

    private void OnPlayerRespawned(PlayerRef player)
    {
        // 추가 리스폰 로직
    }

    /// <summary>
    /// 네트워크 세션이 종료되었을 때 호출됩니다 (호스트가 나가거나 세션이 종료됨)
    /// </summary>
    private void OnNetworkShutdown(NetworkRunner runner)
    {
        Debug.Log("[MainGameManager] Network shutdown detected, returning to title...");
        
        // 테스트 모드가 아닐 때만 타이틀로 돌아감
        if (!_isTestMode)
        {
            GameManager.Instance?.ShowWarningPanel("호스트가 나갔습니다. 타이틀로 돌아갑니다.");
            StartCoroutine(ReturnToTitleAfterDelay());
        }
    }

    /// <summary>
    /// 서버와의 연결이 끊어졌을 때 호출됩니다 (클라이언트 관점)
    /// FusionManager.OnDisconnectedEvent를 통해 호출됩니다.
    /// </summary>
    private void OnDisconnected(NetworkRunner runner)
    {
        Debug.Log("[MainGameManager] Disconnected from server, returning to title...");
        
        // 테스트 모드가 아닐 때만 타이틀로 돌아감
        if (!_isTestMode)
        {
            GameManager.Instance?.ShowWarningPanel("서버와의 연결이 끊어졌습니다. 타이틀로 돌아갑니다.");
            StartCoroutine(ReturnToTitleAfterDelay());
        }
    }

    /// <summary>
    /// 일정 시간 후 타이틀 씬으로 돌아갑니다.
    /// </summary>
    private IEnumerator ReturnToTitleAfterDelay()
    {
        yield return new WaitForSeconds(2f);
        
        // NetworkRunner가 있으면 정리
        if (_runner != null)
        {
            _runner.Shutdown();
        }
        
        // 타이틀 씬으로 이동
        UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
    }
    #endregion
}
