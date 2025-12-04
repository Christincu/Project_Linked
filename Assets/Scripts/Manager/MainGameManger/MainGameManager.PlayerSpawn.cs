using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;

public partial class MainGameManager
{
    // ==================================================================================
    // 1. [새로운 초기화 흐름] Spawned()에서 직접 호출 (Runner 대기 없음)
    // ==================================================================================

    /// <summary>
    /// [수정된 초기화 흐름] 대기 없이 바로 실행
    /// Spawned()에서 호출되므로 Runner가 이미 준비되어 있습니다.
    /// </summary>
    private IEnumerator Co_InitializeGameSession_Direct()
    {
        // 1. (Server) 접속한 플레이어 스폰 처리
        if (_runner.IsServer)
        {
            yield return StartCoroutine(Server_VerifyAndSpawnAllPlayers());
        }

        // 2. (Client) 내 로컬 플레이어 객체 찾기 (비상 대책 포함)
        yield return StartCoroutine(Client_WaitForLocalPlayerObject_ForceFind());

        // 3. 컴포넌트 연결 (재시도 로직 포함)
        yield return StartCoroutine(Co_InitializeLocalPlayerComponents());

        // 4. 로딩 종료
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
    /// [강력한 찾기] Runner 대기 없이 씬을 직접 뒤져서라도 내 캐릭터를 찾아냅니다.
    /// </summary>
    private IEnumerator Client_WaitForLocalPlayerObject_ForceFind()
    {
        var localRef = _runner.LocalPlayer;
        float timeout = 20f;
        float timer = 0f;

        while (timer < timeout)
        {
            NetworkObject myObj = null;

            // 1. Runner 정석 확인
            myObj = _runner.GetPlayerObject(localRef);

            // 2. 씬 검색 (Fallback)
            if (myObj == null)
            {
                var controllers = FindObjectsOfType<PlayerController>();
                foreach (var pc in controllers)
                {
                    if (pc.Object != null && pc.Object.IsValid && pc.Object.HasInputAuthority)
                    {
                        myObj = pc.Object;
                        // Runner에 강제 등록 (다음번엔 정석으로 찾게)
                        _runner.SetPlayerObject(localRef, myObj);
                        break;
                    }
                }
            }

            // 3. 찾음 확인 및 처리
            if (myObj != null && myObj.IsValid)
            {
                if (!_spawnedPlayers.ContainsKey(localRef))
                {
                    _spawnedPlayers[localRef] = myObj;
                }

                // [중요] Rigidbody 경고 해결 코드
                _runner.SetIsSimulated(myObj, true);
                yield break; // 성공!
            }

            yield return new WaitForSeconds(0.1f);
            timer += 0.1f;
        }

        GameManager.Instance?.ShowWarningPanel("캐릭터 생성 실패 (타임아웃)");
    }

    // ==================================================================================
    // 2. 기존 초기화 메인 흐름 (테스트 모드 또는 레거시용)
    // ==================================================================================

    private IEnumerator Co_InitializeGameSession()
    {
        // 1. NetworkRunner 찾기 (무조건 필수)
        yield return StartCoroutine(Co_WaitForRunner());

        // 2. (Host/Server) 접속한 플레이어 스폰 처리
        if (_runner.IsServer)
        {
            yield return StartCoroutine(Server_VerifyAndSpawnAllPlayers());
        }

        // 3. (Client) 내 로컬 플레이어 객체가 생성되고 매핑될 때까지 대기
        yield return StartCoroutine(Client_WaitForLocalPlayerObject());

        // 4. 컴포넌트 연결 (카메라, UI 등) - 재시도 로직 포함
        yield return StartCoroutine(Co_InitializeLocalPlayerComponents());

        // 5. 로딩 종료
        if (GameManager.Instance != null)
        {
            GameManager.Instance.FinishLoadingScreen();
        }
        else
        {
            Debug.LogError("[MainGameManager] GameManager.Instance가 null입니다! 로딩 화면을 닫을 수 없습니다.");
        }
    }

    // ==================================================================================
    // 3. 단계별 코루틴 (단순화)
    // ==================================================================================

    /// <summary>
    /// NetworkRunner가 준비될 때까지 대기합니다.
    /// </summary>
    private IEnumerator Co_WaitForRunner()
    {
        float timeout = 10f;
        float timer = 0f;

        while (_runner == null || !_runner.IsRunning)
        {
            _runner = FusionManager.LocalRunner ?? FindObjectOfType<NetworkRunner>();
            
            if (_runner != null && _runner.IsRunning) break;

            if (timer > timeout)
            {
                Debug.LogError("[MainGameManager] Critical Error: NetworkRunner not found.");
                GameManager.Instance?.ShowWarningPanel("네트워크 연결을 찾을 수 없습니다.");
                yield break;
            }

            timer += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
    }

    /// <summary>
    /// [서버] 현재 접속한 모든 플레이어에 대해 캐릭터가 없으면 스폰합니다.
    /// </summary>
    private IEnumerator Server_VerifyAndSpawnAllPlayers()
    {
        foreach (var playerRef in _runner.ActivePlayers)
        {
            yield return StartCoroutine(Server_SpawnPlayerIfNeeded(playerRef));
        }
    }

    /// <summary>
    /// [클라이언트] 로컬 플레이어 객체를 찾습니다.
    /// 서버의 SetPlayerObject가 늦거나 누락되어도, 씬에 있는 객체를 직접 찾아내어 흐름을 뚫어줍니다.
    /// </summary>
    private IEnumerator Client_WaitForLocalPlayerObject()
    {
        var localRef = _runner.LocalPlayer;
        float timeout = 20f;
        float timer = 0f;

        while (timer < timeout)
        {
            NetworkObject myObj = null;

            // 1. 정석 확인
            myObj = _runner.GetPlayerObject(localRef);

            // 2. 비상 대책 (씬 뒤지기)
            if (myObj == null)
            {
                var controllers = FindObjectsOfType<PlayerController>();
                foreach (var pc in controllers)
                {
                    if (pc.Object != null && pc.Object.IsValid && pc.Object.HasInputAuthority)
                    {
                        myObj = pc.Object;
                        // Runner에 등록 (다음번엔 정석으로 찾게)
                        _runner.SetPlayerObject(localRef, myObj);
                        break;
                    }
                }
            }

            // 3. 찾았으면 성공 처리
            if (myObj != null && myObj.IsValid)
            {
                if (!_spawnedPlayers.ContainsKey(localRef))
                {
                    _spawnedPlayers[localRef] = myObj;
                }

                // 물리 시뮬레이션 강제 켜기
                _runner.SetIsSimulated(myObj, true);
                yield break;
            }

            yield return new WaitForSeconds(0.1f);
            timer += 0.1f;
        }

        Debug.LogError($"[MainGameManager] 타임아웃! 20초 동안 내 캐릭터를 못 찾았습니다.");
        GameManager.Instance?.ShowWarningPanel("캐릭터 생성 응답 시간이 초과되었습니다.");
    }

    /// <summary>
    /// 로컬 플레이어를 찾은 직후 실행되는 연결 로직입니다. (UI, 카메라)
    /// GetLocalPlayer()가 null을 반환하면 기다렸다가 재시도합니다.
    /// </summary>
    private IEnumerator Co_InitializeLocalPlayerComponents()
    {
        const float maxWaitTime = 10f;
        const float retryInterval = 0.1f;
        float elapsedTime = 0f;
        
        PlayerController localPlayer = null;
        
        // GetLocalPlayer()가 null이 아닐 때까지 재시도
        while (localPlayer == null && elapsedTime < maxWaitTime)
        {
            localPlayer = GetLocalPlayer();
            
            if (localPlayer == null)
            {
                elapsedTime += retryInterval;
                yield return new WaitForSeconds(retryInterval);
            }
        }
        
        if (localPlayer == null)
        {
            Debug.LogError($"[MainGameManager] InitializeLocalPlayerComponents GetLocalPlayer() null after {maxWaitTime}s timeout");
            yield break;
        }

        // 1. 카메라 연결
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            var camController = mainCamera.GetComponent<MainCameraController>();
            if (camController != null)
            {
                camController.SetTarget(localPlayer);
            }
        }

        // 2. UI 연결
        if (GameManager.Instance != null)
        {
            if (GameManager.Instance.Canvas is MainCanvas mainCanvas)
            {
                mainCanvas.RegisterPlayer(localPlayer);
            }
        }
        else
        {
            Debug.LogError("[MainGameManager] GameManager.Instance가 null입니다!");
        }
    }

    // ==================================================================================
    // 4. 서버 스폰 로직 (단일 책임)
    // ==================================================================================

    private IEnumerator Server_SpawnPlayerIfNeeded(PlayerRef playerRef)
    {
        if (!_runner.IsServer) yield break;

        // 이미 등록된 유효한 객체가 있는지 확인
        if (_spawnedPlayers.TryGetValue(playerRef, out var existingObj))
        {
            if (existingObj != null && existingObj.IsValid) yield break; // 이미 존재함
        }

        // 스폰 전 PlayerData 확인 대기
        PlayerData playerData = GameManager.Instance?.GetPlayerData(playerRef, _runner);
        if (playerData == null)
        {
            Debug.LogWarning($"[MainGameManager] PlayerData not found for player {playerRef}. Waiting...");
            float waitTime = 0f;
            while (playerData == null && waitTime < 5f)
            {
                yield return new WaitForSeconds(0.1f);
                waitTime += 0.1f;
                playerData = GameManager.Instance?.GetPlayerData(playerRef, _runner);
            }
            
            if (playerData == null)
            {
                Debug.LogError($"[MainGameManager] PlayerData not found for player {playerRef} after waiting.");
                yield break;
            }
        }

        // 실제 캐릭터 스폰
        int charIndex = playerData.CharacterIndex;
        Vector3 spawnPos = GetSceneSpawnPosition(playerRef.AsIndex);
        
        NetworkObject playerObj = _runner.Spawn(_playerPrefab, spawnPos, Quaternion.identity, playerRef);
        
        // **중요**: Runner에 플레이어 객체 등록 (클라이언트가 GetPlayerObject로 찾을 수 있게 함)
        _runner.SetPlayerObject(playerRef, playerObj);
        _spawnedPlayers[playerRef] = playerObj;

        // [수정] PlayerData.Instance에 플레이어 오브젝트 할당 (StateAuthority에서만 가능)
        if (playerData != null && playerData.Object != null && playerData.Object.HasStateAuthority)
        {
            playerData.Instance = playerObj;
            Debug.Log($"[MainGameManager] Assigned player object to PlayerData.Instance for player {playerRef}");
        }

        // 초기화 설정
        var controller = playerObj.GetComponent<PlayerController>();
        if (controller != null)
        {
            controller.SetCharacterIndex(charIndex);
        }

        yield return null;
    }

    // ==================================================================================
    // 5. 이벤트 핸들러 및 유틸리티
    // ==================================================================================

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

        var localRef = _runner.LocalPlayer;

        // 1. 캐시된 딕셔너리 우선 확인
        if (_spawnedPlayers.TryGetValue(localRef, out var obj) && obj != null && obj.IsValid)
        {
            var controller = obj.GetComponent<PlayerController>();
            if (controller != null) return controller;
        }

        // 2. Runner에서 직접 조회
        var networkObj = _runner.GetPlayerObject(localRef);
        if (networkObj != null && networkObj.IsValid && networkObj.HasInputAuthority)
        {
            var controller = networkObj.GetComponent<PlayerController>();
            if (controller != null)
            {
                // 딕셔너리에 등록 (다음번엔 빠르게 찾을 수 있도록)
                if (!_spawnedPlayers.ContainsKey(localRef))
                {
                    _spawnedPlayers[localRef] = networkObj;
                }
                return controller;
            }
        }

        // 3. PlayerData.Instance를 통한 조회 (Fallback)
        PlayerData playerData = GameManager.Instance?.GetPlayerData(localRef, _runner);
        if (playerData != null && playerData.Instance != null && playerData.Instance.IsValid)
        {
            var controller = playerData.Instance.GetComponent<PlayerController>();
            if (controller != null)
            {
                // 딕셔너리에 등록
                if (!_spawnedPlayers.ContainsKey(localRef))
                {
                    _spawnedPlayers[localRef] = playerData.Instance;
                }
                // Runner에도 등록
                if (_runner.GetPlayerObject(localRef) == null)
                {
                    _runner.SetPlayerObject(localRef, playerData.Instance);
                }
                return controller;
            }
        }

        // 4. 씬에서 직접 찾기 (최후의 수단)
        var controllers = FindObjectsOfType<PlayerController>();
        foreach (var pc in controllers)
        {
            if (pc.Object != null && pc.Object.IsValid && pc.Object.HasInputAuthority)
            {
                // 딕셔너리와 Runner에 등록
                if (!_spawnedPlayers.ContainsKey(localRef))
                {
                    _spawnedPlayers[localRef] = pc.Object;
                }
                if (_runner.GetPlayerObject(localRef) == null)
                {
                    _runner.SetPlayerObject(localRef, pc.Object);
                }
                return pc;
            }
        }

        return null;
    }
    
