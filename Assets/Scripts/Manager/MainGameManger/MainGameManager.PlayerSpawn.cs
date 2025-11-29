using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;

/// <summary>
/// 플레이어 스폰 및 관리 관련 기능을 모은 partial 클래스입니다.
/// </summary>
public partial class MainGameManager
{
    /// <summary>
    /// 게임 세션 초기화 메인 코루틴
    /// </summary>
    private IEnumerator Co_InitializeGameSession()
    {
        System.Exception initException = null;
        NetworkRunner runner = null;
        
        int runnerWaitAttempts = 0;
        int maxRunnerWaitAttempts = 300;
        
        while (runnerWaitAttempts < maxRunnerWaitAttempts)
        {
            runner = FusionManager.LocalRunner ?? FindObjectOfType<NetworkRunner>();
            if (runner != null && runner.IsRunning) break;
            yield return null;
            runnerWaitAttempts++;
        }
        
        if (runner == null || !runner.IsRunning)
        {
            Debug.LogError("[MainGameManager] Failed to find valid NetworkRunner after timeout");
            initException = new System.Exception("NetworkRunner not found or not running");
        }
        else
        {
            _runner = runner;
            yield return new WaitForSeconds(0.5f);

            if (runner.IsServer)
            {
                yield return StartCoroutine(SafeRunCoroutine(Server_VerifyAndSpawnPlayers(runner)));
            }
            else
            {
                yield return StartCoroutine(SafeRunCoroutine(Client_WaitForServerToSpawnPlayers(runner)));
            }
            
            var localPlayerRef = runner.LocalPlayer;
            float timeout = 15f;
            bool playerFound = false;

            while (timeout > 0 && runner != null && runner.IsRunning)
            {
                FindAndRegisterSpawnedPlayersSafe(runner);
                
                var myObj = runner.GetPlayerObject(localPlayerRef);
                
                if (myObj != null && myObj.IsValid)
                {
                    if (myObj.InputAuthority == localPlayerRef)
                    {
                        _spawnedPlayers[localPlayerRef] = myObj;
                        playerFound = true;
                        break;
                    }
                }
                
                if (_spawnedPlayers.ContainsKey(localPlayerRef))
                {
                    var dictObj = _spawnedPlayers[localPlayerRef];
                    if (dictObj != null && dictObj.IsValid && dictObj.InputAuthority == localPlayerRef)
                    {
                        playerFound = true;
                        break;
                    }
                }
                
                timeout -= 0.1f;
                yield return new WaitForSeconds(0.1f);
            }
            
            if (!playerFound)
            {
                Debug.LogError("[MainGameManager] CRITICAL: Failed to find Local Player Object! Game might not work correctly.");
                GameManager.Instance?.ShowWarningPanel("플레이어 캐릭터를 찾을 수 없습니다. 게임이 정상적으로 작동하지 않을 수 있습니다.");
            }
            
            InitializeComponentsSafe(ref initException);
            
            if (runner.IsServer && runner.IsRunning)
            {
                yield return StartCoroutine(SafeRunCoroutine(InitializeMapDoorStateAsync(runner)));
            }
        }
        
        if (initException != null)
        {
            Debug.LogError("[MainGameManager] Initialization completed with errors. Showing warning panel.");
            GameManager.Instance?.ShowWarningPanel("게임 초기화 중 오류가 발생했습니다.");
        }
        
        yield return new WaitForSeconds(0.2f);
        GameManager.Instance?.FinishLoadingScreen();
    }
    
