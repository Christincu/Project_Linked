using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;

public class TitleCanvas : MonoBehaviour, ICanvas
{
    [Header("UI Panels")]
    [SerializeField] private GameObject _titlePanel;
    [SerializeField] private GameObject _lobbyPanel;

    [Header("Lobby UI")]
    [SerializeField] private TextMeshProUGUI _playerListText;
    [SerializeField] private TextMeshProUGUI _roomNameText;
    [SerializeField] private Button _startGameButton;
    [SerializeField] private Button _leaveRoomButton;

    [Header("Room Setup UI")]
    [SerializeField] private TMP_InputField _nicknameInput;
    [SerializeField] private TMP_InputField _roomNameInput;
    [SerializeField] private Button _createRoomButton;
    [SerializeField] private Button _joinRoomButton;

    [Header("Character Selection UI")]
    [SerializeField] private TextMeshProUGUI _characterNameText;
    [SerializeField] private TextMeshProUGUI _characterDescriptionText;

    [Header("Chapter UI")]
    [SerializeField] private TextMeshProUGUI _chapterTitleText;
    [SerializeField] private GameObject _chapterBtnPrefab;
    [SerializeField] private Transform _chapterScrollViewContent;
    [SerializeField] private List<string> _chapterNames;

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
        ShowTitlePanel();
        TitleGameManager.Instance?.Initialize(this);
        SetupButtonEvents();
        InitializeChapterButtons();

