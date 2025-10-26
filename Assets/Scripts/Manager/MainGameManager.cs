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
public class MainGameManager : MonoBehaviour
{
    [Header("Mode & Settings")]
    [Tooltip("체크 시, 로컬에서 여러 플레이어를 스폰하고 1/2 키로 조작 대상을 전환합니다.")]
    [SerializeField] private bool _isTestMode = false;
    
    [Header("Player Prefabs & Data")]
    [SerializeField] private NetworkPrefabRef PlayerPrefab;
    [SerializeField] private int firstCharacterIndex = 0;
    [SerializeField] private int secondCharacterIndex = 1;
    
    [Header("Spawn Settings")]
    [SerializeField] private Vector2[] spawnPositions = new Vector2[]
    {
        new Vector2(0, 2),
        new Vector2(0, -2)
    };
    
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
    }
    
    // =========================================================
    // 테스트 모드 로직
    // =========================================================

    private async Task StartTestSession()
    {
        if (FusionManager.Instance == null) { Debug.LogError("[MainGameManager] FusionManager not found!"); return; }
        
        // Runner 생성 또는 가져오기
        if (FusionManager.LocalRunner == null)
        {
            GameObject go = new GameObject("TestRunner");
            _runner = go.AddComponent<NetworkRunner>();
            _runner.AddCallbacks(FusionManager.Instance);
        }
        else
        {
            _runner = FusionManager.LocalRunner;
        }

        // Runner 시작 (Host 모드)
        if (_runner != null && !_runner.IsRunning)
        {
            _runner.ProvideInput = true;
            _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            _runner.gameObject.AddComponent<NetworkObjectProviderDefault>();
            
            await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Host,
                SessionName = "LocalTestSession",
                SceneManager = _runner.GetComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = _runner.GetComponent<NetworkObjectProviderDefault>()
            });

            FusionManager.LocalRunner = _runner;
            Debug.Log("[MainGameManager] Test session started successfully.");
        }
        
        // 플레이어 스폰
        if (_runner.IsServer)
        {
            // 씬 로드 완료까지 대기 (MainCanvas 초기화 등)
            await Task.Delay(500); 
            SpawnTestPlayers();
        }
        
        GameManager.Instance?.FinishLoadingScreen();
    }
    
    private void SpawnTestPlayers()
    {
        if (_runner == null || !_runner.IsServer) return;
        
        var localPlayer = _runner.LocalPlayer;
        
        Vector3 spawnPos0 = GetSceneSpawnPosition(0);
        Vector3 spawnPos1 = GetSceneSpawnPosition(1);
        
        _playerObj1 = _runner.Spawn(PlayerPrefab, spawnPos0, Quaternion.identity, localPlayer, (runner, obj) => 
        {
            var controller = obj.GetComponent<PlayerController>();
            controller.SetCharacterIndex(firstCharacterIndex);
            controller.PlayerSlot = 0;
        });
        
        _playerObj2 = _runner.Spawn(PlayerPrefab, spawnPos1, Quaternion.identity, localPlayer, (runner, obj) => 
        {
            var controller = obj.GetComponent<PlayerController>();
            controller.SetCharacterIndex(secondCharacterIndex);
            controller.PlayerSlot = 1;
        });
        
        if (_playerObj1 != null) _spawnedPlayers[localPlayer] = _playerObj1; 

        if (GameManager.Instance?.Canvas is MainCanvas canvas)
        {
            canvas.RegisterPlayer(_playerObj1.GetComponent<PlayerController>());
        }
        
        InitializeMainCamera();
    }
    
    private void InitializeMainCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            MainCameraController cameraController = mainCamera.GetComponent<MainCameraController>();
            if (cameraController != null)
            {
                cameraController.SetTarget(_playerObj1.GetComponent<PlayerController>());
            }
        }
    }
    
    private void HandleTestModeInput()
    {
        // 1/2 키로 조작 대상 전환
        if (Input.GetKeyDown(KeyCode.Alpha1)) { SelectedSlot = 0; Debug.Log($"Switched to Player 1 (Slot: {SelectedSlot})"); }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { SelectedSlot = 1; Debug.Log($"Switched to Player 2 (Slot: {SelectedSlot})"); }

        // T/Y/U 키로 데미지/힐 테스트
        if (Input.GetKeyDown(KeyCode.T)) ApplyTestHealthChange(-1f, "Damage");
        if (Input.GetKeyDown(KeyCode.Y)) ApplyTestHealthChange(1f, "Heal");
        if (Input.GetKeyDown(KeyCode.U)) ApplyTestHealthChange(999f, "Full Heal");
    }

    private void ApplyTestHealthChange(float amount, string type)
    {
        var player = SelectedSlot == 0 ? _playerObj1 : _playerObj2;
        if (player != null && player.TryGetComponent(out PlayerController controller) && controller.State != null)
        {
            if (type == "Damage") controller.State.TakeDamage(amount);
            else if (type == "Heal") controller.State.Heal(amount);
            else if (type == "Full Heal") controller.State.FullHeal();
            
            Debug.Log($"[TestMode] {type} applied to Player {SelectedSlot + 1}");
        }
    }

    // =========================================================
    // 네트워크 모드 로직
    // =========================================================

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
            yield return new WaitForSeconds(1.0f);
        }
        
        InitializeMainCameraForNetworkMode();
        GameManager.Instance?.FinishLoadingScreen();
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
    
    // 게임 중 플레이어 참여 처리 (서버만 스폰)
    private void OnPlayerJoinedDuringGame(PlayerRef player, NetworkRunner runner)
    {
        if (SceneManager.GetActiveScene().name == "Main")
        {
            if (runner.IsServer && !_spawnedPlayers.ContainsKey(player))
            {
                int spawnIndex = _spawnedPlayers.Count;
                StartCoroutine(SpawnPlayerAsync(runner, player, spawnIndex));
            }
        }
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
            Debug.Log($"[MainGameManager] Using ScenSpawner position [{index}]: {pos}");
            return pos;
        }
        
        Vector3 defaultPos = spawnPositions[index % spawnPositions.Length];
        Debug.Log($"[MainGameManager] ScenSpawner not found, using default position [{index}]: {defaultPos}");
        return defaultPos;
    }
    
    private IEnumerator SpawnPlayerAsync(NetworkRunner runner, PlayerRef player, int spawnIndex)
    {
        if (_spawnedPlayers.ContainsKey(player)) yield break;
        
        Vector2 spawnPosition = GetSceneSpawnPosition(spawnIndex);
        
        yield return null;

        NetworkObject playerObject = runner.Spawn(
            PlayerPrefab, 
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
    
    // 플레이어 제거 처리
    public void OnPlayerLeft(PlayerRef player, NetworkRunner runner)
    {
        if (_spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
        {
            // PlayerData 정리 (서버만)
            if (runner.IsServer)
            {
                PlayerData playerData = GameManager.Instance?.GetPlayerData(player, runner);
                if (playerData != null)
                {
                    playerData.PlayerInstance = null;
                }
            }
            
            // 플레이어 오브젝트 제거 (서버만)
            if (playerObject != null && runner.IsServer)
            {
                runner.Despawn(playerObject);
            }
            _spawnedPlayers.Remove(player);
            
            // 게임 중 플레이어가 나가면 Title 씬으로 복귀 로직 (서버만 실행)
            if (!_isTestMode && SceneManager.GetActiveScene().name == "Main")
            {
                int remainingPlayers = runner.ActivePlayers.Count();
                if (remainingPlayers < 2)
                {
                    GameManager.Instance?.ShowWarningPanel("상대방이 나갔습니다.");
                    if (runner.IsServer)
                    {
                        StartCoroutine(ReturnToTitleSceneDelayed(runner));
                    }
                }
            }
        }
    }
    
    private IEnumerator ReturnToTitleSceneDelayed(NetworkRunner runner)
    {
        yield return new WaitForSeconds(2f);
        
        if (runner != null && runner.IsServer)
        {
            runner.LoadScene("Title");
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
        
        if (runner == null) return null;

        var localPlayerRef = runner.LocalPlayer;
        if (_spawnedPlayers.TryGetValue(localPlayerRef, out var playerObj))
        {
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
        return playerObj?.GetComponent<PlayerController>();
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
        return _spawnedPlayers.Values
            .Select(obj => obj.GetComponent<PlayerController>())
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
    #endregion
}
