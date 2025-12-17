using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using Fusion.Sockets;
using System;
using System.Threading.Tasks;

/// <summary>
/// Fusion NetworkRunner를 관리하고, 세션 시작 및 네트워크 콜백을 처리하는 중앙 매니저입니다.
/// </summary>
public class FusionManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static FusionManager Instance { get; private set; }
    public static NetworkRunner LocalRunner;

    [Header("Prefabs")]
    public NetworkPrefabRef PlayerDataPrefab;

    [Header("Events")]
    public static Action<PlayerRef, NetworkRunner> OnPlayerJoinedEvent;
    public static Action<PlayerRef, NetworkRunner> OnPlayerLeftEvent;
    public static Action<PlayerRef, NetworkRunner, int> OnPlayerChangeCharacterEvent;
    public static Action<NetworkRunner> OnShutdownEvent;
    public static Action<NetworkRunner> OnDisconnectedEvent;
    public static Action<NetworkRunner> OnSceneLoadDoneEvent;

    private GameManager _gameManager = null;

    /// <summary>
    /// 매니저 초기화 여부
    /// </summary>
    public bool IsInitialized { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    /// <summary>
    /// GameManager에 의해 호출되는 초기화 메서드입니다.
    /// </summary>
    public void OnInitialize(GameManager gameManager)
    {
        _gameManager = gameManager;
        IsInitialized = true;
    }

    /// <summary>
    /// 일반 게임 세션(멀티플레이)을 시작합니다.
    /// TitleGameManager 등에서 호출됩니다.
    /// </summary>
    /// <param name="mode">Host 또는 Client</param>
    /// <param name="sessionName">방 이름</param>
    /// <param name="sceneName">시작할 씬 이름 (기본값: Main)</param>
    public async Task StartGameSession(GameMode mode, string sessionName, string sceneName = "Main")
    {
        await StartSessionInternal(mode, sessionName, sceneName);
    }

    /// <summary>
    /// 테스트용 게임 세션(싱글플레이)을 시작합니다.
    /// 현재 활성화된 씬을 기반으로 즉시 시작됩니다.
    /// </summary>
    public async Task StartTestGameSession()
    {
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        await StartSessionInternal(GameMode.Single, "TestSession", null, currentSceneIndex);
    }

    /// <summary>
    /// 세션 시작을 위한 내부 공통 로직입니다.
    /// Runner 생성, 컴포넌트 부착, 씬 정보 설정 등을 수행합니다.
    /// </summary>
    private async Task StartSessionInternal(GameMode mode, string sessionName, string sceneName, int overrideSceneIndex = -1)
    {
        if (LocalRunner == null) LocalRunner = gameObject.AddComponent<NetworkRunner>();

        if (LocalRunner.IsRunning) await LocalRunner.Shutdown();

        var sceneManager = LocalRunner.GetComponent<NetworkSceneManagerDefault>();
        if (sceneManager == null) sceneManager = LocalRunner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        var objProvider = LocalRunner.GetComponent<NetworkObjectProviderDefault>();
        if (objProvider == null) objProvider = LocalRunner.gameObject.AddComponent<NetworkObjectProviderDefault>();

        LocalRunner.ProvideInput = true;
        var physicsSimulator = LocalRunner.GetComponent<Fusion.Addons.Physics.RunnerSimulatePhysics2D>();
        if (physicsSimulator == null) physicsSimulator = LocalRunner.gameObject.AddComponent<Fusion.Addons.Physics.RunnerSimulatePhysics2D>();
        physicsSimulator.ClientPhysicsSimulation = Fusion.Addons.Physics.ClientPhysicsSimulation.SimulateAlways;

        var sceneInfo = new NetworkSceneInfo();
        int sceneIndex = overrideSceneIndex;

        if (sceneIndex == -1 && !string.IsNullOrEmpty(sceneName))
        {
            sceneIndex = SceneUtility.GetBuildIndexByScenePath(sceneName);
            if (sceneIndex == -1) sceneIndex = SceneManager.GetActiveScene().buildIndex;
        }
        else if (sceneIndex == -1)
        {
            sceneIndex = SceneManager.GetActiveScene().buildIndex;
        }

        sceneInfo.AddSceneRef(SceneRef.FromIndex(sceneIndex), LoadSceneMode.Single);

        byte[] connectionToken = null;
        if (GameManager.Instance != null && !string.IsNullOrEmpty(GameManager.MyLocalNickname))
        {
            connectionToken = System.Text.Encoding.UTF8.GetBytes(GameManager.MyLocalNickname);
        }

        if (sceneIndex < 0)
        {
            Debug.LogError($"[FusionManager] Invalid Scene Index '{sceneIndex}'. The scene likely needs to be added to the Build Settings for Fusion to load it.");
            return;
        }

        Debug.Log($"[FusionManager] Starting session '{sessionName}' in mode '{mode}' with scene index {sceneIndex}.");

        StartGameResult result = null;

        try
        {
            result = await LocalRunner.StartGame(new StartGameArgs()
            {
                GameMode = mode,
                SessionName = sessionName,
                Scene = sceneInfo,
                SceneManager = sceneManager,
                ObjectProvider = objProvider,
                ConnectionToken = connectionToken,
                PlayerCount = 4
            });
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FusionManager] Exception during LocalRunner.StartGame: {e.Message}\n{e.StackTrace}");
            return;
        }

        if (result.Ok)
        {
            Debug.Log("[FusionManager] Session started successfully.");
        }
        else
        {
            Debug.LogError($"[FusionManager] Failed to start session: {result.ShutdownReason}");
        }

        Debug.Log("[FusionManager] Session started successfully.");
    }

    /// <summary>
    /// 플레이어가 세션에 입장했을 때 호출됩니다.
    /// 닉네임 동기화 및 이벤트 전파를 담당합니다.
    /// </summary>
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner.LocalPlayer == player) LocalRunner = runner;

        string playerNickname = $"Player_{player.AsIndex}";

        try
        {
            byte[] connectionToken = runner.GetPlayerConnectionToken(player);
            if (connectionToken != null && connectionToken.Length > 0)
            {
                playerNickname = System.Text.Encoding.UTF8.GetString(connectionToken);
            }
            else if (runner.LocalPlayer == player && GameManager.Instance != null)
            {
                string savedNick = GameManager.MyLocalNickname;
                if (!string.IsNullOrEmpty(savedNick)) playerNickname = savedNick;
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[FusionManager] Token Error: {e.Message}"); }

        bool isTestMode = _gameManager.CurrentSceneManager.IsTestMode;

        if (runner.IsServer && !isTestMode)
        {
            NetworkObject existingObj = runner.GetPlayerObject(player);
            if (existingObj != null && existingObj.TryGetComponent(out PlayerData existingPD))
            {
                SetNicknameToPlayerData(runner, player, playerNickname);
                OnPlayerJoinedEvent?.Invoke(player, runner);
                return;
            }

            PlayerData cachedData = GameManager.Instance?.GetPlayerData(player, runner);
            if (cachedData != null && cachedData.Object != null && cachedData.Object.IsValid)
            {
                SetNicknameToPlayerData(runner, player, playerNickname);
                OnPlayerJoinedEvent?.Invoke(player, runner);
                return;
            }

            PlayerData[] allPD = FindObjectsOfType<PlayerData>();
            foreach (var pd in allPD)
            {
                if (pd != null && pd.Object != null && pd.Object.IsValid && pd.Object.InputAuthority == player)
                {
                    SetNicknameToPlayerData(runner, player, playerNickname);
                    OnPlayerJoinedEvent?.Invoke(player, runner);
                    return;
                }
            }

            if (PlayerDataPrefab != null)
            {
                runner.Spawn(PlayerDataPrefab, inputAuthority: player, onBeforeSpawned: (runner, obj) =>
                {
                    if (obj.TryGetComponent(out PlayerData pd)) pd.SetNickname(playerNickname);
                });
            }
        }

        OnPlayerJoinedEvent?.Invoke(player, runner);
    }

    private void SetNicknameToPlayerData(NetworkRunner runner, PlayerRef player, string nickname)
    {
        PlayerData playerData = GameManager.Instance?.GetPlayerData(player, runner);
        if (playerData != null && playerData.Object != null && playerData.Object.IsValid)
        {
            playerData.SetNickname(nickname);
        }
    }

    /// <summary>
    /// 로컬 입력 처리 콜백입니다.
    /// </summary>
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        InputData currentInput = new InputData();

        currentInput.Buttons.Set(InputButton.LEFT, Input.GetKey(KeyCode.A));
        currentInput.Buttons.Set(InputButton.RIGHT, Input.GetKey(KeyCode.D));
        currentInput.Buttons.Set(InputButton.UP, Input.GetKey(KeyCode.W));
        currentInput.Buttons.Set(InputButton.DOWN, Input.GetKey(KeyCode.S));

        currentInput.MouseButtons.Set(InputMouseButton.LEFT, Input.GetMouseButton(0));
        currentInput.MouseButtons.Set(InputMouseButton.RIGHT, Input.GetMouseButton(1));

        currentInput.MouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        currentInput.MousePosition = Input.mousePosition;
        currentInput.MouseScroll = Input.mouseScrollDelta.y;

        if (_gameManager != null && _gameManager.CurrentSceneManager.IsTestMode)
        {
            currentInput.ControlledSlot = MainGameManager.SelectedSlot;
        }
        else
        {
            currentInput.ControlledSlot = 0;
        }

        input.Set(currentInput);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) => OnPlayerLeftEvent?.Invoke(player, runner);
    public void OnPlayerChangeCharacter(NetworkRunner runner, PlayerRef player, int characterIndex) => OnPlayerChangeCharacterEvent?.Invoke(player, runner, characterIndex);
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) => OnShutdownEvent?.Invoke(runner);
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) => OnDisconnectedEvent?.Invoke(runner);
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) => request.Accept();
    public void OnSceneLoadDone(NetworkRunner runner) => OnSceneLoadDoneEvent?.Invoke(runner);

    // 사용하지 않는 콜백 (인터페이스 구현용)
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
}