using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class PlayerData : NetworkBehaviour
{
    // Network synchronized player nickname (max 16 characters)
    [Networked]
    public NetworkString<_16> Nick { get; set; }
    
    // Player's actual game object reference
    [Networked]
    public NetworkObject PlayerInstance { get; set; }
    
    // Player ready state
    [Networked]
    public bool IsReady { get; set; }
    
    // Event when player data is spawned
    public static System.Action<PlayerRef, NetworkRunner> OnPlayerDataSpawned;
    
    // Network synchronization detector
    private ChangeDetector _changeDetector;
    
    // RPC: Set nickname (player requests to server)
    [Rpc(sources: RpcSources.InputAuthority, targets: RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_SetNick(string nick)
    {
        Nick = nick;
        Debug.Log($"Player nickname set: {nick}");
    }
    
    // RPC: Set ready state
    [Rpc(sources: RpcSources.InputAuthority, targets: RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_SetReady(bool ready)
    {
        IsReady = ready;
        Debug.Log($"Player ready state: {ready}");
    }
    
    // Called when network object is created
    public override void Spawned()
    {
        // Initialize change detector
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState, false);
        
        // Set nickname for local player
        if (Object.HasInputAuthority)
        {
            // Get saved nickname (or use default)
            string savedNick = PlayerPrefs.GetString("PlayerNick", "");
            if (string.IsNullOrEmpty(savedNick))
            {
                savedNick = $"Player_{Object.InputAuthority.AsIndex}";
            }
            
            // Send nickname to server
            RPC_SetNick(savedNick);
        }
        
        // Persist through scene transitions
        DontDestroyOnLoad(gameObject);
        
        // Register player object with runner
        Runner.SetPlayerObject(Object.InputAuthority, Object);
        
        // Trigger event
        OnPlayerDataSpawned?.Invoke(Object.InputAuthority, Runner);
        
        // Register with GameManager if server
        if (Object.HasStateAuthority)
        {
            GameManager.Instance?.SetPlayerData(Object.InputAuthority, this);
        }
    }
    
    // Called every frame (detect changes)
    public override void Render()
    {
        // Check for changes
        foreach (var change in _changeDetector.DetectChanges(this))
        {
            switch (change)
            {
                case nameof(Nick):
                    // Trigger event when nickname changes
                    OnPlayerDataSpawned?.Invoke(Object.InputAuthority, Runner);
                    break;
                case nameof(IsReady):
                    // Trigger event when ready state changes
                    OnPlayerDataSpawned?.Invoke(Object.InputAuthority, Runner);
                    break;
            }
        }
    }
    
    // Toggle player ready state
    public void ToggleReady()
    {
        if (Object.HasInputAuthority)
        {
            RPC_SetReady(!IsReady);
        }
    }
    
    // Set nickname
    public void SetNickname(string nickname)
    {
        if (Object.HasInputAuthority)
        {
            RPC_SetNick(nickname);
            // Save locally
            PlayerPrefs.SetString("PlayerNick", nickname);
        }
    }
}