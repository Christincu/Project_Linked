using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;

public partial class MainGameManager
{
    [Header("References")]
    [SerializeField] private NetworkPrefabRef _playerPrefab;

    [Header("Settings")]
    [SerializeField] private float _timeoutPlayerSpawn = 20f;
    [SerializeField] private float _retryInterval = 0.1f;

    // 내부 상태 관리
    public static int SelectedSlot = 0; // 테스트 모드용
    private NetworkObject _testPlayerObj1;
    private NetworkObject _testPlayerObj2;

    // 플레이어 캐싱 (검색 비용 최적화)
    private Dictionary<PlayerRef, PlayerController> _playerCache = new Dictionary<PlayerRef, PlayerController>();

    /// <summary>
    /// 게임 세션 초기화 메인 루틴
    /// Runner가 준비된 상태에서 호출된다고 가정합니다 (Spawned 이후).
    /// </summary>
    private IEnumerator Co_InitializeGameSession()
    {
        // 1. [Server] 접속한 플레이어 스폰 (호스트/서버만 실행)
        if (_runner.IsServer)
        {
            yield return StartCoroutine(Server_SpawnAllActivePlayers());
        }

        // 2. [Client] 내 로컬 캐릭터가 생성될 때까지 대기
        yield return StartCoroutine(Client_WaitForLocalPlayer());

        // 3. [Client] 카메라 및 UI 연결
        Client_SetupLocalPlayerComponents();

        _gameManager.FinishLoadingScreen();
    }

    /// <summary>
    /// [Server] 현재 접속 중인 모든 플레이어를 순차적으로 스폰합니다.
    /// </summary>
    private IEnumerator Server_SpawnAllActivePlayers()
    {
        foreach (var playerRef in _runner.ActivePlayers)
        {
            yield return StartCoroutine(Server_SpawnPlayerRoutine(playerRef));
        }
    }

    /// <summary>
    /// [Server] 개별 플레이어 스폰 로직 (데이터 로드 대기 -> 스폰 -> 세팅)
    /// </summary>
    private IEnumerator Server_SpawnPlayerRoutine(PlayerRef playerRef)
    {
        // 이미 스폰된 플레이어라면 패스
        if (_runner.GetPlayerObject(playerRef) != null) yield break;

        // 1. PlayerData 로드 대기 (최대 5초)
        PlayerData playerData = null;
        float elapsed = 0f;

        while (playerData == null && elapsed < 5f)
        {
            playerData = GameManager.Instance?.GetPlayerData(playerRef, _runner);
            if (playerData != null) break;

            elapsed += _retryInterval;
            yield return new WaitForSeconds(_retryInterval);
        }

        if (playerData == null)
        {
            Debug.LogError($"[MainGameManager] 플레이어 {playerRef}의 데이터를 찾을 수 없습니다.");
            yield break;
        }

        // 2. 스폰 위치 및 생성
        Vector3 spawnPos = GetSceneSpawnPosition(playerRef.AsIndex);
        NetworkObject playerObj = _runner.Spawn(_playerPrefab, spawnPos, Quaternion.identity, playerRef);

        // 3. Runner에 등록 (중요: 이걸 해야 GetPlayerObject로 조회가 됨)
        _runner.SetPlayerObject(playerRef, playerObj);

        // 4. PlayerData 및 컴포넌트 초기화
        if (playerData.Object != null && playerData.Object.HasStateAuthority)
        {
            playerData.Instance = playerObj;
        }

        if (playerObj.TryGetComponent<PlayerController>(out var controller))
        {
            controller.SetCharacterIndex(playerData.CharacterIndex);
            _playerCache[playerRef] = controller;
        }

        yield return null;
    }

    /// <summary>
    /// 게임 도중 난입한 플레이어 처리
    /// </summary>
    private void OnPlayerJoinedDuringGame(PlayerRef player, NetworkRunner runner)
    {
        if (runner.IsServer && SceneManager.GetActiveScene().name == "Main")
        {
            StartCoroutine(Server_SpawnPlayerRoutine(player));
        }
    }

