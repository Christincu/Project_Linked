using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;

public class MainCanvas : MonoBehaviour, ICanvas
{
    #region [References]
    [Header("Magic Selection UI")]
    [SerializeField] private GameObject _magicSelectBtnPrefab;
    [SerializeField] private Transform _magicSelectBtnContentTransform;
    [SerializeField] private Image _localMagicIcon1; // 이름 변경: 명확성 확보
    [SerializeField] private Image _localMagicIcon2;
    [SerializeField] private Image _otherPlayerIcon;
    [SerializeField] private Image _otherPlayerMagicIcon1;
    [SerializeField] private Image _otherPlayerMagicIcon2;

    [Header("Status UI")]
    [SerializeField] private Sprite _emptyHeart;
    [SerializeField] private Sprite _filledHeart;
    [SerializeField] private GameObject _hpImgObjPrefab;
    [SerializeField] private Transform _hpContent;
    [SerializeField] private TextMeshProUGUI _playerNameText;
    [SerializeField] private TextMeshProUGUI _waveText;
    [SerializeField] private TextMeshProUGUI _goalText;
    #endregion

    #region [Internal State]
    public Transform CanvasTransform => transform;
    public bool IsMagicSelectionMode => _isMagicSelectionMode;

    private GameManager _gameManager;
    private GameDataManager _gameDataManager;
    private PlayerController _localPlayer;
    private PlayerController _otherPlayer; // 상대 플레이어 캐싱

    private bool _isInitialized = false;
    private Coroutine _initCoroutine;

    // HP UI Pooling
    private List<Image> _hpImages = new List<Image>();

    // Magic Selection
    private List<MagicSelectBtn> _magicSelectButtons = new List<MagicSelectBtn>();
    private bool _isMagicSelectionMode = false;
    private MagicSelectBtn _selectedMagicButton = null;
    private int _selectedMagicCode = -1;

    // State Tracking (For Dirty Check)
    private int _cachedMagic1 = -1;
    private int _cachedMagic2 = -1;
    private int _cachedOtherMagic1 = -1;
    private int _cachedOtherMagic2 = -1;
    private int _cachedOtherCharIdx = -1;
    #endregion

    #region [Initialization]
    public void OnInitialize(GameManager gameManager, GameDataManager gameDataManager)
    {
        if (_isInitialized) return;
        _gameManager = gameManager;
        _gameDataManager = gameDataManager;
        _isInitialized = true;
    }

    private void OnDestroy()
    {
        UnsubscribeFromPlayer();
    }

    public void RegisterPlayer(PlayerController player)
    {
        if (player == null || !player.Object.IsValid) return;

        // InputAuthority 체크 (테스트 모드 예외 처리 포함)
        bool isTestMode = MainGameManager.Instance != null && MainGameManager.Instance.IsTestMode;
        if (!isTestMode && !player.Object.HasInputAuthority) return;

        // 중복 등록 방지
        if (_localPlayer == player) return;

        // 이전 작업 정리
        if (_initCoroutine != null) StopCoroutine(_initCoroutine);
        UnsubscribeFromPlayer();

        // 초기화 코루틴 시작
        _initCoroutine = StartCoroutine(Co_InitializePlayer(player));
    }

    private IEnumerator Co_InitializePlayer(PlayerController player)
    {
        _localPlayer = player;

        // 1. 데이터 동기화 대기 (MaxHealth, Nickname)
        // WaitUntil을 사용하여 가독성 향상
        float timeOut = 5f;
        float timer = 0f;
        
        // 데이터가 유효해질 때까지 대기
        yield return new WaitUntil(() => 
        {
            timer += Time.deltaTime;
            return (_localPlayer.MaxHealth > 0 && GetSafeNickname().Length > 0) || timer > timeOut;
        });

        // 2. 이벤트 구독
        SubscribeToPlayer();

        // 3. UI 최초 갱신
        RefreshHealthUI();
        UpdatePlayerName();
        UpdateLocalMagicUI(true); // 강제 갱신
        InitializeMagicSelectButtons();

        // 4. 상대방 찾기 시도
        FindAndCacheOtherPlayer();

        _initCoroutine = null;
    }