    /// <summary>
    /// 코루틴을 안전하게 실행합니다. 예외가 발생해도 계속 진행됩니다.
    /// </summary>
    private IEnumerator SafeRunCoroutine(IEnumerator coroutine)
    {
        if (coroutine == null) yield break;
        
        while (true)
        {
            bool moveNext = false;
            
            try
            {
                moveNext = coroutine.MoveNext();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MainGameManager] Error in coroutine execution: {e.Message}\n{e.StackTrace}");
                yield break;
            }
            
            if (!moveNext)
            {
                break;
            }
            
            yield return coroutine.Current;
        }
    }
    
    /// <summary>
    /// 안전하게 플레이어를 찾아 등록합니다.
    /// </summary>
    private void FindAndRegisterSpawnedPlayersSafe(NetworkRunner runner)
    {
        try
        {
            FindAndRegisterSpawnedPlayers(runner);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MainGameManager] Error in FindAndRegisterSpawnedPlayers: {e.Message}");
        }
    }
    
    /// <summary>
    /// 안전하게 컴포넌트를 초기화합니다.
    /// </summary>
    private void InitializeComponentsSafe(ref System.Exception initException)
    {
        try
        {
            InitializeMainCameraForNetworkMode();
            InitializeStageEnemySpawning();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MainGameManager] Error during initialization: {e.Message}\n{e.StackTrace}");
            initException = e;
        }
    }
    
    /// <summary>
    /// 클라이언트가 서버가 플레이어를 스폰할 때까지 대기합니다.
    /// </summary>
    private IEnumerator Client_WaitForServerToSpawnPlayers(NetworkRunner runner)
    {
        if (runner.IsServer) yield break;
        
        var localPlayerRef = runner.LocalPlayer;
        float timeout = 15f;
        float waitTimer = 0f;
        
        while (waitTimer < timeout)
        {
            var playerObj = runner.GetPlayerObject(localPlayerRef);
            
            if (playerObj != null && playerObj.IsValid && playerObj.InputAuthority == localPlayerRef)
            {
                yield break;
            }
            
            waitTimer += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        
        Debug.LogWarning($"[MainGameManager] Client: Timeout waiting for server to spawn player object after {timeout}s. Continuing anyway...");
    }

    /// <summary>
    /// 서버에서 플레이어 데이터와 캐릭터를 검증하고 스폰합니다.
    /// </summary>
    private IEnumerator Server_VerifyAndSpawnPlayers(NetworkRunner runner)
    {
        if (!runner.IsServer) yield break;

        foreach (var playerRef in runner.ActivePlayers)
        {
            if (_spawnedPlayers.ContainsKey(playerRef))
            {
                var existingObj = _spawnedPlayers[playerRef];
                if (existingObj != null && existingObj.IsValid && existingObj.InputAuthority == playerRef)
                {
                    continue;
                }
                else
                {
                    _spawnedPlayers.Remove(playerRef);
                }
            }
            
            PlayerData playerData = GameManager.Instance?.GetPlayerData(playerRef, runner);
            
            if (playerData != null && (playerData.Object == null || !playerData.Object.IsValid))
            {
                GameManager.Instance?.SetPlayerData(playerRef, null);
                playerData = null;
            }

            if (playerData == null)
            {
                if (FusionManager.Instance != null && FusionManager.Instance.PlayerDataPrefab != null)
                {
                    NetworkObject dataObj = runner.Spawn(
                        FusionManager.Instance.PlayerDataPrefab,
                        Vector3.zero,
                        Quaternion.identity,
                        playerRef
                    );
                    
                    yield return null; 
                }
            }

            float waitTimer = 0f;
            while (GameManager.Instance != null && GameManager.Instance.GetPlayerData(playerRef, runner) == null && waitTimer < 2.0f)
            {
                yield return new WaitForSeconds(0.1f);
                waitTimer += 0.1f;
            }

            playerData = GameManager.Instance?.GetPlayerData(playerRef, runner);

            if (playerData == null || playerData.Object == null || !playerData.Object.IsValid)
            {
                Debug.LogError($"[Server] PlayerData missing for {playerRef}. Will attempt to spawn Character anyway.");
            }
            else
            {
                waitTimer = 0f;
                while (playerData.IsInitialized == false && waitTimer < 3.0f)
                {
                    yield return new WaitForSeconds(0.1f);
                    waitTimer += 0.1f;
                }

                if (!playerData.IsInitialized)
                {
                    Debug.LogWarning($"[Server] Timeout waiting for PlayerData init from {playerRef}. Using default values.");
                }
            }

            NetworkObject characterObj = runner.GetPlayerObject(playerRef);

            if (characterObj == null || !characterObj.IsValid)
            {
                if (playerData != null && playerData.PlayerInstance != null && playerData.PlayerInstance.IsValid)
                {
                    if (playerData.PlayerInstance.InputAuthority == playerRef)
                    {
                        characterObj = playerData.PlayerInstance;
                    }
                    else
                    {
                        Debug.LogWarning($"[Server] PlayerInstance for {playerRef} has incorrect InputAuthority ({playerData.PlayerInstance.InputAuthority}). Will respawn.");
                        characterObj = null;
                    }
                }
                
                if (characterObj == null || !characterObj.IsValid)
                {
                    Vector3 spawnPos = GetSceneSpawnPosition(playerRef.AsIndex);
                    
                    int charIndex = (playerData != null) ? playerData.CharacterIndex : 0;
                    
                    characterObj = runner.Spawn(
                        _playerPrefab,
                        spawnPos,
                        Quaternion.identity,
                        playerRef
                    );

                    yield return null;
                }
            }

            if (characterObj != null && characterObj.IsValid)
            {
                runner.SetPlayerObject(playerRef, characterObj);
                _spawnedPlayers[playerRef] = characterObj;

                if (playerData != null)
                {
                    playerData.PlayerInstance = characterObj;
                }

                var controller = characterObj.GetComponent<PlayerController>();
                if (controller != null)
                {
                    int charIndex = (playerData != null) ? playerData.CharacterIndex : 0;
                    controller.SetCharacterIndex(charIndex);
                }
            }
            else
            {
                Debug.LogError($"[Server] Failed to get valid Character object for {playerRef}");
            }
            
            yield return null;
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
    
    /// <summary>
    /// 클라이언트에서 이미 스폰된 플레이어를 찾아서 딕셔너리에 등록합니다.
    /// </summary>
    private void FindAndRegisterSpawnedPlayers(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning) return;
        
        int newlyRegistered = 0;
        
        foreach (var playerRef in runner.ActivePlayers)
        {
            if (playerRef == PlayerRef.None) continue;
            
            if (_spawnedPlayers.ContainsKey(playerRef)) continue;
            
            NetworkObject playerObj = runner.GetPlayerObject(playerRef);
            if (playerObj != null && playerObj.IsValid)
            {
                _spawnedPlayers[playerRef] = playerObj;
                newlyRegistered++;
                
                bool isLocalPlayer = playerRef == runner.LocalPlayer;
                
                if (isLocalPlayer && playerObj.InputAuthority != playerRef)
                {
                    Debug.LogWarning($"[MainGameManager] WARNING: Local player {playerRef} does not have InputAuthority! Expected: {playerRef}, Got: {playerObj.InputAuthority}");
                }
            }
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
        }
    }
    
    /// <summary>
    /// 중간에 난입한 플레이어 처리
    /// </summary>
    private void OnPlayerJoinedDuringGame(PlayerRef player, NetworkRunner runner)
    {
        if (SceneManager.GetActiveScene().name == "Main")
        {
            if (runner.IsServer)
            {
                StartCoroutine(Server_VerifyAndSpawnPlayers(runner));
            }
            else
            {
                StartCoroutine(WaitAndRegisterPlayer(runner, player));
            }
        }
    }
    
    /// <summary>
    /// 새로 참여한 플레이어가 스폰될 때까지 대기 후 등록합니다.
    /// </summary>
    private IEnumerator WaitAndRegisterPlayer(NetworkRunner runner, PlayerRef player)
    {
        int maxAttempts = 50;
        int attempts = 0;
        
        while (attempts < maxAttempts)
        {
            FindAndRegisterSpawnedPlayers(runner);
            
            if (_spawnedPlayers.ContainsKey(player))
            {
                yield break;
            }
            
            yield return new WaitForSeconds(0.1f);
            attempts++;
        }
        
        Debug.LogWarning($"[MainGameManager] Client: Timeout waiting for player {player} to spawn");
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
        NetworkObject networkObj = null;
        
        if (_spawnedPlayers.TryGetValue(localPlayerRef, out var playerObj) && playerObj != null && playerObj.IsValid)
        {
            networkObj = playerObj;
        }
        else
        {
            networkObj = runner.GetPlayerObject(localPlayerRef);
            
            if (networkObj != null && networkObj.IsValid)
            {
                _spawnedPlayers[localPlayerRef] = networkObj;
            }
            else
            {
                FindAndRegisterSpawnedPlayers(runner);
                
                if (_spawnedPlayers.TryGetValue(localPlayerRef, out playerObj) && playerObj != null && playerObj.IsValid)
                {
                    networkObj = playerObj;
                }
            }
        }
        
        if (networkObj != null && networkObj.IsValid)
        {
            if (networkObj.InputAuthority == localPlayerRef)
            {
                return networkObj.GetComponent<PlayerController>();
            }
            else
            {
                Debug.LogError($"[MainGameManager] CRITICAL: Local Player {localPlayerRef} found object ID {networkObj.Id} but it is controlled by {networkObj.InputAuthority}. ABORTING CONTROL.");
                
                if (_spawnedPlayers.ContainsKey(localPlayerRef) && _spawnedPlayers[localPlayerRef] == networkObj)
                {
                    _spawnedPlayers.Remove(localPlayerRef);
                }
                
                return null;
            }
        }
        
        if (_spawnedPlayers.ContainsKey(localPlayerRef))
        {
            var invalidObj = _spawnedPlayers[localPlayerRef];
            if (invalidObj == null || !invalidObj.IsValid)
            {
                _spawnedPlayers.Remove(localPlayerRef);
            }
        }

        return null;
    }
    
    /// <summary>
    /// 테스트 모드 전용: 현재 선택된 플레이어의 PlayerController를 가져옵니다.
    /// </summary>
    public PlayerController GetSelectedPlayer()
    {
        var playerObj = SelectedSlot == 0 ? _playerObj1 : _playerObj2;
        
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

        return _spawnedPlayers.Values
            .Select(obj => obj != null ? obj.GetComponent<PlayerController>() : null)
            .Where(controller => controller != null)
            .ToList();
    }
}