        string savedNickname = PlayerPrefs.GetString("PlayerNick", "Player");
        _nicknameInput.text = savedNickname;
    }
    
    /// <summary>
    /// 챕터 버튼들을 생성합니다.
    /// </summary>
    private void InitializeChapterButtons()
    {
        if (_chapterBtnPrefab == null || _chapterScrollViewContent == null)
        {
            Debug.LogWarning("[TitleCanvas] Chapter button prefab or scroll content is not set!");
            return;
        }
        
        ClearChapterButtons();
        
        for (int i = 0; i < _chapterNames.Count; i++)
        {
            string sceneName = _chapterNames[i];
            GameObject btnObj = Instantiate(_chapterBtnPrefab, _chapterScrollViewContent);
            btnObj.name = $"ChapterBtn_{i}_{sceneName}";
            
            ChapterBtn chapterBtn = btnObj.GetComponent<ChapterBtn>();
            if (chapterBtn != null)
            {
                string displayName = $"Chapter {i + 1}: {sceneName}";
                chapterBtn.Initialize(sceneName, displayName, this);
            }
        }
        
        Debug.Log($"[TitleCanvas] Created {_chapterNames.Count} chapter buttons");
    }
    
    /// <summary>
    /// 기존 챕터 버튼들을 제거합니다.
    /// </summary>
    private void ClearChapterButtons()
    {
        if (_chapterScrollViewContent == null) return;
        
        foreach (Transform child in _chapterScrollViewContent)
        {
            Destroy(child.gameObject);
        }
    }

    // Auto assign button events
    private void SetupButtonEvents()
    {
        RemoveButtonEvents();

        if (_createRoomButton != null)
            _createRoomButton.onClick.AddListener(OnCreateRoomButton);

        if (_joinRoomButton != null)
            _joinRoomButton.onClick.AddListener(OnJoinRoomButton);

        if (_startGameButton != null)
            _startGameButton.onClick.AddListener(OnStartGameButton);

        if (_leaveRoomButton != null)
            _leaveRoomButton.onClick.AddListener(OnLeaveRoomButton);
    }

    void OnDestroy()
    {
        RemoveButtonEvents();
    }

    // Remove button event connections
    private void RemoveButtonEvents()
    {
        if (_createRoomButton != null)
            _createRoomButton.onClick.RemoveListener(OnCreateRoomButton);

        if (_joinRoomButton != null)
            _joinRoomButton.onClick.RemoveListener(OnJoinRoomButton);

        if (_startGameButton != null)
            _startGameButton.onClick.RemoveListener(OnStartGameButton);

        if (_leaveRoomButton != null)
            _leaveRoomButton.onClick.RemoveListener(OnLeaveRoomButton);
    }

    // ========== UI Panel Switching ==========

    public void ShowTitlePanel()
    {
        _titlePanel.SetActive(true);
        _lobbyPanel.SetActive(false);
    }

    public void ShowLobbyPanel()
    {
        _titlePanel.SetActive(false);
        _lobbyPanel.SetActive(true);

        InitializeLocalPlayerCharacterUI();
        UpdateChapterButtonsInteractable();
        UpdateLobbyUI();
    }
    
    /// <summary>
    /// 챕터 버튼들의 활성화 상태를 업데이트합니다. (호스트만 활성화)
    /// </summary>
    private void UpdateChapterButtonsInteractable()
    {
        if (_chapterScrollViewContent == null) return;
        
        bool isHost = FusionManager.LocalRunner != null && FusionManager.LocalRunner.IsServer;
        
        foreach (Transform child in _chapterScrollViewContent)
        {
            Button btn = child.GetComponent<Button>();
            if (btn != null)
            {
                btn.interactable = isHost;
            }
        }
    }

    // ========== Button Events ==========

    public void OnCreateRoomButton()
    {
        // 버튼 즉시 비활성화 (중복 클릭 방지)
        SetButtonsInteractable(false);

        string roomName = _roomNameInput.text;
        string playerNickname = _nicknameInput.text;

        TitleGameManager.Instance?.CreateRoom(roomName, playerNickname);
    }

    public void OnJoinRoomButton()
    {
        // 버튼 즉시 비활성화 (중복 클릭 방지)
        SetButtonsInteractable(false);

        string roomName = _roomNameInput.text;
        string playerNickname = _nicknameInput.text;

        TitleGameManager.Instance?.JoinRoom(roomName, playerNickname);
    }

    public void OnStartGameButton()
    {
        TitleGameManager.Instance?.StartGame();
    }

    public void OnLeaveRoomButton()
    {
        TitleGameManager.Instance?.LeaveRoom();
    }

    public void OnExitGameButton()
    {
        GameManager.Instance?.ExitGame();
    }
    
    /// <summary>
    /// 챕터가 선택되었을 때 호출됩니다.
    /// </summary>
    public void OnChapterSelected(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[TitleCanvas] Scene name is empty!");
            return;
        }
        
        Debug.Log($"[TitleCanvas] Chapter selected: {sceneName}");
        TitleGameManager.Instance?.LoadChapterScene(sceneName);
    }

    // ========== Character Selection Functions ==========

    public void OnSelectCharacter0()
    {
        SelectCharacter(0);
    }

    public void OnSelectCharacter1()
    {
        SelectCharacter(1);
    }

    public void OnSelectCharacter2()
    {
        SelectCharacter(2);
    }

    private void SelectCharacter(int characterIndex)
    {
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

        UpdateCharacterUI(characterData);

        // Set character index in network if connected
        if (FusionManager.LocalRunner != null)
        {
            var playerData = GameManager.Instance?.GetPlayerData(FusionManager.LocalRunner.LocalPlayer, FusionManager.LocalRunner);
            if (playerData != null)
            {
                playerData.SetCharacterIndex(characterIndex);
            }
            else
            {
                Debug.LogWarning("PlayerData not found for local player");
            }
        }
    }

    public void UpdateCharacterUI(CharacterData characterData)
    {
        if (characterData == null) return;

        if (_characterNameText != null)
        {
            _characterNameText.text = characterData.characterName;
        }

        if (_characterDescriptionText != null)
        {
            _characterDescriptionText.text = characterData.description;
        }
    }

    private void InitializeLocalPlayerCharacterUI()
    {
        if (FusionManager.LocalRunner != null && GameManager.Instance != null && GameDataManager.Instance != null)
        {
            var playerData = GameManager.Instance?.GetPlayerData(FusionManager.LocalRunner.LocalPlayer, FusionManager.LocalRunner);
            if (playerData != null)
            {
                var characterData = GameDataManager.Instance?.CharacterService.GetCharacter(playerData.CharacterIndex);
                if (characterData != null)
                {
                    UpdateCharacterUI(characterData);
                }
            }
        }
    }

    // ========== UI Update (public for TitleGameManager access) ==========

    public void UpdateLobbyUI()
    {
        if (FusionManager.LocalRunner != null)
        {
            _roomNameText.text = $"Room: {FusionManager.LocalRunner.SessionInfo.Name}";

            bool isHost = FusionManager.LocalRunner.IsServer;
            
            if (_startGameButton != null)
            {
                _startGameButton.interactable = isHost;
            }
            
            UpdateChapterButtonsInteractable();

            string playerList = "";
            foreach (var player in FusionManager.LocalRunner.ActivePlayers)
            {
                var playerData = GameManager.Instance?.GetPlayerData(player, FusionManager.LocalRunner);
                string nick = playerData?.Nick.ToString() ?? $"Player_{player.AsIndex}";

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
                string isHostTag = (player == FusionManager.LocalRunner.LocalPlayer && isHost) ? " [Host]" : "";
                playerList += $"{nick} - {characterName}{isLocal}{isHostTag}\n";
            }
            _playerListText.text = playerList;
        }
        else
        {
            string roomName = TitleGameManager.Instance?.RoomName ?? "Unknown";
            _roomNameText.text = $"Room: {roomName}";
            _playerListText.text = TitleGameManager.Instance.IsConnecting ? "Connecting..." : "Waiting for connection...";

            if (_startGameButton != null)
            {
                _startGameButton.interactable = false;
            }
            
            UpdateChapterButtonsInteractable();
        }
    }

    // ========== Helper Functions ==========

    // 버튼 활성화/비활성화 설정 (public for TitleGameManager access)
    public void SetButtonsInteractable(bool interactable)
    {
        if (_createRoomButton != null)
            _createRoomButton.interactable = interactable;
        if (_joinRoomButton != null)
            _joinRoomButton.interactable = interactable;

        // 로비 패널 진입 후에는 startGameButton과 leaveRoomButton은 UpdateLobbyUI에서 관리
    }
}
