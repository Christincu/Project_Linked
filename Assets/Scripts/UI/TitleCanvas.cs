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

    [SerializeField] private TitleGameManager _titleGameManager;

    /// <summary>
    /// TitleGameManager 참조를 설정합니다. (씬 매니저가 초기화 시 호출)
    /// </summary>
    public void SetTitleGameManager(TitleGameManager titleGameManager)
    {
        _titleGameManager = titleGameManager;
    }

    public void OnInitialize(GameManager gameManager, GameDataManager gameDataManager)
    {
        ShowTitlePanel();
        InitializeChapterButtons();

        string savedNickname = GameManager.MyLocalNickname;
        if (string.IsNullOrEmpty(savedNickname))
        {
            savedNickname = "Player";
        }
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
                string displayName = $"Chapter : {sceneName}";
                chapterBtn.Initialize(sceneName, displayName, this);
            }
        }
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

    public void ShowTitlePanel()
    {
        _titlePanel.SetActive(true);
        _lobbyPanel.SetActive(false);
    }

    public void ShowLobbyPanel()
    {
        _titlePanel.SetActive(false);
        _lobbyPanel.SetActive(true);

        StartCoroutine(WaitForLocalPlayerAndRefreshUI());
    }
    
    /// <summary>
    /// [추가됨] 로컬 플레이어 데이터가 네트워크상에 스폰될 때까지 기다린 후 UI를 갱신합니다.
    /// </summary>
    private IEnumerator WaitForLocalPlayerAndRefreshUI()
    {
        // 1. Runner가 준비될 때까지 대기
        while (FusionManager.LocalRunner == null || !FusionManager.LocalRunner.IsRunning)
        {
            yield return null;
        }

        // 2. 로컬 플레이어의 PlayerData 객체가 찾아질 때까지 대기 (최대 5초)
        float timeout = 5f;
        float timer = 0f;

        while (timer < timeout)
        {
            if (GameManager.Instance != null)
            {
                // 로컬 플레이어의 데이터를 가져와 봅니다.
                var playerData = GameManager.Instance.GetPlayerData(FusionManager.LocalRunner.LocalPlayer, FusionManager.LocalRunner);
                
                // 데이터가 존재하고 유효하다면 루프 탈출
                if (playerData != null && playerData.Object != null && playerData.Object.IsValid)
                {
                    break;
                }
            }
            
            yield return new WaitForSeconds(0.1f); // 0.1초 간격으로 체크
            timer += 0.1f;
        }

        // 3. 데이터 준비 완료 (혹은 타임아웃) 후 UI 갱신 실행
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

    public void OnCreateRoomButton()
    {
        SetButtonsInteractable(false);

        string roomName = _roomNameInput.text;
        string playerNickname = _nicknameInput.text;

        _titleGameManager?.CreateRoom(roomName, playerNickname);
    }

    public void OnJoinRoomButton()
    {
        SetButtonsInteractable(false);

        string roomName = _roomNameInput.text;
        string playerNickname = _nicknameInput.text;

        _titleGameManager?.JoinRoom(roomName, playerNickname);
    }

    public void OnStartGameButton()
    {
        _titleGameManager?.StartGame();
    }

    public void OnLeaveRoomButton()
    {
        _titleGameManager?.LeaveRoom();
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

        _titleGameManager.LoadChapterScene(sceneName);
    }

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

        GameManager.MyLocalCharacterIndex = characterIndex;

        UpdateCharacterUI(characterData);
        if (FusionManager.LocalRunner != null)
        {
            // PlayerData가 생성될 때까지 대기하는 코루틴 시작
            StartCoroutine(SetCharacterIndexWhenPlayerDataReady(characterIndex));
        }
    }
    
    /// <summary>
    /// PlayerData가 생성될 때까지 대기한 후 캐릭터 인덱스를 설정합니다.
    /// </summary>
    private IEnumerator SetCharacterIndexWhenPlayerDataReady(int characterIndex)
    {
        if (FusionManager.LocalRunner == null) yield break;
        
        var localPlayerRef = FusionManager.LocalRunner.LocalPlayer;
        float timeout = 5f;
        float timer = 0f;
        
        while (timer < timeout)
        {
            var playerData = GameManager.Instance?.GetPlayerData(localPlayerRef, FusionManager.LocalRunner);
            if (playerData != null && playerData.Object != null && playerData.Object.IsValid)
            {
                playerData.SetCharacterIndex(characterIndex);
                Debug.Log($"[TitleCanvas] Character index set to {characterIndex} for player {localPlayerRef}");
                yield break;
            }
            
            yield return new WaitForSeconds(0.1f);
            timer += 0.1f;
        }
        
        Debug.LogWarning($"[TitleCanvas] PlayerData not found for local player {localPlayerRef} after timeout");
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
            string roomName = _titleGameManager?.RoomName ?? "Unknown";
            _roomNameText.text = $"Room: {roomName}";
            _playerListText.text = _titleGameManager != null && _titleGameManager.IsConnecting ? "Connecting..." : "Waiting for connection...";

            if (_startGameButton != null)
            {
                _startGameButton.interactable = false;
            }
            
            UpdateChapterButtonsInteractable();
        }
    }

    public void SetButtonsInteractable(bool interactable)
    {
        if (_createRoomButton != null)
            _createRoomButton.interactable = interactable;
        if (_joinRoomButton != null)
            _joinRoomButton.interactable = interactable;
    }
}
