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
    // =========================================================
    // 테스트 모드 로직
    // =========================================================

    private async Task StartTestSession()
    {
        if (FusionManager.Instance == null) { Debug.LogError("[MainGameManager] FusionManager not found!"); return; }

        // [테스트 모드 전용] Runner 생성 또는 가져오기
        // 일반 네트워크 모드에서는 타이틀 씬에서 생성된 Runner를 사용하지만,
        // 테스트 모드에서는 직접 생성할 수 있습니다.
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

        // Runner 시작 (Host 모드)
        if (_runner != null && !_runner.IsRunning)
        {
            _runner.ProvideInput = true;
            _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            _runner.gameObject.AddComponent<NetworkObjectProviderDefault>();

            var physicsSimulator = _runner.gameObject.GetComponent<Fusion.Addons.Physics.RunnerSimulatePhysics2D>();
            if (physicsSimulator == null)
            {
                physicsSimulator = _runner.gameObject.AddComponent<Fusion.Addons.Physics.RunnerSimulatePhysics2D>();
            }

            // Client Physics Simulation을 SimulateAlways로 설정 (가장 부드러운 움직임)
            physicsSimulator.ClientPhysicsSimulation = Fusion.Addons.Physics.ClientPhysicsSimulation.SimulateAlways;

            await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Host,
                SessionName = "EditorTestSession",
                SceneManager = _runner.GetComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = _runner.GetComponent<NetworkObjectProviderDefault>()
            });

            FusionManager.LocalRunner = _runner;
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

        // 테스트 모드에서는 모든 플레이어가 localPlayer를 InputAuthority로 사용
        var localPlayer = _runner.LocalPlayer;

        // 가상 PlayerRef 생성 (PlayerData 식별용)
        PlayerRef player1Ref = PlayerRef.FromIndex(0);
        PlayerRef player2Ref = PlayerRef.FromIndex(1);

        Vector3 spawnPos0 = GetSceneSpawnPosition(0);
        Vector3 spawnPos1 = GetSceneSpawnPosition(1);

        // PlayerData 먼저 생성 (실제 멀티 환경과 동일한 순서)
        NetworkObject playerData1 = null;
        NetworkObject playerData2 = null;

        if (FusionManager.Instance != null && FusionManager.Instance.PlayerDataPrefab != null)
        {
            playerData1 = _runner.Spawn(FusionManager.Instance.PlayerDataPrefab, Vector3.zero, Quaternion.identity, player1Ref, (runner, obj) =>
            {
                if (obj.TryGetComponent(out PlayerData pd))
                {
                    pd.CharacterIndex = _firstCharacterIndex;
                }
            });

            playerData2 = _runner.Spawn(FusionManager.Instance.PlayerDataPrefab, Vector3.zero, Quaternion.identity, player2Ref, (runner, obj) =>
            {
                if (obj.TryGetComponent(out PlayerData pd))
                {
                    pd.CharacterIndex = _secondCharacterIndex;
                }
            });
        }

        // Player 1 스폰 (InputAuthority는 localPlayer)
        _playerObj1 = _runner.Spawn(_playerPrefab, spawnPos0, Quaternion.identity, localPlayer, (runner, obj) =>
        {
            var controller = obj.GetComponent<PlayerController>();
            controller.SetCharacterIndex(_firstCharacterIndex);
            controller.PlayerSlot = 0;
        });

        // Player 2 스폰 (InputAuthority는 localPlayer)
        _playerObj2 = _runner.Spawn(_playerPrefab, spawnPos1, Quaternion.identity, localPlayer, (runner, obj) =>
        {
            var controller = obj.GetComponent<PlayerController>();
            controller.SetCharacterIndex(_secondCharacterIndex);
            controller.PlayerSlot = 1;
        });

        if (playerData1 != null && playerData1.TryGetComponent(out PlayerData pd1))
        {
            pd1.PlayerInstance = _playerObj1;
        }

        if (playerData2 != null && playerData2.TryGetComponent(out PlayerData pd2))
        {
            pd2.PlayerInstance = _playerObj2;
        }

        // 첫 번째 플레이어를 딕셔너리에 추가
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
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SelectedSlot = 0;
            UpdateCanvasForSelectedPlayer();
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SelectedSlot = 1;
            UpdateCanvasForSelectedPlayer();
        }

        // T/Y/U 키로 데미지/힐 테스트
        if (Input.GetKeyDown(KeyCode.T)) ApplyTestHealthChange(-1f, "Damage");
        if (Input.GetKeyDown(KeyCode.Y)) ApplyTestHealthChange(1f, "Heal");
        if (Input.GetKeyDown(KeyCode.U)) ApplyTestHealthChange(999f, "Full Heal");
    }

    /// <summary>
    /// 테스트 모드에서 선택된 플레이어에 맞춰 Canvas를 업데이트합니다.
    /// </summary>
    private void UpdateCanvasForSelectedPlayer()
    {
        if (!_isTestMode) return;

        PlayerController selectedPlayer = GetSelectedPlayer();
        if (selectedPlayer != null && GameManager.Instance?.Canvas is MainCanvas canvas)
        {
            canvas.RegisterPlayer(selectedPlayer);
        }
    }

    private void ApplyTestHealthChange(float amount, string type)
    {
        var player = SelectedSlot == 0 ? _playerObj1 : _playerObj2;
        if (player != null && player.TryGetComponent(out PlayerController controller) && controller.State != null)
        {
            if (type == "Damage") controller.State.TakeDamage(amount);
            else if (type == "Heal") controller.State.Heal(amount);
            else if (type == "Full Heal") controller.State.SetHealth(controller.State.MaxHealth);
        }
    }
}


