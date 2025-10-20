using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;

public class TestGameManager : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private NetworkPrefabRef PlayerPrefab;
    [SerializeField] private NetworkPrefabRef PlayerDataPrefab;

    [Header("Spawn Settings")] 
    [SerializeField] private Vector2 firstPlayerPos = new Vector2(-2, 0);
    [SerializeField] private Vector2 secondPlayerPos = new Vector2(2, 0);
    [SerializeField] private int firstCharacterIndex = 0;
    [SerializeField] private int secondCharacterIndex = 0;

    private NetworkRunner _runner;
    public static int SelectedSlot = 0; // 0: 첫째, 1: 둘째
    private NetworkObject _playerObj1;
    private NetworkObject _playerObj2;

    async void Start()
    {
        // 씬 단독 테스트를 위해 기존 러너가 없으면 생성
        if (FusionManager.LocalRunner == null)
        {
            GameObject go = new GameObject("TestRunner");
            _runner = go.AddComponent<NetworkRunner>();
        }
        else
        {
            _runner = FusionManager.LocalRunner;
        }

        // 러너가 아직 실행 중이 아니면 호스트로 시작
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

            FusionManager.LocalRunner = _runner; // 공유
        }

        // 서버에서만 스폰 처리
        if (_runner.IsServer)
        {
            SpawnTestPlayers();
        }
    }

    private void SpawnTestPlayers()
    {
        // 1) 로컬 플레이어용 데이터/오브젝트
        var localPlayer = _runner.LocalPlayer;

        if (PlayerDataPrefab.IsValid)
        {
            _runner.Spawn(PlayerDataPrefab, inputAuthority: localPlayer);
        }

        _playerObj1 = _runner.Spawn(PlayerPrefab, firstPlayerPos, Quaternion.identity, localPlayer);
        var controller1 = _playerObj1 != null ? _playerObj1.GetComponent<PlayerController>() : null;
        if (controller1 != null)
        {
            controller1.SetCharacterIndex(firstCharacterIndex);
            controller1.PlayerSlot = 0;
        }

        // 2) 두 번째 플레이어도 로컬 입력으로 전환 테스트가 가능하도록 로컬 inputAuthority 부여
        if (PlayerDataPrefab.IsValid)
        {
            _runner.Spawn(PlayerDataPrefab, inputAuthority: localPlayer);
        }

        _playerObj2 = _runner.Spawn(PlayerPrefab, secondPlayerPos, Quaternion.identity, localPlayer);
        var controller2 = _playerObj2 != null ? _playerObj2.GetComponent<PlayerController>() : null;
        if (controller2 != null)
        {
            controller2.SetCharacterIndex(secondCharacterIndex);
            controller2.PlayerSlot = 1;
        }
    }

    void Update()
    {
        // 키 1/2로 조종 대상 전환
        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectedSlot = 0;
        if (Input.GetKeyDown(KeyCode.Alpha2)) SelectedSlot = 1;
    }
}