    private void SubscribeToPlayer()
    {
        if (_localPlayer == null || _localPlayer.State == null) return;
        _localPlayer.State.OnHealthChanged += OnHealthChanged;
        _localPlayer.State.OnDeath += OnPlayerDeath;
        _localPlayer.State.OnRespawned += OnPlayerRespawned;
    }

    private void UnsubscribeFromPlayer()
    {
        if (_localPlayer == null || _localPlayer.State == null) return;
        _localPlayer.State.OnHealthChanged -= OnHealthChanged;
        _localPlayer.State.OnDeath -= OnPlayerDeath;
        _localPlayer.State.OnRespawned -= OnPlayerRespawned;
    }
    #endregion

    #region [Game Loop & Input]
    private void Update()
    {
        if (!IsValidLocalPlayer()) return;

        HandleInput();
    }

    // LateUpdate에서 변경 사항 감지 (Dirty Check) - 코루틴 폴링 대체
    private void LateUpdate()
    {
        if (!IsValidLocalPlayer()) return;

        CheckLocalMagicChanges();
        CheckOtherPlayerChanges();
    }

    private bool IsValidLocalPlayer()
    {
        return _localPlayer != null && _localPlayer.Object != null && _localPlayer.Object.IsValid;
    }

    private void HandleInput()
    {
        if (_localPlayer.IsDead || _localPlayer.HasDashSkill) return;

        // 숫자키 입력
        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectMagicByIndex(0);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) SelectMagicByIndex(1);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) SelectMagicByIndex(2);
        else if (Input.GetKeyDown(KeyCode.Alpha4)) SelectMagicByIndex(3);

        // 마법 선택 모드 입력
        if (_isMagicSelectionMode && _selectedMagicCode != -1)
        {
            if (Input.GetMouseButtonDown(0)) ApplyMagicSelection(1); // 좌클릭 -> 1번 슬롯
            else if (Input.GetMouseButtonDown(1)) ApplyMagicSelection(2); // 우클릭 -> 2번 슬롯
            else if (Input.GetKeyDown(KeyCode.Escape)) ExitMagicSelectionMode();
        }
    }
    #endregion

    #region [State Change Detection]
    private void CheckLocalMagicChanges()
    {
        if (_localPlayer.Magic1Code != _cachedMagic1 || _localPlayer.Magic2Code != _cachedMagic2)
        {
            UpdateLocalMagicUI();
        }
    }

    private void CheckOtherPlayerChanges()
    {
        // 상대방이 없거나 유효하지 않으면 주기적으로 찾기
        if (_otherPlayer == null || !_otherPlayer.Object.IsValid)
        {
            // 간단한 타이머나 프레임 체크로 과도한 Find 방지 (여기서는 매 프레임 체크하되 null일때만)
             if (Time.frameCount % 60 == 0) FindAndCacheOtherPlayer();
             return;
        }

        // 상대방 상태 변경 감지
        if (_otherPlayer.Magic1Code != _cachedOtherMagic1 ||
            _otherPlayer.Magic2Code != _cachedOtherMagic2 ||
            _otherPlayer.CharacterIndex != _cachedOtherCharIdx)
        {
            UpdateOtherPlayerUI();
        }
    }

    private void FindAndCacheOtherPlayer()
    {
        if (MainGameManager.Instance == null) return;
        
        var foundPlayer = MainGameManager.Instance.FindOtherPlayer(_localPlayer);
        if (foundPlayer != null && foundPlayer != _otherPlayer)
        {
            _otherPlayer = foundPlayer;
            UpdateOtherPlayerUI(true); // 찾았으면 강제 갱신
        }
        else if (foundPlayer == null && _otherPlayer != null)
        {
            _otherPlayer = null;
            UpdateOtherPlayerUI(); // 사라졌으면 UI 끄기
        }
    }
    #endregion

    #region [UI Updates - Health]
    private void OnHealthChanged(float current, float max)
    {
        RefreshHealthUI();
    }

    private void RefreshHealthUI()
    {
        if (!IsValidLocalPlayer()) return;

        float currentHealth = _localPlayer.CurrentHealth;
        float maxHealth = _localPlayer.MaxHealth > 0 ? _localPlayer.MaxHealth : 3f; // Fallback

        int requiredHearts = Mathf.CeilToInt(maxHealth);

        // 1. 하트 오브젝트 풀링 (개수 맞추기)
        EnsureHeartCount(requiredHearts);

        // 2. 상태 업데이트
        for (int i = 0; i < _hpImages.Count; i++)
        {
            if (i < requiredHearts)
            {
                _hpImages[i].gameObject.SetActive(true);
                float heartStartHealth = i;
                float fill = Mathf.Clamp01(currentHealth - heartStartHealth);
                
                _hpImages[i].sprite = (fill > 0) ? _filledHeart : _emptyHeart;
                _hpImages[i].fillAmount = (fill > 0) ? fill : 1f;
            }
            else
            {
                _hpImages[i].gameObject.SetActive(false);
            }
        }
    }

    private void EnsureHeartCount(int count)
    {
        // 부족하면 생성
        while (_hpImages.Count < count)
        {
            GameObject hpObj = Instantiate(_hpImgObjPrefab, _hpContent);
            Image hpImage = hpObj.GetComponent<Image>() ?? hpObj.AddComponent<Image>();
            
            hpImage.type = Image.Type.Filled;
            hpImage.fillMethod = Image.FillMethod.Horizontal;
            
            _hpImages.Add(hpImage);
        }
    }
    #endregion

    #region [UI Updates - Magic & Player Info]
    private void UpdateLocalMagicUI(bool force = false)
    {
        if (!IsValidLocalPlayer()) return;

        SetMagicIcon(_localMagicIcon1, _localPlayer.Magic1Code);
        SetMagicIcon(_localMagicIcon2, _localPlayer.Magic2Code);

        // 캐시 업데이트
        _cachedMagic1 = _localPlayer.Magic1Code;
        _cachedMagic2 = _localPlayer.Magic2Code;
    }

    private void UpdateOtherPlayerUI(bool force = false)
    {
        bool hasOther = _otherPlayer != null && _otherPlayer.Object.IsValid;
        
        if (_otherPlayerIcon) _otherPlayerIcon.gameObject.SetActive(hasOther);
        if (_otherPlayerMagicIcon1) _otherPlayerMagicIcon1.gameObject.SetActive(hasOther);
        if (_otherPlayerMagicIcon2) _otherPlayerMagicIcon2.gameObject.SetActive(hasOther);

        if (!hasOther)
        {
            // Reset Cache
            _cachedOtherMagic1 = -1; _cachedOtherMagic2 = -1; _cachedOtherCharIdx = -1;
            return;
        }

        // 캐릭터 아이콘
        if (_gameDataManager != null)
        {
            var charData = _gameDataManager.CharacterService.GetCharacter(_otherPlayer.CharacterIndex);
            if (_otherPlayerIcon && charData != null) 
                _otherPlayerIcon.sprite = charData.profileIcon;
        }

        // 마법 아이콘
        SetMagicIcon(_otherPlayerMagicIcon1, _otherPlayer.Magic1Code);
        SetMagicIcon(_otherPlayerMagicIcon2, _otherPlayer.Magic2Code);

        // 캐시 업데이트
        _cachedOtherMagic1 = _otherPlayer.Magic1Code;
        _cachedOtherMagic2 = _otherPlayer.Magic2Code;
        _cachedOtherCharIdx = _otherPlayer.CharacterIndex;
    }

    private void SetMagicIcon(Image targetImg, int magicCode)
    {
        if (targetImg == null) return;

        var magicData = GetMagicDataByCode(magicCode);
        if (magicData != null && magicData.magicCombinedSprite != null)
        {
            targetImg.sprite = magicData.magicCombinedSprite;
            targetImg.enabled = true;
        }
        else
        {
            targetImg.enabled = false;
        }
    }
    #endregion

    #region [UI Updates - Name & Goal]
    private void UpdatePlayerName()
    {
        if (_playerNameText == null) return;
        string finalName = GetSafeNickname();
        _playerNameText.text = finalName;
    }

    private string GetSafeNickname()
    {
        if (!IsValidLocalPlayer()) return "";

        var playerData = _gameManager.GetPlayerData(_localPlayer.Object.InputAuthority, _localPlayer.Runner);
        if (playerData != null && !string.IsNullOrEmpty(playerData.Nick.ToString()))
        {
            return playerData.Nick.ToString();
        }
        
        // 데이터가 아직 없으면 임시 ID 반환
        return $"Player {_localPlayer.Object.InputAuthority.AsIndex}";
    }

    private void OnPlayerDeath(PlayerRef killer)
    {
        if (_playerNameText != null)
        {
            _playerNameText.text = "DEAD";
            _playerNameText.color = Color.red;
        }
    }

    private void OnPlayerRespawned()
    {
        UpdatePlayerName();
        if (_playerNameText != null) _playerNameText.color = Color.white;
        RefreshHealthUI();
    }
    
    // 외부 호출용 (Wave, Goal)
    public void SetWaveText(string text) { if (_waveText) _waveText.text = text; }
    public void SetGoalText(string text) { if (_goalText) _goalText.text = text; }
    #endregion

    #region [Magic Selection Logic]
    private void InitializeMagicSelectButtons()
    {
        ClearMagicSelectButtons();
        if (_gameDataManager == null) return;

        var allMagics = _gameDataManager.MagicService.GetAllMagics();
        var combinedCodes = _gameDataManager.MagicService.GetCombinationMagicCodes();

        foreach (var magic in allMagics)
        {
            if (combinedCodes.Contains(magic.magicCode)) continue;

            GameObject btnObj = Instantiate(_magicSelectBtnPrefab, _magicSelectBtnContentTransform);
            if (btnObj.TryGetComponent(out MagicSelectBtn btn))
            {
                btn.OnInitialized(magic);
                _magicSelectButtons.Add(btn);
            }
        }
    }

    private void SelectMagicByIndex(int index)
    {
        if (index >= 0 && index < _magicSelectButtons.Count)
        {
            OnMagicButtonSelected(_magicSelectButtons[index]);
        }
    }

    public void OnMagicButtonSelected(MagicSelectBtn button)
    {
        if (_localPlayer == null || _localPlayer.HasDashSkill || _localPlayer.IsDead) return;

        if (_selectedMagicButton != null) _selectedMagicButton.SetSelected(false);
        
        _selectedMagicButton = button;
        _selectedMagicCode = button.MagicCode;
        _isMagicSelectionMode = true;
        
        button.SetSelected(true);
    }

    private void ApplyMagicSelection(int slotIndex)
    {
        if (IsValidLocalPlayer())
        {
            _localPlayer.RPC_ChangeMagicCode(slotIndex, _selectedMagicCode);
        }
        ExitMagicSelectionMode();
    }

    private void ExitMagicSelectionMode()
    {
        if (_selectedMagicButton != null) _selectedMagicButton.SetSelected(false);
        _isMagicSelectionMode = false;
        _selectedMagicButton = null;
        _selectedMagicCode = -1;
    }

    private void ClearMagicSelectButtons()
    {
        foreach (var btn in _magicSelectButtons)
        {
            if (btn) Destroy(btn.gameObject);
        }
        _magicSelectButtons.Clear();
    }

    private MagicData GetMagicDataByCode(int code)
    {
        return (_gameDataManager != null && code != -1) 
            ? _gameDataManager.MagicService.GetMagicByName(code) 
            : null;
    }
    #endregion
}