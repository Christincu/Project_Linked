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
    private void StartTestSession()
    {
        if (FusionManager.Instance == null) { Debug.LogError("[MainGameManager] FusionManager not found!"); return; }

        // [테스트 모드 전용] Runner 생성 또는 가져오기
        if (FusionManager.LocalRunner == null)
        {
            // 테스트 모드: Runner가 없으면 새로 생성
            GameObject go = new GameObject("TestRunner");
            _runner = go.AddComponent<NetworkRunner>();
            _runner.AddCallbacks(FusionManager.Instance);
        }
        else
        {
            // 기존 Runner 재사용 (타이틀에서 넘어온 경우)
            _runner = FusionManager.LocalRunner;
        }

        // Runner 시작 (Host/Single 모드)
        if (_runner != null && !_runner.IsRunning)
        {
            _runner.ProvideInput = true;
            
            // 필수 컴포넌트 확인 및 추가
            if (_runner.GetComponent<NetworkSceneManagerDefault>() == null)
                _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            
            if (_runner.GetComponent<NetworkObjectProviderDefault>() == null)
                _runner.gameObject.AddComponent<NetworkObjectProviderDefault>();

            // 물리 시뮬레이터 확인 및 추가
            var physicsSimulator = _runner.gameObject.GetComponent<Fusion.Addons.Physics.RunnerSimulatePhysics2D>();
            if (physicsSimulator == null)
            {
                physicsSimulator = _runner.gameObject.AddComponent<Fusion.Addons.Physics.RunnerSimulatePhysics2D>();
            }
            // Client Physics Simulation을 SimulateAlways로 설정 (가장 부드러운 움직임)
            physicsSimulator.ClientPhysicsSimulation = Fusion.Addons.Physics.ClientPhysicsSimulation.SimulateAlways;

            // Scene 정보 구성 (Fusion 2.0)
            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex), LoadSceneMode.Single);

            var result = _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single, // 테스트는 싱글 모드로 실행 (혼자서 2캐릭 조종)
                SessionName = "EditorTestSession",
                Scene = sceneInfo,
                SceneManager = _runner.GetComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = _runner.GetComponent<NetworkObjectProviderDefault>()
            });

            FusionManager.LocalRunner = _runner;
        }

        if (_runner.IsServer)
        {
            SpawnTestPlayers();
            StartCoroutine(Co_InitializeGameSession());
        }

        GameManager.Instance.FinishLoadingScreen();
    }

    private void SpawnTestPlayers()
    {
        if (_runner == null || !_runner.IsServer) return;

        // 테스트 모드에서는 모든 플레이어가 localPlayer를 InputAuthority로 사용
        var localPlayer = _runner.LocalPlayer;

        // 가상 PlayerRef 생성 (PlayerData 식별용)
        PlayerRef player1Ref = PlayerRef.FromIndex(0);
        PlayerRef player2Ref = PlayerRef.FromIndex(1);

        Vector3 spawnPos0 = GetSceneSpawnPosition(0);
        Vector3 spawnPos1 = GetSceneSpawnPosition(1);

        // PlayerData 먼저 생성 (이미 존재하는 경우 재사용)
        NetworkObject playerData1 = null;
        NetworkObject playerData2 = null;

        // [수정] 기존 PlayerData 확인 (씬 전환 후에도 유지되는 경우)
        PlayerData[] existingPlayerData = FindObjectsOfType<PlayerData>();
        foreach (var pd in existingPlayerData)
        {
            if (pd != null && pd.Object != null && pd.Object.IsValid)
            {
                // 첫 번째 플레이어 데이터 확인
                if (playerData1 == null && pd.CharacterIndex == _firstCharacterIndex)
                {
                    playerData1 = pd.Object;
                }
                // 두 번째 플레이어 데이터 확인
                else if (playerData2 == null && pd.CharacterIndex == _secondCharacterIndex)
                {
                    playerData2 = pd.Object;
                }
            }
        }

        // [수정] 없을 때만 새로 생성
        if (FusionManager.Instance != null && FusionManager.Instance.PlayerDataPrefab != null)
        {
            if (playerData1 == null)
            {
                playerData1 = _runner.Spawn(FusionManager.Instance.PlayerDataPrefab, Vector3.zero, Quaternion.identity, localPlayer, (runner, obj) =>
                {
                    if (obj.TryGetComponent(out PlayerData pd))
                    {
                        pd.CharacterIndex = _firstCharacterIndex;
                        pd.Nick = "TestPlayer 1";
                    }
                });
            }

            if (playerData2 == null)
            {
                playerData2 = _runner.Spawn(FusionManager.Instance.PlayerDataPrefab, Vector3.zero, Quaternion.identity, localPlayer, (runner, obj) =>
                {
                    if (obj.TryGetComponent(out PlayerData pd))
                    {
                        pd.CharacterIndex = _secondCharacterIndex;
                        pd.Nick = "TestPlayer 2";
                    }
                });
            }
        }

        // Player 1 스폰 (InputAuthority는 localPlayer)
        _playerObj1 = _runner.Spawn(_playerPrefab, spawnPos0, Quaternion.identity, localPlayer, (runner, obj) =>
        {
            var controller = obj.GetComponent<PlayerController>();
            if (controller != null)
            {
                controller.SetCharacterIndex(_firstCharacterIndex);
                controller.PlayerSlot = 0;
            }
        });

        // Player 2 스폰 (InputAuthority는 localPlayer)
        _playerObj2 = _runner.Spawn(_playerPrefab, spawnPos1, Quaternion.identity, localPlayer, (runner, obj) =>
        {
            var controller = obj.GetComponent<PlayerController>();
            if (controller != null)
            {
                controller.SetCharacterIndex(_secondCharacterIndex);
                controller.PlayerSlot = 1;
            }
        });

        // PlayerData와 Player 연결
        if (playerData1 != null && playerData1.TryGetComponent(out PlayerData pd1))
        {
            // [수정] Instance 필드에 직접 할당 (StateAuthority에서만 가능)
            if (pd1.Object != null && pd1.Object.HasStateAuthority)
            {
                pd1.Instance = _playerObj1;
                Debug.Log("[MainGameManager.TestMode] Assigned _playerObj1 to PlayerData.Instance");
            }
        }

        if (playerData2 != null && playerData2.TryGetComponent(out PlayerData pd2))
        {
            // [수정] Instance 필드에 직접 할당 (StateAuthority에서만 가능)
            if (pd2.Object != null && pd2.Object.HasStateAuthority)
            {
                pd2.Instance = _playerObj2;
                Debug.Log("[MainGameManager.TestMode] Assigned _playerObj2 to PlayerData.Instance");
            }
        }

        if (_playerObj1 != null) _spawnedPlayers[localPlayer] = _playerObj1;
        if (_playerObj1 != null)
        {
            _runner.SetPlayerObject(localPlayer, _playerObj1);
        }

        // UI에 첫 번째 플레이어 등록
        if (GameManager.Instance?.Canvas is MainCanvas canvas && _playerObj1 != null)
        {
            canvas.RegisterPlayer(_playerObj1.GetComponent<PlayerController>());
        }

        InitializeMainCamera();
        
        // 초기 선택 슬롯 설정
        SelectedSlot = 0;
        UpdateCanvasForSelectedPlayer();
    }

    private void InitializeMainCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null && _playerObj1 != null)
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
        if (Input.GetKeyDown(KeyCode.F1))
        {
            if (SelectedSlot != 0)
            {
                SelectedSlot = 0;
                UpdateCanvasForSelectedPlayer();
            }
        }
        if (Input.GetKeyDown(KeyCode.F2))
        {
            if (SelectedSlot != 1)
            {
                SelectedSlot = 1;
                UpdateCanvasForSelectedPlayer();
            }
        }

        if (Input.GetKeyDown(KeyCode.T)) ApplyTestHealthChange(-1f, "Damage");
        if (Input.GetKeyDown(KeyCode.Y)) ApplyTestHealthChange(1f, "Heal");
        if (Input.GetKeyDown(KeyCode.U)) ApplyTestHealthChange(999f, "Full Heal");
    }

    private void UpdateCanvasForSelectedPlayer()
    {
        if (!_isTestMode) return;

        NetworkObject targetObj = SelectedSlot == 0 ? _playerObj1 : _playerObj2;
        if (targetObj == null) return;

        var controller = targetObj.GetComponent<PlayerController>();
        if (controller == null) return;

        if (GameManager.Instance != null && GameManager.Instance.Canvas is MainCanvas canvas)
        {
            canvas.RegisterPlayer(controller);
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            var camController = mainCamera.GetComponent<MainCameraController>();
            if (camController != null)
            {
                camController.SetTarget(controller);
            }
        }
    }

    private void ApplyTestHealthChange(float amount, string type)
    {
        var player = SelectedSlot == 0 ? _playerObj1 : _playerObj2;
        if (player != null && player.TryGetComponent(out PlayerController controller))
        {
            if (type == "Damage") controller.TakeDamage(Mathf.Abs(amount));
            else if (type == "Heal") controller.Heal(amount);
            else if (type == "Full Heal") controller.SetHealth(controller.MaxHealth);
        }
    }
}