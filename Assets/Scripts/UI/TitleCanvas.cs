using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;

public class TitleCanvas : MonoBehaviour, ICanvas
{
    [Header("UI Panels")]
    [SerializeField] private GameObject titlePanel;      // Title screen
    [SerializeField] private GameObject lobbyPanel;    // Lobby screen
    
    [Header("Lobby UI")]
    [SerializeField] private TextMeshProUGUI playerListText;    // Player list
    [SerializeField] private TextMeshProUGUI roomNameText;     // Room name
    [SerializeField] private Button startGameButton;            // Start game button
    [SerializeField] private Button leaveRoomButton;            // Leave room button
    
    [Header("Room Setup UI")]
    [SerializeField] private TMP_InputField nicknameInput;     // Nickname input
    [SerializeField] private TMP_InputField roomNameInput;     // Room name input
    [SerializeField] private Button createRoomButton;          // Create room button
    [SerializeField] private Button joinRoomButton;            // Join room button
    
    [Header("Character Selection UI")]
    [SerializeField] private TextMeshProUGUI characterNameText;     // Character name text
    [SerializeField] private TextMeshProUGUI characterDescriptionText;     // Character description text

    public Transform CanvasTransform => transform;
    
    void Start()
    {
        // Create TitleGameManager if it doesn't exist
        if (TitleGameManager.Instance == null)
        {
            GameObject titleGameManagerObj = new GameObject("TitleGameManager");
            titleGameManagerObj.AddComponent<TitleGameManager>();
        }
    }

    public void Initialize(GameManager gameManager, GameDataManager gameDataManager)
    {
        // Initial UI setup
        ShowTitlePanel();
        
        // Initialize TitleGameManager
        TitleGameManager.Instance?.Initialize(this);
        
        // Auto assign button events
        SetupButtonEvents();
        
        // Load saved nickname
        string savedNickname = PlayerPrefs.GetString("PlayerNick", "Player");
        nicknameInput.text = savedNickname;
    }
    
    // Auto assign button events
    private void SetupButtonEvents()
    {
        // 중복 등록 방지를 위해 먼저 제거
        RemoveButtonEvents();
        
        // Create room button
        if (createRoomButton != null)
            createRoomButton.onClick.AddListener(OnCreateRoomButton);
        
        // Join room button
        if (joinRoomButton != null)
            joinRoomButton.onClick.AddListener(OnJoinRoomButton);
        
        // Start game button
        if (startGameButton != null)
            startGameButton.onClick.AddListener(OnStartGameButton);
        
        // Leave room button
        if (leaveRoomButton != null)
            leaveRoomButton.onClick.AddListener(OnLeaveRoomButton);
    }
    
    void OnDestroy()
    {
        // Remove button event connections
        RemoveButtonEvents();
    }
    
    // Remove button event connections
    private void RemoveButtonEvents()
    {
        if (createRoomButton != null)
            createRoomButton.onClick.RemoveListener(OnCreateRoomButton);
        
        if (joinRoomButton != null)
            joinRoomButton.onClick.RemoveListener(OnJoinRoomButton);
        
        if (startGameButton != null)
            startGameButton.onClick.RemoveListener(OnStartGameButton);
        
        if (leaveRoomButton != null)
            leaveRoomButton.onClick.RemoveListener(OnLeaveRoomButton);
    }
    
    // ========== UI Panel Switching ==========
    
    public void ShowTitlePanel()
    {
        titlePanel.SetActive(true);
        lobbyPanel.SetActive(false);
    }
    
    public void ShowLobbyPanel()
    {
        titlePanel.SetActive(false);
        lobbyPanel.SetActive(true);
        
        // Initialize character UI for local player
        InitializeLocalPlayerCharacterUI();
        
        // Immediate UI update (show room name before connection)
        UpdateLobbyUI();
    }
    
    // ========== Button Events ==========
    
    // Create room button
    public void OnCreateRoomButton()
    {
        // 버튼 즉시 비활성화 (중복 클릭 방지)
        if (createRoomButton != null)
            createRoomButton.interactable = false;
        
        string roomName = roomNameInput.text;
        string playerNickname = nicknameInput.text;
        
        TitleGameManager.Instance?.CreateRoom(roomName, playerNickname);
    }
    
    // Join room button
    public void OnJoinRoomButton()
    {
        // 버튼 즉시 비활성화 (중복 클릭 방지)
        if (joinRoomButton != null)
            joinRoomButton.interactable = false;
        
        string roomName = roomNameInput.text;
        string playerNickname = nicknameInput.text;
        
        TitleGameManager.Instance?.JoinRoom(roomName, playerNickname);
    }
    
    // Start game button (host only)
    public void OnStartGameButton()
    {
        TitleGameManager.Instance?.StartGame();
    }
    
    // Leave room button
    public void OnLeaveRoomButton()
    {
        TitleGameManager.Instance?.LeaveRoom();
    }
    