    // 단순 조회용 (fallback 로직 포함)
    public PlayerController GetPlayer(PlayerRef playerRef)
    {
        // 1. 캐시된 딕셔너리 우선 확인
        if (_spawnedPlayers.TryGetValue(playerRef, out var playerObj) && playerObj != null && playerObj.IsValid)
        {
            var controller = playerObj.GetComponent<PlayerController>();
            if (controller != null) return controller;
        }

        // 2. Runner에서 직접 조회
        if (_runner != null)
        {
            var networkObj = _runner.GetPlayerObject(playerRef);
            if (networkObj != null && networkObj.IsValid)
            {
                var controller = networkObj.GetComponent<PlayerController>();
                if (controller != null)
                {
                    // 딕셔너리에 등록 (다음번엔 빠르게 찾을 수 있도록)
                    if (!_spawnedPlayers.ContainsKey(playerRef))
                    {
                        _spawnedPlayers[playerRef] = networkObj;
                    }
                    return controller;
                }
            }
        }

        // 3. 모든 플레이어를 순회하며 찾기 (fallback)
        var allPlayers = GetAllPlayers();
        foreach (var player in allPlayers)
        {
            if (player != null && player.Object != null && player.Object.IsValid)
            {
                if (player.Object.InputAuthority == playerRef)
                {
                    // 딕셔너리에 등록
                    if (!_spawnedPlayers.ContainsKey(playerRef))
                    {
                        _spawnedPlayers[playerRef] = player.Object;
                    }
                    return player;
                }
            }
        }

        return null;
    }

    public List<PlayerController> GetAllPlayers()
    {
        // [수정] 테스트 모드에서는 _playerObj1과 _playerObj2를 모두 반환
        if (_isTestMode)
        {
            List<PlayerController> testPlayers = new List<PlayerController>();
            
            if (_playerObj1 != null && _playerObj1.IsValid)
            {
                var controller1 = _playerObj1.GetComponent<PlayerController>();
                if (controller1 != null && !controller1.IsDead)
                {
                    testPlayers.Add(controller1);
                }
            }
            
            if (_playerObj2 != null && _playerObj2.IsValid)
            {
                var controller2 = _playerObj2.GetComponent<PlayerController>();
                if (controller2 != null && !controller2.IsDead)
                {
                    testPlayers.Add(controller2);
                }
            }
            
            return testPlayers;
        }
        
        // 일반 모드: 기존 로직 유지
        return _spawnedPlayers.Values
            .Where(obj => obj != null && obj.IsValid)
            .Select(obj => obj.GetComponent<PlayerController>())
            .Where(c => c != null)
            .ToList();
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