    /// <summary>
    /// [Client] 내 캐릭터(Local Player)가 네트워크 상에 존재할 때까지 대기
    /// </summary>
    private IEnumerator Client_WaitForLocalPlayer()
    {
        var localRef = _runner.LocalPlayer;
        float timer = 0f;

        while (timer < _timeoutPlayerSpawn)
        {
            // Fusion 내부 조회
            NetworkObject myObj = _runner.GetPlayerObject(localRef);

            if (myObj != null)
            {
                _runner.SetIsSimulated(myObj, true);
                yield break;
            }

            timer += _retryInterval;
            yield return new WaitForSeconds(_retryInterval);
        }

        Debug.LogError($"[MainGameManager] timeout");
        GameManager.Instance?.ShowWarningPanel("캐릭터 생성 실패 (서버 응답 없음)");
    }

    /// <summary>
    /// [Client] 찾은 로컬 플레이어에 카메라와 UI를 연결
    /// </summary>
    private void Client_SetupLocalPlayerComponents()
    {
        PlayerController localPlayer = GetLocalPlayer();

        // 1. 카메라 연결
        if (Camera.main != null && Camera.main.TryGetComponent<MainCameraController>(out var camController))
        {
            camController.SetTarget(localPlayer);
        }

        // 2. UI 연결
        if (_gameManager != null && _gameManager.Canvas is MainCanvas mainCanvas)
        {
            
        }
    }

    /// <summary>
    /// 로컬 플레이어 가져오기
    /// </summary>
    public PlayerController GetLocalPlayer()
    {
        if (_isTestMode) return GetTestModePlayer();
        if (_runner == null) return null;
        return GetPlayer(_runner.LocalPlayer);
    }

    /// <summary>
    /// 특정 PlayerRef의 컨트롤러 가져오기 (캐싱 적용)
    /// </summary>
    public PlayerController GetPlayer(PlayerRef playerRef)
    {
        // 1. 캐시 확인
        if (_playerCache.TryGetValue(playerRef, out var cachedPc))
        {
            if (cachedPc != null && cachedPc.Object != null && cachedPc.Object.IsValid)
                return cachedPc;

            _playerCache.Remove(playerRef);
        }

        // 2. Fusion Runner에서 조회
        NetworkObject nob = _runner.GetPlayerObject(playerRef);
        if (nob != null && nob.TryGetComponent<PlayerController>(out var pc))
        {
            _playerCache[playerRef] = pc;
            return pc;
        }

        return null;
    }

    /// <summary>
    /// 현재 유효한 모든 플레이어 리스트 반환
    /// </summary>
    public List<PlayerController> GetAllPlayers()
    {
        if (_isTestMode) return GetTestModeAllPlayers();

        var results = new List<PlayerController>();

        // ActivePlayers 기준으로 최신 상태 조회
        foreach (var playerRef in _runner.ActivePlayers)
        {
            var pc = GetPlayer(playerRef);
            if (pc != null && !pc.IsDead)
            {
                results.Add(pc);
            }
        }
        return results;
    }

    /// <summary>
    /// 나를 제외한 다른 플레이어 찾기 (1:1 게임 가정)
    /// </summary>
    public PlayerController FindOtherPlayer(PlayerController localPlayer)
    {
        if (localPlayer == null) return null;

        foreach (var pc in GetAllPlayers())
        {
            // 내가 아니고, 죽지 않은 플레이어 리턴
            if (pc != localPlayer && pc.Object.InputAuthority != localPlayer.Object.InputAuthority)
            {
                return pc;
            }
        }
        return null;
    }

    private Vector3 GetSceneSpawnPosition(int index)
    {
        if (ScenSpawner.Instance != null) return ScenSpawner.Instance.GetSpawnPosition(index);
        Vector2[] defaults = { new Vector2(0, 2), new Vector2(0, -2) };
        return defaults[index % defaults.Length];
    }

    private PlayerController GetTestModePlayer()
    {
        var target = SelectedSlot == 0 ? _testPlayerObj1 : _testPlayerObj2;
        return (target != null && target.IsValid) ? target.GetComponent<PlayerController>() : null;
    }

    private List<PlayerController> GetTestModeAllPlayers()
    {
        var list = new List<PlayerController>();
        if (_testPlayerObj1 != null) list.Add(_testPlayerObj1.GetComponent<PlayerController>());
        if (_testPlayerObj2 != null) list.Add(_testPlayerObj2.GetComponent<PlayerController>());
        return list;
    }
}