using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;

public class TestGameManager : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private NetworkPrefabRef PlayerPrefab;

    [Header("Spawn Settings")] 
    [SerializeField] private Vector2 firstPlayerPos = new Vector2(-2, 0);
    [SerializeField] private Vector2 secondPlayerPos = new Vector2(2, 0);
    [SerializeField] private int firstCharacterIndex = 0;
    [SerializeField] private int secondCharacterIndex = 0;

    private NetworkRunner _runner;
    public static int SelectedSlot = 0; // 0: first, 1: second
    private NetworkObject _playerObj1;
    private NetworkObject _playerObj2;

    // Singleton
    public static TestGameManager Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    async void Start()
    {
        // Check if managers exist in scene
        if (FusionManager.Instance == null)
        {
            Debug.LogError("[TestGameManager] FusionManager not found in scene!");
            return;
        }
        
        if (GameManager.Instance == null)
        {
            Debug.LogError("[TestGameManager] GameManager not found in scene!");
            return;
        }
        
        if (GameDataManager.Instance == null)
        {
            Debug.LogError("[TestGameManager] GameDataManager not found in scene!");
            return;
        }

        // Create runner if not exists (for standalone scene testing)
        if (FusionManager.LocalRunner == null)
        {
            GameObject go = new GameObject("TestRunner");
            _runner = go.AddComponent<NetworkRunner>();
            
            // Connect FusionManager callbacks
            _runner.AddCallbacks(FusionManager.Instance);
        }
        else
        {
            _runner = FusionManager.LocalRunner;
        }

        // Start runner as host if not running
        if (_runner != null && !_runner.IsRunning)
        {
            _runner.ProvideInput = true;

            var sceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

            await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Host,
                SessionName = "LocalTestSession",
                SceneManager = sceneManager
            });

            FusionManager.LocalRunner = _runner;
            Debug.Log("[TestGameManager] Test session started successfully");
        }

        // Spawn players on server only
        if (_runner.IsServer)
        {
            // PlayerData is auto-created in FusionManager.OnPlayerJoined
            // Wait a bit before spawning player objects
            await System.Threading.Tasks.Task.Delay(100);
            SpawnTestPlayers();
        }
    }

    private void SpawnTestPlayers()
    {
        var localPlayer = _runner.LocalPlayer;
        
        // InitialPlayerData 가져오기
        InitialPlayerData initialData = null;
        if (GameDataManager.Instance != null)
        {
            initialData = GameDataManager.Instance.InitialPlayerData;
        }

        // 1) Spawn first player
        _playerObj1 = _runner.Spawn(PlayerPrefab, firstPlayerPos, Quaternion.identity, localPlayer);
        var controller1 = _playerObj1 != null ? _playerObj1.GetComponent<PlayerController>() : null;
        if (controller1 != null)
        {
            controller1.SetCharacterIndex(firstCharacterIndex);
            controller1.PlayerSlot = 0;
            
            Debug.Log($"[TestGameManager] Player 1 spawned (Character: {firstCharacterIndex}, Slot: 0)");
        }

        // 2) Spawn second player (controlled by same local player)
        _playerObj2 = _runner.Spawn(PlayerPrefab, secondPlayerPos, Quaternion.identity, localPlayer);
        var controller2 = _playerObj2 != null ? _playerObj2.GetComponent<PlayerController>() : null;
        if (controller2 != null)
        {
            controller2.SetCharacterIndex(secondCharacterIndex);
            controller2.PlayerSlot = 1;
            
            Debug.Log($"[TestGameManager] Player 2 spawned (Character: {secondCharacterIndex}, Slot: 1)");
        }
    }

    void Update()
    {
        // Switch control target with 1/2 keys
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SelectedSlot = 0;
            Debug.Log($"[TestGameManager] Switched to Player 1 (Slot: {SelectedSlot})");
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SelectedSlot = 1;
            Debug.Log($"[TestGameManager] Switched to Player 2 (Slot: {SelectedSlot})");
        }

        // Test: T key for damage
        if (Input.GetKeyDown(KeyCode.T))
        {
            var player = SelectedSlot == 0 ? _playerObj1 : _playerObj2;
            if (player != null)
            {
                var state = player.GetComponent<PlayerState>();
                if (state != null)
                {
                    state.TakeDamage(1f);
                    Debug.Log($"[TestGameManager] Damage applied to Player {SelectedSlot + 1}");
                }
            }
        }

        // Test: Y key for heal
        if (Input.GetKeyDown(KeyCode.Y))
        {
            var player = SelectedSlot == 0 ? _playerObj1 : _playerObj2;
            if (player != null)
            {
                var state = player.GetComponent<PlayerState>();
                if (state != null)
                {
                    state.Heal(1f);
                    Debug.Log($"[TestGameManager] Heal applied to Player {SelectedSlot + 1}");
                }
            }
        }

        // Test: U key for full heal
        if (Input.GetKeyDown(KeyCode.U))
        {
            var player = SelectedSlot == 0 ? _playerObj1 : _playerObj2;
            if (player != null)
            {
                var state = player.GetComponent<PlayerState>();
                if (state != null)
                {
                    state.FullHeal();
                    Debug.Log($"[TestGameManager] Full heal applied to Player {SelectedSlot + 1}");
                }
            }
        }
    }

    #region Public Methods
    /// <summary>
    /// 선택된 플레이어의 PlayerController를 가져옵니다.
    /// </summary>
    public PlayerController GetSelectedPlayer()
    {
        var playerObj = SelectedSlot == 0 ? _playerObj1 : _playerObj2;
        return playerObj?.GetComponent<PlayerController>();
    }

    /// <summary>
    /// 특정 슬롯의 PlayerController를 가져옵니다.
    /// </summary>
    public PlayerController GetPlayer(int slot)
    {
        var playerObj = slot == 0 ? _playerObj1 : _playerObj2;
        return playerObj?.GetComponent<PlayerController>();
    }
    #endregion
}
