using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;

public class InputController : NetworkBehaviour, INetworkRunnerCallbacks
{
    [Networked]
    private NetworkButtons _prevData { get; set; }
    public NetworkButtons PrevButtons { get => _prevData; set => _prevData = value; }

    public override void Spawned()
    {
        if (Object.HasInputAuthority)
        {
            Runner.AddCallbacks(this);
        }
    }

    /// <summary>
    /// Update: 로컬 입력만 처리 (RPC용)
    /// R 키 입력을 감지하면 서버에 RPC 전송
    /// </summary>
    private void Update()
    {
        if (Object != null && Object.HasInputAuthority && Runner != null && Runner.IsRunning)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {

            }
        }
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        InputData currentInput = new InputData();

        // NetworkButtons는 인덱스 기반 (0..4)
        currentInput.Buttons.Set((int)InputButton.LEFT, Input.GetKey(KeyCode.A));
        currentInput.Buttons.Set((int)InputButton.RIGHT, Input.GetKey(KeyCode.D));
        currentInput.Buttons.Set((int)InputButton.UP, Input.GetKey(KeyCode.W));
        currentInput.Buttons.Set((int)InputButton.DOWN, Input.GetKey(KeyCode.S));

        // Mouse buttons
        currentInput.MouseButtons.Set((int)InputMouseButton.LEFT, Input.GetMouseButton(0));
        currentInput.MouseButtons.Set((int)InputMouseButton.RIGHT, Input.GetMouseButton(1));

        // Mouse movement deltas and absolute position
        currentInput.MouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        currentInput.MousePosition = Input.mousePosition;
        currentInput.MouseScroll = Input.mouseScrollDelta.y;

        // 현재 선택된 슬롯 반영 (TestGameManager의 SelectedSlot 사용)
        currentInput.ControlledSlot = TestGameManager.SelectedSlot;

        input.Set(currentInput);
    }

    #region UnusedCallbacks
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
    }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
    }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
    }

    #endregion
}