    // Exit game button
    public void OnExitGameButton()
    {
        GameManager.Instance?.ExitGame();
    }
    
    // ========== Character Selection Functions ==========
    
    // Select character 0
    public void OnSelectCharacter0()
    {
        SelectCharacter(0);
    }
    
    // Select character 1
    public void OnSelectCharacter1()
    {
        SelectCharacter(1);
    }
    
    // Select character 2
    public void OnSelectCharacter2()
    {
        SelectCharacter(2);
    }
    
    // Common character selection logic
    private void SelectCharacter(int characterIndex)
    {
        // Get character data from GameDataManager
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("GameDataManager instance not found");
            return;
        }
        
        CharacterData characterData = GameDataManager.Instance.CharacterService.GetCharacter(characterIndex);
        if (characterData == null)
        {
            Debug.LogWarning($"Character data not found for index: {characterIndex}");
            return;
        }
        
        // Update UI with character data
        UpdateCharacterUI(characterData);
        
        // Set character index in network if connected
        if (FusionManager.LocalRunner != null)
        {
            var playerData = GameManager.Instance?.GetPlayerData(FusionManager.LocalRunner.LocalPlayer, FusionManager.LocalRunner);
            if (playerData != null)
            {
                playerData.SetCharacterIndex(characterIndex);
                Debug.Log($"Character '{characterData.characterName}' (index: {characterIndex}) selected");
            }
            else
            {
                Debug.LogWarning("PlayerData not found for local player");
            }
        }
        else
        {
            Debug.Log($"Character '{characterData.characterName}' (index: {characterIndex}) selected (not connected yet)");
        }
    }
    
    // Update character UI elements (public for TitleGameManager access)
    public void UpdateCharacterUI(CharacterData characterData)
    {
        if (characterData == null) return;
        
        // Update character name text
        if (characterNameText != null)
        {
            characterNameText.text = characterData.characterName;
        }
        
        // Update character description text
        if (characterDescriptionText != null)
        {
            characterDescriptionText.text = characterData.description;
        }
    }
    
    // Initialize local player's character UI when entering lobby
    private void InitializeLocalPlayerCharacterUI()
    {
        if (FusionManager.LocalRunner != null && GameManager.Instance != null && GameDataManager.Instance != null)
        {
            var playerData = GameManager.Instance.GetPlayerData(FusionManager.LocalRunner.LocalPlayer, FusionManager.LocalRunner);
            if (playerData != null)
            {
                var characterData = GameDataManager.Instance.CharacterService.GetCharacter(playerData.CharacterIndex);
                if (characterData != null)
                {
                    UpdateCharacterUI(characterData);
                    Debug.Log($"Initialized character UI with '{characterData.characterName}'");
                }
            }
        }
    }
    
    // ========== UI Update (public for TitleGameManager access) ==========
    
    public void UpdateLobbyUI()
    {
        Debug.Log($"UpdateLobbyUI - FusionManager.LocalRunner: {FusionManager.LocalRunner}");
        
        // Show room name (even before network connection)
        if (FusionManager.LocalRunner != null)
        {
            Debug.Log($"LocalRunner found - ActivePlayers: {FusionManager.LocalRunner.ActivePlayers}");
            
            // When connected: use session name
            roomNameText.text = $"Room: {FusionManager.LocalRunner.SessionInfo.Name}";
            
            // Update player list
            string playerList = "";
            foreach (var player in FusionManager.LocalRunner.ActivePlayers)
            {
                var playerData = GameManager.Instance?.GetPlayerData(player, FusionManager.LocalRunner);
                string nick = playerData?.Nick.ToString() ?? $"Player_{player.AsIndex}";
                
                // Get character name
                string characterName = "No Character";
                if (playerData != null && GameDataManager.Instance != null)
                {
                    var characterData = GameDataManager.Instance.CharacterService.GetCharacter(playerData.CharacterIndex);
                    if (characterData != null)
                    {
                        characterName = characterData.characterName;
                    }
                }
                
                string isLocal = (player == FusionManager.LocalRunner.LocalPlayer) ? " (You)" : "";
                string isHost = (player == FusionManager.LocalRunner.LocalPlayer && FusionManager.LocalRunner.IsServer) ? " [Host]" : "";
                playerList += $"{nick} - {characterName}{isLocal}{isHost}\n";
            }
            playerListText.text = playerList;
        }
        else
        {
            Debug.Log("LocalRunner is null - showing connecting...");
            // Before connection: use local variable
            string roomName = TitleGameManager.Instance?.RoomName ?? "Unknown";
            roomNameText.text = $"Room: {roomName}";
            playerListText.text = "Connecting...";
        }
    }
    
    // ========== Helper Functions ==========
    
    // 버튼 활성화/비활성화 설정 (public for TitleGameManager access)
    public void SetButtonsInteractable(bool interactable)
    {
        if (createRoomButton != null)
            createRoomButton.interactable = interactable;
        if (joinRoomButton != null)
            joinRoomButton.interactable = interactable;
    }
}
