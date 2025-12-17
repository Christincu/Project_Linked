using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;

/// <summary>
/// 메인 게임 매니저 - 테스트 모드 관련 partial 클래스
/// </summary>
public partial class MainGameManager
{
    // [설정] 테스트 캐릭터 인덱스 (Inspector에서 설정 가능하도록 필드 확보)
    [Header("Test Mode Settings")]
    [SerializeField] private int _firstCharacterIndex = 0;
    [SerializeField] private int _secondCharacterIndex = 1;

    private async void StartTestSession()
    {
        Debug.Log("[MainGameManager] Initializing Test Session...");

        // 1. Singleton Validation
        if (FusionManager.Instance == null) 
        { 
            Debug.LogError("[MainGameManager] FusionManager not found! Cannot start session."); 
            return; 
        }

        // 2. Runner Instantiation & Persistence
        if (FusionManager.LocalRunner == null)
        {
            GameObject go = new GameObject("TestRunner");
            _runner = go.AddComponent<NetworkRunner>();
            
            // CRITICAL FIX: Prevent "Runner Suicide".
            // Moves the Runner to the DontDestroyOnLoad scene so it survives the scene reload.
            DontDestroyOnLoad(go);

            _runner.AddCallbacks(FusionManager.Instance);
            
            // [Fix] Optimistically assign LocalRunner so the new MainGameManager (spawned after reload)
            // sees that a runner is already being setup and skips its own StartTestSession logic.
            FusionManager.LocalRunner = _runner;
        }
        else
        {
            _runner = FusionManager.LocalRunner;
        }

        // 3. Runner Configuration
        if (_runner != null && !_runner.IsRunning)
        {
            _runner.ProvideInput = true;
            
            // Component Verification
            if (_runner.GetComponent<NetworkSceneManagerDefault>() == null)
                _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            if (_runner.GetComponent<NetworkObjectProviderDefault>() == null)
                _runner.gameObject.AddComponent<NetworkObjectProviderDefault>();

            // Physics Setup
            var physicsSimulator = _runner.gameObject.GetComponent<Fusion.Addons.Physics.RunnerSimulatePhysics2D>();
            if (physicsSimulator == null)
            {
                physicsSimulator = _runner.gameObject.AddComponent<Fusion.Addons.Physics.RunnerSimulatePhysics2D>();
                physicsSimulator.ClientPhysicsSimulation = Fusion.Addons.Physics.ClientPhysicsSimulation.SimulateAlways;
            }

            // 4. Scene Index Validation
            // CRITICAL FIX: Check if the scene index is valid (-1 means not in Build Settings).
            int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
            if (currentSceneIndex == -1)
            {
                Debug.LogError("[MainGameManager] Critical Error: The active scene is not added to the Build Settings.");
                Debug.LogError("Please go to File > Build Settings and add the current open scene.");
                // Revert optimistic assignment if we fail here
                if (FusionManager.LocalRunner == _runner) FusionManager.LocalRunner = null;
                return; 
            }

            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(SceneRef.FromIndex(currentSceneIndex), LoadSceneMode.Single);

            Debug.Log($"[MainGameManager] Runner created. Starting GameMode.Single on Scene Index {currentSceneIndex}...");

            // 5. Asynchronous Execution with Exception Handling
            try 
            {
                // The await here will now work because the runner persists in DDOL.
                var result = await _runner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Single,
                    SessionName = "EditorTestSession",
                    Scene = sceneInfo,
                    SceneManager = _runner.GetComponent<NetworkSceneManagerDefault>(),
                    ObjectProvider = _runner.GetComponent<NetworkObjectProviderDefault>()
                });

                if (result.Ok)
                {
                    // FusionManager.LocalRunner = _runner; // Already set above
                    Debug.Log("[MainGameManager] Test session started successfully (Task Completed).");
                }
                else
                {
                    Debug.LogError($"[MainGameManager] Failed to start Test Session: {result.ShutdownReason}");
                    
                    // Cleanup failed runner
                    if (FusionManager.LocalRunner == _runner) FusionManager.LocalRunner = null;
                    if (_runner != null) Destroy(_runner.gameObject);
                    return;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MainGameManager] Exception during StartGame: {ex.Message}\n{ex.StackTrace}");
                 // Cleanup failed runner
                if (FusionManager.LocalRunner == _runner) FusionManager.LocalRunner = null;
                if (_runner != null) Destroy(_runner.gameObject);
                return;
            }
        }
        else if (_runner.IsRunning)
        {
             Debug.LogWarning("[MainGameManager] Runner is already running. Ignoring StartTestSession call.");
        }

        // 6. UI Finalization
        if (_gameManager != null) 
        {
            _gameManager.FinishLoadingScreen();
        }
    }

    private void SpawnTestPlayers()
    {
        var runner = FusionManager.LocalRunner;
        
        if (runner == null || !runner.IsRunning || !runner.IsServer)
        {
            Debug.LogWarning($"[MainGameManager] Cannot spawn test players: Runner={runner != null}, IsRunning={runner?.IsRunning}, IsServer={runner?.IsServer}");
            return;
        }

        var localPlayer = runner.LocalPlayer;
        Vector3 spawnPos0 = GetSceneSpawnPosition(0);
        Vector3 spawnPos1 = GetSceneSpawnPosition(1);

        NetworkObject pdObj1 = null;
        NetworkObject pdObj2 = null;

        // [Fix] Check if runner is valid before proceeding
        if (runner == null || !runner.IsRunning || runner.IsShutdown) return;

        var existingData = FindObjectsOfType<PlayerData>();
        if (existingData != null)
        {
            foreach (var pd in existingData)
            {
                if (pd == null) continue;
                if (pd.CharacterIndex == _firstCharacterIndex) pdObj1 = pd.Object;
                if (pd.CharacterIndex == _secondCharacterIndex) pdObj2 = pd.Object;
            }
        }

        if (pdObj1 == null)
            pdObj1 = runner.Spawn(FusionManager.Instance.PlayerDataPrefab, Vector3.zero, Quaternion.identity, localPlayer,
                (r, obj) => SetupPlayerData(obj, _firstCharacterIndex, "TestPlayer 1"));

        if (pdObj2 == null)
            pdObj2 = runner.Spawn(FusionManager.Instance.PlayerDataPrefab, Vector3.zero, Quaternion.identity, localPlayer,
                (r, obj) => SetupPlayerData(obj, _secondCharacterIndex, "TestPlayer 2"));

        _testPlayerObj1 = runner.Spawn(_playerPrefab, spawnPos0, Quaternion.identity, localPlayer, (r, obj) =>
        {
            var controller = obj.GetComponent<PlayerController>();
            if (controller != null)
            {
                controller.SetCharacterIndex(_firstCharacterIndex);
                controller.PlayerSlot = 0;
            }
        });

        _testPlayerObj2 = runner.Spawn(_playerPrefab, spawnPos1, Quaternion.identity, localPlayer, (r, obj) =>
        {
            var controller = obj.GetComponent<PlayerController>();
            if (controller != null)
            {
                controller.SetCharacterIndex(_secondCharacterIndex);
                controller.PlayerSlot = 1;
            }
        });
        
        if (pdObj1 != null && _testPlayerObj1 != null && pdObj1.TryGetComponent(out PlayerData data1)) data1.Instance = _testPlayerObj1;
        if (pdObj2 != null && _testPlayerObj2 != null && pdObj2.TryGetComponent(out PlayerData data2)) data2.Instance = _testPlayerObj2;

        // [Fix] Must register the local player object so WaitForLocalPlayerObject doesn't hang
        if (_testPlayerObj1 != null)
        {
            _spawnedPlayers[localPlayer] = _testPlayerObj1;
            runner.SetPlayerObject(localPlayer, _testPlayerObj1);
        }

        SelectedSlot = 0;
        UpdateCanvasForSelectedPlayer();
    }

    // PlayerData 초기화 헬퍼
    private void SetupPlayerData(NetworkObject obj, int charIndex, string nick)
    {
        if (obj.TryGetComponent(out PlayerData pd))
        {
            pd.CharacterIndex = charIndex;
            pd.Nick = nick;
        }
    }

    private void HandleTestModeInput()
    {
        // F1: 플레이어 1 선택
        if (Input.GetKeyDown(KeyCode.F1))
        {
            if (SelectedSlot != 0)
            {
                SelectedSlot = 0;
                UpdateCanvasForSelectedPlayer();
            }
        }
        // F2: 플레이어 2 선택
        if (Input.GetKeyDown(KeyCode.F2))
        {
            if (SelectedSlot != 1)
            {
                SelectedSlot = 1;
                UpdateCanvasForSelectedPlayer();
            }
        }

        // 테스트 치트키
        if (Input.GetKeyDown(KeyCode.T)) ApplyTestHealthChange(-10f, "Damage");
        if (Input.GetKeyDown(KeyCode.Y)) ApplyTestHealthChange(10f, "Heal");
        if (Input.GetKeyDown(KeyCode.U)) ApplyTestHealthChange(9999f, "Full Heal");
    }

    private void UpdateCanvasForSelectedPlayer()
    {
        if (!_isTestMode) return;

        // 선택된 플레이어 객체 가져오기
        NetworkObject targetObj = SelectedSlot == 0 ? _testPlayerObj1 : _testPlayerObj2;

        if (targetObj == null || !targetObj.IsValid) return;

        var controller = targetObj.GetComponent<PlayerController>();
        if (controller == null) return;

        // UI 연결
        if (GameManager.Instance != null && GameManager.Instance.Canvas is MainCanvas canvas)
        {
            canvas.RegisterPlayer(controller);
        }

        // 카메라 연결
        if (Camera.main != null && Camera.main.TryGetComponent<MainCameraController>(out var camController))
        {
            camController.SetTarget(controller);
        }
    }

    private void ApplyTestHealthChange(float amount, string type)
    {
        var targetObj = SelectedSlot == 0 ? _testPlayerObj1 : _testPlayerObj2;

        if (targetObj != null && targetObj.TryGetComponent(out PlayerController controller))
        {
            Debug.Log($"[TestMode] {type} : {amount}");

            if (amount < 0)
                controller.TakeDamage(Mathf.Abs(amount));
            else if (amount >= 9999f)
                controller.SetHealth(controller.MaxHealth);
            else
                controller.Heal(amount);
        }
    }

    /// <summary>
    /// 테스트 모드에서 현재 선택된 플레이어를 반환합니다.
    /// </summary>
    public PlayerController GetSelectedPlayer()
    {
        if (!_isTestMode) return null;
        var target = SelectedSlot == 0 ? _testPlayerObj1 : _testPlayerObj2;
        return (target != null && target.IsValid) ? target.GetComponent<PlayerController>() : null;
    }
}