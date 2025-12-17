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

    [Header("Spawn Settings")]
    [SerializeField] private Vector2[] _spawnPositions = new Vector2[]
    {
        new Vector2(0, 2), new Vector2(0, -2)
    };

    public static int SelectedSlot = 0;
    private NetworkObject _playerObj1;
    private NetworkObject _playerObj2;

    [SerializeField] private int _firstCharacterIndex = 0;
    [SerializeField] private int _secondCharacterIndex = 1;

    private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();

    private const float TIMEOUT_RUNNER_SEARCH = 10f;
    private const float TIMEOUT_PLAYER_SPAWN = 20f;
    private const float TIMEOUT_COMPONENT_INIT = 10f;
    private const float RETRY_INTERVAL = 0.1f;

    /// <summary>
    /// [최적화된 초기화] Spawned()에서 호출. Runner 대기 없이 바로 실행.
    /// </summary>
    private IEnumerator Co_InitializeGameSession_Direct()
    {
        // 1. (Server) 접속한 플레이어 스폰 처리
        if (_runner.IsServer)
        {
            yield return StartCoroutine(Server_VerifyAndSpawnAllPlayers());
        }

        // 2. (Client) 내 로컬 플레이어 객체 찾기 (통합된 강력한 탐색 로직 사용)
        yield return StartCoroutine(Client_WaitForLocalPlayerObject());

        // 3. 컴포넌트 연결 (재시도 로직 포함)
        yield return StartCoroutine(Co_InitializeLocalPlayerComponents());

        // 4. 로딩 종료
        FinishLoading();
    }

    /// <summary>
    /// [레거시 초기화] Runner 찾기부터 시작하는 흐름
    /// </summary>
    private IEnumerator Co_InitializeGameSession()
    {
        // 1. NetworkRunner 찾기
        yield return StartCoroutine(Co_WaitForRunner());

        // Direct와 동일한 흐름으로 위임하여 로직 중복 제거
        yield return StartCoroutine(Co_InitializeGameSession_Direct());
    }

    private void FinishLoading()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.FinishLoadingScreen();
        }
        else
        {
            Debug.LogError("[MainGameManager] GameManager 인스턴스가 없어 로딩 패널을 끌 수 없습니다.");
        }
    }

    /// <summary>
    /// NetworkRunner가 준비될 때까지 대기합니다.
    /// </summary>
    private IEnumerator Co_WaitForRunner()
    {
        float timer = 0f;

        while (_runner == null || !_runner.IsRunning)
        {
            _runner = FusionManager.LocalRunner ?? FindObjectOfType<NetworkRunner>();
            
            if (_runner != null && _runner.IsRunning) yield break;

            if (timer > TIMEOUT_RUNNER_SEARCH)
            {
                Debug.LogError("[MainGameManager] Critical Error: NetworkRunner not found.");
                GameManager.Instance?.ShowWarningPanel("네트워크 연결을 찾을 수 없습니다.");
                yield break;
            }

            timer += RETRY_INTERVAL;
            yield return new WaitForSeconds(RETRY_INTERVAL);
        }
    }

    /// <summary>
    /// [서버] 현재 접속한 모든 플레이어 확인 및 스폰
    /// </summary>
    private IEnumerator Server_VerifyAndSpawnAllPlayers()
    {
        if (!_runner.IsServer) yield break;

        foreach (var playerRef in _runner.ActivePlayers)
        {
            yield return StartCoroutine(Server_SpawnPlayerIfNeeded(playerRef));
        }
    }

    /// <summary>
    /// [클라이언트] 로컬 플레이어 객체를 찾습니다. (ForceFind 로직 통합)
    /// </summary>
    private IEnumerator Client_WaitForLocalPlayerObject()
    {
        // 이미 찾았다면 스킵
        if (_spawnedPlayers.ContainsKey(_runner.LocalPlayer) && _spawnedPlayers[_runner.LocalPlayer] != null)
            yield break;

        yield return StartCoroutine(Client_WaitForLocalPlayerObject_ForceFind());
    }

    /// <summary>
    /// [강력한 찾기] Runner 대기 + 씬 검색을 통해 내 캐릭터를 찾아냅니다.
    /// </summary>
    private IEnumerator Client_WaitForLocalPlayerObject_ForceFind()
    {
        var localRef = _runner.LocalPlayer;
        float timer = 0f;

        while (timer < TIMEOUT_PLAYER_SPAWN)
        {
            // 통합된 검색 로직 사용
            PlayerController myPC = ResolvePlayerController(localRef);

            if (myPC != null)
            {
                // [중요] Rigidbody 경고 해결 코드
                if (myPC.Object != null)
                {
                    _runner.SetIsSimulated(myPC.Object, true);
                }
                yield break; // 성공
            }

            yield return new WaitForSeconds(RETRY_INTERVAL);
            timer += RETRY_INTERVAL;
        }

        Debug.LogError($"[MainGameManager] 타임아웃! {TIMEOUT_PLAYER_SPAWN}초 동안 내 캐릭터를 못 찾았습니다.");
        GameManager.Instance?.ShowWarningPanel("캐릭터 생성 실패 (타임아웃)");
    }

    /// <summary>
    /// 로컬 플레이어 컴포넌트(카메라, UI) 연결
    /// </summary>
    private IEnumerator Co_InitializeLocalPlayerComponents()
    {
        float elapsedTime = 0f;
        PlayerController localPlayer = null;
        
        // GetLocalPlayer() 재시도 로직
        while (localPlayer == null && elapsedTime < TIMEOUT_COMPONENT_INIT)
        {
            localPlayer = GetLocalPlayer();
            if (localPlayer != null) break;

            elapsedTime += RETRY_INTERVAL;
            yield return new WaitForSeconds(RETRY_INTERVAL);
        }
        
        if (localPlayer == null)
        {
            Debug.LogError($"[MainGameManager] InitializeLocalPlayerComponents Failed: GetLocalPlayer() null.");
            yield break;
        }

        // 1. 카메라 연결
        if (Camera.main != null && Camera.main.TryGetComponent<MainCameraController>(out var camController))
        {
            camController.SetTarget(localPlayer);
        }

        // 2. UI 연결
        if (GameManager.Instance != null && GameManager.Instance.Canvas is MainCanvas mainCanvas)
        {
            mainCanvas.RegisterPlayer(localPlayer);
        }
    }

    private IEnumerator Server_SpawnPlayerIfNeeded(PlayerRef playerRef)
    {
        if (!_runner.IsServer) yield break;

        // 1. 이미 존재하는지 확인
        if (CheckIfPlayerExists(playerRef)) yield break;

        // 2. PlayerData 대기
        PlayerData playerData = null;
        float waitTime = 0f;
        
        while (waitTime < 5f)
        {
            playerData = GameManager.Instance?.GetPlayerData(playerRef, _runner);
            if (playerData != null) break;

            yield return new WaitForSeconds(RETRY_INTERVAL);
            waitTime += RETRY_INTERVAL;
        }

        if (playerData == null)
        {
            Debug.LogError($"[MainGameManager] PlayerData not found for player {playerRef}.");
            yield break;
        }

        // 3. 스폰 실행
        int charIndex = playerData.CharacterIndex;
        Vector3 spawnPos = GetSceneSpawnPosition(playerRef.AsIndex);
        
        NetworkObject playerObj = _runner.Spawn(_playerPrefab, spawnPos, Quaternion.identity, playerRef);
        
        // 4. 등록 절차
        RegisterSpawnedPlayer(playerRef, playerObj);

        // PlayerData에 인스턴스 할당 (StateAuthority)
        if (playerData.Object != null && playerData.Object.HasStateAuthority)
        {
            playerData.Instance = playerObj;
        }

        // 초기화
        if (playerObj.TryGetComponent<PlayerController>(out var controller))
        {
            controller.SetCharacterIndex(charIndex);
        }

        yield return null;
    }

    private bool CheckIfPlayerExists(PlayerRef playerRef)
    {
        if (_spawnedPlayers.TryGetValue(playerRef, out var existingObj))
        {
            return existingObj != null && existingObj.IsValid;
        }
        return false;
    }

    private void RegisterSpawnedPlayer(PlayerRef playerRef, NetworkObject playerObj)
    {
        if (playerObj == null) return;

        _runner.SetPlayerObject(playerRef, playerObj);
        
        if (!_spawnedPlayers.ContainsKey(playerRef))
        {
            _spawnedPlayers[playerRef] = playerObj;
        }
    }

    private void OnPlayerJoinedDuringGame(PlayerRef player, NetworkRunner runner)
    {
        if (runner.IsServer && SceneManager.GetActiveScene().name == "Main")
        {
            StartCoroutine(Server_SpawnPlayerIfNeeded(player));
        }
    }

    public PlayerController GetLocalPlayer()
    {
        if (_isTestMode) return GetSelectedPlayer();
        if (_runner == null) return null;

        return ResolvePlayerController(_runner.LocalPlayer);
    }

    public PlayerController GetPlayer(PlayerRef playerRef)
    {
        return ResolvePlayerController(playerRef);
    }

    /// <summary>
    /// [핵심] 딕셔너리 -> Runner -> Scene 순서로 플레이어를 찾고 캐싱하는 통합 함수
    /// </summary>
    private PlayerController ResolvePlayerController(PlayerRef playerRef)
    {
        // 1. 캐시(Dictionary) 확인
        if (_spawnedPlayers.TryGetValue(playerRef, out var cachedObj) && cachedObj != null && cachedObj.IsValid)
        {
            var pc = cachedObj.GetComponent<PlayerController>();
            if (pc != null) return pc;
        }

        NetworkObject targetObj = null;

        // 2. Runner 확인
        if (_runner != null)
        {
            targetObj = _runner.GetPlayerObject(playerRef);
        }

        // 3. Fallback: PlayerData 확인
        if (targetObj == null && GameManager.Instance != null)
        {
            var pd = GameManager.Instance.GetPlayerData(playerRef, _runner);
            if (pd != null && pd.Instance != null && pd.Instance.IsValid)
            {
                targetObj = pd.Instance;
            }
        }

        // 4. Fallback: Scene 전체 검색 (최후의 수단)
        if (targetObj == null)
        {
            var controllers = FindObjectsOfType<PlayerController>();
            foreach (var pc in controllers)
            {
                if (pc.Object != null && pc.Object.IsValid && pc.Object.InputAuthority == playerRef)
                {
                    targetObj = pc.Object;
                    break;
                }
            }
        }

        // 5. 결과 처리 및 캐싱
        if (targetObj != null && targetObj.IsValid)
        {
            // 캐시 갱신
            RegisterSpawnedPlayer(playerRef, targetObj);
            return targetObj.GetComponent<PlayerController>();
        }

        return null;
    }

    public List<PlayerController> GetAllPlayers()
    {
        // [테스트 모드]
        if (_isTestMode)
        {
            var list = new List<PlayerController>();
            AddIfValid(_playerObj1, list);
            AddIfValid(_playerObj2, list);
            return list;
        }

        // [일반 모드] 캐싱된 데이터 기반 + 누락된 데이터 보정
        var results = new List<PlayerController>();
        
        // 1. 딕셔너리 기반으로 리스트 생성
        foreach (var kvp in _spawnedPlayers)
        {
            if (kvp.Value != null && kvp.Value.IsValid)
            {
                var pc = kvp.Value.GetComponent<PlayerController>();
                if (pc != null) results.Add(pc);
            }
        }

        // 2. Runner의 ActivePlayers와 개수가 다르면 보정 시도 (동기화)
        if (_runner != null && _runner.IsRunning && _runner.ActivePlayers.Count() != results.Count)
        {
            foreach (var playerRef in _runner.ActivePlayers)
            {
                // 이미 리스트에 있으면 패스
                bool exists = false;
                for(int i = 0; i < results.Count; i++)
                {
                    if (results[i].Object.InputAuthority == playerRef) 
                    {
                        exists = true; 
                        break;
                    }
                }

                if (!exists)
                {
                    var pc = ResolvePlayerController(playerRef); // 찾아서 캐싱하고 반환
                    if (pc != null) results.Add(pc);
                }
            }
        }

        return results;

        // 로컬 헬퍼
        void AddIfValid(NetworkObject obj, List<PlayerController> list)
        {
            if (obj != null && obj.IsValid && obj.TryGetComponent<PlayerController>(out var pc) && !pc.IsDead)
                list.Add(pc);
        }
    }

    /// <summary>
    /// 상대방 플레이어를 찾습니다.
    /// </summary>
    public PlayerController FindOtherPlayer(PlayerController localPlayer)
    {
        if (localPlayer == null) return null;

        // [테스트 모드]
        if (_isTestMode)
        {
            var selected = GetSelectedPlayer();
            var otherObj = (selected == _playerObj1) ? _playerObj2 : _playerObj1; // 반대편 선택
            if (localPlayer == selected)
            {
                return (otherObj != null && otherObj.IsValid) ? otherObj.GetComponent<PlayerController>() : null;
            }
            return selected;
        }

        // [일반 모드]
        var allPlayers = GetAllPlayers();
        foreach (var p in allPlayers)
        {
            // 나 자신이 아니고, 죽지 않았으며, 유효한 객체
            if (p != localPlayer && !p.IsDead && p.Object != null && p.Object.IsValid)
            {
                // InputAuthority가 다르면 상대방
                if (p.Object.InputAuthority != localPlayer.Object.InputAuthority)
                {
                    return p;
                }
            }
        }

        return null;
    }

    private Vector3 GetSceneSpawnPosition(int index)
    {
        if (ScenSpawner.Instance != null) return ScenSpawner.Instance.GetSpawnPosition(index);
        return _spawnPositions[index % _spawnPositions.Length];
    }
    
    // 테스트 모드용
    public PlayerController GetSelectedPlayer()
    {
        var playerObj = SelectedSlot == 0 ? _playerObj1 : _playerObj2;
        return (playerObj != null && playerObj.IsValid) ? playerObj.GetComponent<PlayerController>() : null;
    }
}