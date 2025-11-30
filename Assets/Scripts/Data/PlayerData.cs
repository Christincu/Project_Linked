using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class PlayerData : NetworkBehaviour
{
    // Network synchronized player nickname (max 16 characters)
    [Networked]
    public NetworkString<_16> Nick { get; set; }
    
    // Player's actual game object reference (씬 전환 후에도 유지)
    [Networked]
    public NetworkObject Instance { get; set; }
    
    // [호환성] PlayerInstance는 Instance의 별칭 (기존 코드 호환성)
    public NetworkObject PlayerInstance 
    { 
        get => Instance; 
        set => Instance = value; 
    }
    
    // Player ready state
    [Networked]
    public bool IsReady { get; set; }
    
    // Player's selected character index (0, 1, 2)
    [Networked]
    public int CharacterIndex { get; set; }
    
    // [추가] 데이터 초기화가 완료되었는지 확인하는 플래그
    [Networked]
    public NetworkBool IsInitialized { get; set; }
    
    // Event when player data is spawned
    public static System.Action<PlayerRef, NetworkRunner> OnPlayerDataSpawned;
    
    // Network synchronization detector
    private ChangeDetector _changeDetector;
    
    // RPC: Set nickname (player requests to server)
    [Rpc(sources: RpcSources.InputAuthority, targets: RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_SetNick(string nick)
    {
        Nick = nick;
    }
    
    // RPC: Set ready state
    [Rpc(sources: RpcSources.InputAuthority, targets: RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_SetReady(bool ready)
    {
        IsReady = ready;
    }
    
    // RPC: Set character index
    [Rpc(sources: RpcSources.InputAuthority, targets: RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_SetCharacterIndex(int characterIndex)
    {
        CharacterIndex = characterIndex;
        FusionManager.OnPlayerChangeCharacterEvent?.Invoke(Object.InputAuthority, Runner, characterIndex);
    }
    
    // [변경] 닉네임과 캐릭터 인덱스를 한 번에 설정하고 초기화 완료 도장을 찍는 RPC
    [Rpc(sources: RpcSources.InputAuthority, targets: RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_SetInitData(string nick, int charIndex)
    {
        Nick = nick;
        CharacterIndex = charIndex;
        IsInitialized = true;
        
        FusionManager.OnPlayerChangeCharacterEvent?.Invoke(Object.InputAuthority, Runner, charIndex);
    }
    
    // Called when network object is created
    public override void Spawned()
    {
        // Initialize change detector
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState, false);
        
        // [수정] DontDestroyOnLoad로 설정하여 씬 전환 시에도 유지
        DontDestroyOnLoad(gameObject);
        
        // [수정] Runner에 PlayerObject로 등록 (씬 전환 후에도 찾을 수 있도록)
        if (Runner != null)
        {
            Runner.SetPlayerObject(Object.InputAuthority, Object);
        }
        
        // ===============================================================
        // [핵심 수정] InputAuthority(이 캐릭터의 주인)만 실행하는 로직
        // ===============================================================
        if (Object.HasInputAuthority)
        {
            // PlayerPrefs에서 닉네임 가져오기 (씬 전환 후에도 유지)
            string savedNick = PlayerPrefs.GetString("Nick", string.Empty);
            if (string.IsNullOrEmpty(savedNick))
            {
                savedNick = GameManager.MyLocalNickname;
            }
            if (string.IsNullOrEmpty(savedNick))
            {
                savedNick = $"Player_{Object.InputAuthority.AsIndex}";
            }
            
            int savedCharIndex = GameManager.MyLocalCharacterIndex;
            
            RPC_SetInitData(savedNick, savedCharIndex);
        }
        else
        {
            // 주인이 아닌 경우(다른 사람 화면), 이미 동기화된 데이터가 있다면 이벤트를 발생시켜 UI를 갱신해줍니다.
            // (늦게 들어온 사람을 위해)
            if (!string.IsNullOrEmpty(Nick.ToString()))
            {
                // 필요하다면 여기서 초기 UI 갱신 이벤트 호출 가능
            }
        }
        
        // [수정] StateAuthority에서만 GameManager에 등록
        if (Object.HasStateAuthority && GameManager.Instance != null)
        {
            GameManager.Instance.SetPlayerData(Object.InputAuthority, this);
        }
        
        // [수정] Instance가 null이면 자기 자신을 설정
        if (Instance == null)
        {
            Instance = Object;
        }
        
        // Trigger event
        OnPlayerDataSpawned?.Invoke(Object.InputAuthority, Runner);
    }
    
    // [수정] Despawned 추가 (오브젝트 파괴 시 정리)
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
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
                case nameof(CharacterIndex):
                    // Trigger event when character index changes
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
            GameManager.MyLocalNickname = nickname;
            RPC_SetNick(nickname);
        }
    }
    
    // Set character index
    public void SetCharacterIndex(int characterIndex)
    {
        if (Object.HasInputAuthority)
        {
            GameManager.MyLocalCharacterIndex = characterIndex;
            RPC_SetCharacterIndex(characterIndex);
        }
    }
}