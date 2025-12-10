using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;

/// <summary>
/// 메인 게임 씬의 UI 상태를 관리합니다. (HP, 이름 등)
/// ICanvas 인터페이스를 구현하며, 로컬 플레이어의 상태를 구독합니다.
/// </summary>
public class MainCanvas : MonoBehaviour, ICanvas
{
    [SerializeField] private GameObject _magicSelectBtnPrefab;
    [SerializeField] private Transform _magicSelectBtnContentTransform;
    [SerializeField] private Image _magicIconImage1;
    [SerializeField] private Image _magicIconImage2;
    [SerializeField] private Image _otherPlayerImage;
    [SerializeField] private Image _otherPlayerMagicIconImage1;
    [SerializeField] private Image _otherPlayerMagicIconImage2;

    [Header("UI References")]
    [SerializeField] private Sprite _emptyHeart;
    [SerializeField] private Sprite _filledHeart;
    [SerializeField] private GameObject _hpImgObjPrefab;
    [SerializeField] private Transform _hpContent;
    [SerializeField] private TextMeshProUGUI _playerNameText;
    [SerializeField] private TextMeshProUGUI _waveText;
    [SerializeField] private TextMeshProUGUI _goalText;

    public Transform CanvasTransform => transform;
    
    /// <summary>
    /// 마법 선택 모드가 활성화되어 있는지 확인합니다.
    /// </summary>
    public bool IsMagicSelectionMode => _isMagicSelectionMode;

    private List<Image> _hpImages = new List<Image>();
    private GameManager _gameManager;
    private GameDataManager _gameDataManager;
    private PlayerController _localPlayer;
    private bool _isInitialized = false;
    
    // 코루틴 중복 실행 방지를 위한 변수
    private Coroutine _initCoroutine;
    
    // 마법 선택 버튼 리스트
    private List<MagicSelectBtn> _magicSelectButtons = new List<MagicSelectBtn>();
    
    // 마법 선택 모드 관련
    private bool _isMagicSelectionMode = false;
    private MagicSelectBtn _selectedMagicButton = null;
    private int _selectedMagicCode = -1;
    

    public void OnInitialize(GameManager gameManager, GameDataManager gameDataManager)
    {
        if (_isInitialized) return;

        _gameManager = gameManager;
        _gameDataManager = gameDataManager;

        _isInitialized = true;
    }

    /// <summary>
    /// 현재 웨이브/라운드 정보를 텍스트로 표시합니다.
    /// </summary>
    public void SetWaveText(string text)
    {
        if (_waveText != null)
        {
            _waveText.text = text;
        }
    }

    /// <summary>
    /// 현재 목표(킬 수, 생존 시간, 수집 개수 등)를 텍스트로 표시합니다.
    /// </summary>
    public void SetGoalText(string text)
    {
        if (_goalText != null)
        {
            _goalText.text = text;
        }
    }

    void OnDestroy()
    {
        UnsubscribeFromPlayer();
    }

    /// <summary>
    /// PlayerController가 스폰될 때 호출합니다. 로컬 Input Authority가 있는 플레이어만 등록합니다.
    /// 테스트 모드에서는 선택된 플레이어를 등록합니다.
    /// </summary>
    public void RegisterPlayer(PlayerController player)
    {
        if (player == null) return;
        
        // 객체가 유효한지 확인
        if (player.Object == null || !player.Object.IsValid) return;

        // 테스트 모드가 아닐 때만 Input Authority 체크
        if (MainGameManager.Instance == null || !MainGameManager.Instance.IsTestMode)
        {
            // HasInputAuthority 속성을 사용하는 것이 더 안전합니다.
            if (!player.Object.HasInputAuthority)
            {
                return;
            }
        }
        
        // 중복 등록 방지: 같은 플레이어가 이미 등록되어 있으면 무시
        if (_localPlayer != null && _localPlayer == player && _localPlayer.Object != null && _localPlayer.Object.IsValid)
        {
            Debug.Log($"[MainCanvas] Player already registered, skipping duplicate registration: {player.Object.Id}");
            return;
        }
        
        Debug.Log($"[MainCanvas] Register request received for Player {player.Object.Id}. Starting setup process...");
        
        // 기존 코루틴이 있다면 중지 (중복 실행 방지)
        if (_initCoroutine != null)
        {
            StopCoroutine(_initCoroutine);
            Debug.Log("[MainCanvas] Stopped previous setup coroutine.");
        }
        
        // 데이터 대기 및 설정 코루틴 시작
        _initCoroutine = StartCoroutine(Co_SetupPlayerSafe(player));
    }

    /// <summary>
    /// 플레이어 데이터(MaxHealth)가 동기화될 때까지 기다린 후 UI를 설정합니다.
    /// </summary>
    private IEnumerator Co_SetupPlayerSafe(PlayerController player)
    {
        if (player == null || player.State == null)
        {
            Debug.LogWarning("[MainCanvas] Co_SetupPlayerSafe: Player or State is null!");
            yield break;
        }

        _localPlayer = player;
        
        // 1. [체력 동기화 대기] MaxHealth가 들어올 때까지 대기
        float timeout = 5.0f;
        float timer = 0f;
        
        Debug.Log("[MainCanvas] Waiting for player stats to sync...");

        while (timer < timeout)
        {
            if (player == null || !player.Object.IsValid) yield break;

            if (player.MaxHealth > 0.1f)
            {
                Debug.Log($"[MainCanvas] Player stats synced! MaxHealth: {player.MaxHealth}");
                break;
            }

            timer += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }

        // 2. 이벤트 재구독
        UnsubscribeFromPlayer(); 
        _localPlayer = player; 

        if (_localPlayer != null && _localPlayer.State != null)
        {
            _localPlayer.State.OnHealthChanged += OnHealthChanged;
            _localPlayer.State.OnDeath += OnPlayerDeath;
            _localPlayer.State.OnRespawned += OnPlayerRespawned;
        }
        
        // 네트워크 변수 변경 감지를 위한 ChangeDetector 구독
        if (_localPlayer != null && _localPlayer.MagicChangeDetector != null)
        {
            StartCoroutine(Co_UpdateUIOnMagicChange());
        }

        // 3. UI 그리기 (체력)
        InitializeHealthUI();
        
        // 4. [수정됨] 이름 동기화 대기 (최대 3초간 닉네임 수신 시도)
        // PlayerData는 캐릭터보다 조금 늦게 동기화될 수 있습니다.
        float nameTimer = 0f;
        while (nameTimer < 3.0f)
        {
            UpdatePlayerName(); // 계속 시도

            // 닉네임 텍스트가 "Player 숫자" 형식이 아니고, 비어있지도 않으면 성공으로 간주
            if (_playerNameText.text.Length > 0 && !_playerNameText.text.StartsWith("Player "))
            {
                break; // 진짜 닉네임을 찾았으니 루프 탈출
            }
            
            yield return new WaitForSeconds(0.5f);
            nameTimer += 0.5f;
        }
        
        // 마지막으로 한 번 더 확실하게 업데이트
        UpdatePlayerName();
        
        // UI 전체 업데이트
        UpdateAllUI();

        Debug.Log("[MainCanvas] SetupPlayerSafe completed successfully.");
        _initCoroutine = null;
    }
    

    /// <summary>
    /// 플레이어 이벤트 구독 해제
    /// </summary>
    private void UnsubscribeFromPlayer()
    {
        if (_localPlayer != null && _localPlayer.State != null)
        {
            _localPlayer.State.OnHealthChanged -= OnHealthChanged;
            _localPlayer.State.OnDeath -= OnPlayerDeath;
            _localPlayer.State.OnRespawned -= OnPlayerRespawned;
            _localPlayer = null;
        }
    }
    
    /// <summary>
    /// 마법 관련 네트워크 변수 변경을 감지하여 UI를 업데이트합니다.
    /// Magic1Code, Magic2Code 변경을 감지합니다.
    /// </summary>
    private IEnumerator Co_UpdateUIOnMagicChange()
    {
        int lastMagic1Code = -1;
        int lastMagic2Code = -1;
        
        // 상대방 마법 코드 추적
        int lastOtherMagic1Code = -1;
        int lastOtherMagic2Code = -1;
        
        while (_localPlayer != null && _localPlayer.Object != null && _localPlayer.Object.IsValid)
        {
            // 로컬 플레이어 마법 코드 변경 감지
            if (_localPlayer.Magic1Code != lastMagic1Code ||
                _localPlayer.Magic2Code != lastMagic2Code)
            {
                UpdateMagicUI();
                lastMagic1Code = _localPlayer.Magic1Code;
                lastMagic2Code = _localPlayer.Magic2Code;
            }
            
            // 상대방 플레이어 찾기
            PlayerController otherPlayer = FindOtherPlayer();
            if (otherPlayer != null)
            {
                // 상대방 마법 코드 변경 감지 또는 초기 로드 시
                if (otherPlayer.Magic1Code != lastOtherMagic1Code ||
                    otherPlayer.Magic2Code != lastOtherMagic2Code ||
                    lastOtherMagic1Code == -1) // 초기 로드 시에도 업데이트
                {
                    UpdateOtherPlayerUI();
                    lastOtherMagic1Code = otherPlayer.Magic1Code;
                    lastOtherMagic2Code = otherPlayer.Magic2Code;
                }
            }
            else
            {
                // 상대방이 없으면 추적 변수 초기화 및 UI 업데이트
                if (lastOtherMagic1Code != -1 || lastOtherMagic2Code != -1)
                {
                    UpdateOtherPlayerUI(); // UI 숨김 처리
                }
                lastOtherMagic1Code = -1;
                lastOtherMagic2Code = -1;
            }
            
            yield return new WaitForSeconds(0.1f);
        }
    }

    /// <summary>
    /// HP UI를 초기화합니다.
    /// </summary>
    private void InitializeHealthUI()
    {
        if (_localPlayer == null)
        {
            Debug.LogWarning("[MainCanvas] InitializeHealthUI: _localPlayer is null!");
            return;
        }
        
        // 방어 코드: MaxHealth가 여전히 0이라면 기본값 3이라도 줘서 UI가 깨지지 않게 함
        float targetMaxHealth = _localPlayer.MaxHealth > 0 ? _localPlayer.MaxHealth : 3f;
        
        if (_localPlayer.MaxHealth <= 0)
        {
            Debug.LogWarning($"[MainCanvas] InitializeHealthUI: MaxHealth is still 0, using default value {targetMaxHealth}");
        }

        // 기존 하트 이미지 제거
        ClearHealthUI();

        int maxHearts = Mathf.CeilToInt(targetMaxHealth);
        Debug.Log($"[MainCanvas] Creating {maxHearts} hearts (MaxHealth: {_localPlayer.MaxHealth}, Using: {targetMaxHealth})");

        for (int i = 0; i < maxHearts; i++)
        {
            GameObject hpObj = Instantiate(_hpImgObjPrefab, _hpContent);
            Image hpImage = hpObj.GetComponent<Image>();
            if (hpImage == null)
            {
                hpImage = hpObj.AddComponent<Image>();
            }
            
            // Filled 타입 설정
            hpImage.type = Image.Type.Filled;
            hpImage.fillMethod = Image.FillMethod.Horizontal;
            hpImage.sprite = _filledHeart;
            hpImage.fillAmount = 1f;
            
            _hpImages.Add(hpImage);
        }

        // 초기 상태 업데이트
        UpdateHealthUI(_localPlayer.CurrentHealth, targetMaxHealth);
    }

    /// <summary>
    /// MaxHealth가 초기화될 때까지 대기한 후 HP UI를 초기화합니다.
    /// </summary>
    private IEnumerator WaitForMaxHealthAndInitialize()
    {
        int maxAttempts = 50; // 5초 대기 (0.1초 * 50)
        int attempts = 0;
        
        // 로컬 플레이어가 존재하고, 객체가 유효한 동안 대기
        while (attempts < maxAttempts)
        {
            if (_localPlayer == null || !_localPlayer.Object.IsValid) 
            {
                yield break; // 플레이어가 사라졌으면 중단
            }

            // MaxHealth가 들어왔다면 초기화 진행
            if (_localPlayer.MaxHealth > 0)
            {
                InitializeHealthUI();
                yield break;
            }

            yield return new WaitForSeconds(0.1f);
            attempts++;
        }
        
        Debug.LogWarning("[MainCanvas] Failed to initialize Health UI: MaxHealth is 0 or timeout.");
    }

    /// <summary>
    /// HP UI를 모두 제거합니다.
    /// </summary>
    private void ClearHealthUI()
    {
        foreach (var image in _hpImages)
        {
            if (image != null)
            {
                Destroy(image.gameObject);
            }
        }
        _hpImages.Clear();
    }

    // ========== Event Handlers ==========

    /// <summary>
    /// HP 변경 이벤트 핸들러
    /// </summary>
    private void OnHealthChanged(float current, float max)
    {
        // 방어 코드: 로컬 플레이어가 없거나 파괴되었으면 리턴
        if (_localPlayer == null || !_localPlayer.Object.IsValid) return;

        // MaxHealth가 변경되었거나(레벨업 등), 
        // 하트 개수와 실제 MaxHealth가 다르면 UI 재생성 (리스폰 직후 동기화 문제 해결)
        int requiredHearts = Mathf.CeilToInt(max);
        
        if (_hpImages.Count != requiredHearts)
        {
            InitializeHealthUI(); // 개수가 다르면 아예 다시 그리기
        }
        else
        {
            UpdateHealthUI(current, max);
        }
    }

    /// <summary>
    /// HP UI를 업데이트합니다. (하트 시스템)
    /// </summary>
    private void UpdateHealthUI(float currentHealth, float maxHealth)
    {
        if (_hpImages.Count == 0) return;

        float healthPerHeart = 1f; // 하트 1개당 체력 1로 가정

        for (int i = 0; i < _hpImages.Count; i++)
        {
            // 방어 코드: 리스트 중간에 파괴된 객체가 있는지 확인
            if (_hpImages[i] == null) 
            {
                InitializeHealthUI(); // UI가 깨졌으면 재초기화
                return;
            }

            float heartStartHealth = i * healthPerHeart;
            
            // fillAmount 계산 로직 단순화 및 클램핑
            float fill = Mathf.Clamp01(currentHealth - heartStartHealth);
            
            _hpImages[i].sprite = (fill > 0) ? _filledHeart : _emptyHeart;
            _hpImages[i].fillAmount = (fill > 0) ? fill : 1f; // 빈 하트일 때도 모양 유지를 위해 1로
        }
    }

    /// <summary>
    /// 플레이어 사망 이벤트 핸들러
    /// </summary>
    private void OnPlayerDeath(Fusion.PlayerRef killer)
    {
        if (_playerNameText != null)
        {
            _playerNameText.text = "DEAD";
            _playerNameText.color = Color.red;
        }
    }

    /// <summary>
    /// 플레이어 리스폰 이벤트 핸들러
    /// </summary>
    private void OnPlayerRespawned()
    {
        UpdatePlayerName();
        
        // 이름 색상 복구 및 HP UI 강제 업데이트
        if (_playerNameText != null)
        {
            _playerNameText.color = Color.white;
        }
        
        UpdateHealthUI(_localPlayer.CurrentHealth, _localPlayer.MaxHealth);
    }

    // ========== Additional UI Methods ==========
    
    /// <summary>
    /// 로컬 플레이어의 닉네임을 가져와 화면에 표시합니다.
    /// </summary>
    public void UpdatePlayerName()
    {
        if (_playerNameText == null || _localPlayer == null || _gameManager == null || _localPlayer.Runner == null) return;

        // PlayerData 조회
        var playerData = _gameManager.GetPlayerData(_localPlayer.Object.InputAuthority, _localPlayer.Runner);
        
        // [수정] 닉네임 가져오기 로직 강화
        string nickName = "";

        if (playerData != null && !string.IsNullOrEmpty(playerData.Nick.ToString()))
        {
            // 데이터도 있고 닉네임도 제대로 있을 때
            nickName = playerData.Nick.ToString();
        }
        else
        {
            // 데이터가 없거나 닉네임이 비어있으면 임시 이름(Player ID) 표시
            if (_localPlayer.Object != null && _localPlayer.Object.IsValid)
            {
                nickName = $"Player {_localPlayer.Object.InputAuthority.AsIndex}";
            }
            else
            {
                nickName = "Loading...";
            }
        }

        _playerNameText.text = nickName;
    }

    public void SetPlayerName(string playerName)
    {
        if (_playerNameText != null)
        {
            _playerNameText.text = playerName;
        }
    }

    /// <summary>
    /// HP 텍스트로 표시 (하트 대신 숫자로 표시하고 싶을 때)
    /// </summary>
    public void UpdateHealthText(float current, float max)
    {
        if (_playerNameText != null)
        {
            _playerNameText.text = $"HP: {current:F0}/{max:F0}";
        }
    }
    
    // ========== Magic UI Methods ==========
    
    /// <summary>
    /// 모든 UI 요소를 업데이트합니다.
    /// 플레이어 변경 시 호출하여 전체 UI를 갱신합니다.
    /// </summary>
    public void UpdateAllUI()
    {
        if (_localPlayer == null || _gameDataManager == null) return;
        
        // 체력 UI 업데이트
        if (_localPlayer.State != null)
        {
            UpdateHealthUI(_localPlayer.CurrentHealth, _localPlayer.MaxHealth);
        }
        
        // 플레이어 이름 업데이트
        UpdatePlayerName();
        
        // 마법 UI 업데이트
        UpdateMagicUI();
        
        // 상대방 UI 업데이트
        UpdateOtherPlayerUI();
        
        // 마법 선택 버튼 초기화
        InitializeMagicSelectButtons();
    }
    
    /// <summary>
    /// 자신의 마법 아이콘을 업데이트합니다.
    /// CharacterData의 magicData1, magicData2를 상시 표시합니다.
    /// </summary>
    private void UpdateMagicUI()
    {
        if (_localPlayer == null || _gameDataManager == null) return;
        
        // 첫 번째 마법 아이콘 업데이트 (CharacterData의 magicData1 - 상시 표시)
        if (_magicIconImage1 != null)
        {
            MagicData magic1 = GetMagicDataByCode(_localPlayer.Magic1Code);
            if (magic1 != null && magic1.magicCombinedSprite != null)
            {
                _magicIconImage1.sprite = magic1.magicCombinedSprite;
                _magicIconImage1.enabled = true;
            }
            else
            {
                _magicIconImage1.enabled = false;
            }
        }
        
        // 두 번째 마법 아이콘 업데이트 (CharacterData의 magicData2 - 상시 표시)
        if (_magicIconImage2 != null)
        {
            MagicData magic2 = GetMagicDataByCode(_localPlayer.Magic2Code);
            if (magic2 != null && magic2.magicCombinedSprite != null)
            {
                _magicIconImage2.sprite = magic2.magicCombinedSprite;
                _magicIconImage2.enabled = true;
            }
            else
            {
                _magicIconImage2.enabled = false;
            }
        }
    }
    
    /// <summary>
    /// 상대방의 캐릭터 및 마법 아이콘을 업데이트합니다.
    /// </summary>
    private void UpdateOtherPlayerUI()
    {
        if (_localPlayer == null || _gameDataManager == null) return;
        
        // 상대방 플레이어 찾기
        PlayerController otherPlayer = FindOtherPlayer();
        if (otherPlayer == null)
        {
            // 상대방이 없으면 UI 숨김 (캐릭터 이미지만)
            if (_otherPlayerImage != null) _otherPlayerImage.enabled = false;
            // 마법 아이콘은 항상 활성화 상태 유지 (켜고 끄는 기능 제거)
            return;
        }
        
        // 상대방 캐릭터 이미지 업데이트
        if (_otherPlayerImage != null)
        {
            CharacterData characterData = _gameDataManager.CharacterService.GetCharacter(otherPlayer.CharacterIndex);
            if (characterData != null && characterData.profileIcon != null)
            {
                _otherPlayerImage.sprite = characterData.profileIcon;
                _otherPlayerImage.enabled = true;
            }
            else
            {
                _otherPlayerImage.enabled = false;
            }
        }
        
        // 상대방 첫 번째 마법 아이콘 업데이트 (CharacterData의 magicData1 - 상시 표시)
        if (_otherPlayerMagicIconImage1 != null)
        {
            MagicData magic1 = GetMagicDataByCode(otherPlayer.Magic1Code);
            if (magic1 != null && magic1.magicCombinedSprite != null)
            {
                _otherPlayerMagicIconImage1.sprite = magic1.magicCombinedSprite;
                _otherPlayerMagicIconImage1.enabled = true;
            }
            else
            {
                _otherPlayerMagicIconImage1.enabled = false;
            }
        }
        
        // 상대방 두 번째 마법 아이콘 업데이트 (CharacterData의 magicData2 - 상시 표시)
        if (_otherPlayerMagicIconImage2 != null)
        {
            MagicData magic2 = GetMagicDataByCode(otherPlayer.Magic2Code);
            if (magic2 != null && magic2.magicCombinedSprite != null)
            {
                _otherPlayerMagicIconImage2.sprite = magic2.magicCombinedSprite;
                _otherPlayerMagicIconImage2.enabled = true;
            }
            else
            {
                _otherPlayerMagicIconImage2.enabled = false;
            }
        }
    }
    
    /// <summary>
    /// 마법 선택 버튼들을 초기화하고 생성합니다.
    /// 합체 마법을 제외한 단일 마법만 표시합니다.
    /// </summary>
    private void InitializeMagicSelectButtons()
    {
        if (_magicSelectBtnPrefab == null || _magicSelectBtnContentTransform == null) return;
        if (_gameDataManager == null) return;
        
        // 기존 버튼 제거
        ClearMagicSelectButtons();
        
        // 모든 마법 가져오기
        List<MagicData> allMagics = _gameDataManager.MagicService.GetAllMagics();
        if (allMagics == null || allMagics.Count == 0) return;
        
        // 합체 마법 코드 목록 가져오기 (제외용)
        HashSet<int> combinationMagicCodes = GetCombinationMagicCodes();
        
        // 단일 마법만 필터링하여 버튼 생성
        foreach (var magic in allMagics)
        {
            if (magic == null) continue;
            
            // 합체 마법이면 제외
            if (combinationMagicCodes.Contains(magic.magicCode)) continue;
            
            // 버튼 생성
            GameObject btnObj = Instantiate(_magicSelectBtnPrefab, _magicSelectBtnContentTransform);
            MagicSelectBtn btn = btnObj.GetComponent<MagicSelectBtn>();
            if (btn != null)
            {
                btn.OnInitialized(magic);
                _magicSelectButtons.Add(btn);
            }
        }
    }
    
    /// <summary>
    /// 마법 선택 버튼들을 모두 제거합니다.
    /// </summary>
    private void ClearMagicSelectButtons()
    {
        foreach (var btn in _magicSelectButtons)
        {
            if (btn != null && btn.gameObject != null)
            {
                Destroy(btn.gameObject);
            }
        }
        _magicSelectButtons.Clear();
        
        // Transform의 자식도 모두 제거 (안전장치)
        if (_magicSelectBtnContentTransform != null)
        {
            foreach (Transform child in _magicSelectBtnContentTransform)
            {
                if (child != null)
                {
                    Destroy(child.gameObject);
                }
            }
        }
    }
    
    /// <summary>
    /// 마법 코드로 MagicData를 가져옵니다.
    /// </summary>
    private MagicData GetMagicDataByCode(int magicCode)
    {
        if (_gameDataManager == null || magicCode == -1) return null;
        return _gameDataManager.MagicService.GetMagicByName(magicCode);
    }
    
    /// <summary>
    /// 상대방 플레이어를 찾습니다.
    /// </summary>
    private PlayerController FindOtherPlayer()
    {
        if (_localPlayer == null || MainGameManager.Instance == null) return null;
        
        List<PlayerController> allPlayers = MainGameManager.Instance.GetAllPlayers();
        if (allPlayers == null || allPlayers.Count == 0) return null;
        
        foreach (var player in allPlayers)
        {
            if (player != null && player != _localPlayer && !player.IsDead)
            {
                return player;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// 합체 마법 코드 목록을 가져옵니다.
    /// </summary>
    private HashSet<int> GetCombinationMagicCodes()
    {
        HashSet<int> combinationCodes = new HashSet<int>();
        
        if (_gameDataManager == null) return combinationCodes;
        
        List<MagicCombinationData> combinations = _gameDataManager.MagicService.GetAllCombinations();
        if (combinations == null) return combinationCodes;
        
        foreach (var combination in combinations)
        {
            if (combination != null && combination.IsValid())
            {
                combinationCodes.Add(combination.resultMagicCode);
            }
        }
        
        return combinationCodes;
    }
    
    // ========== Magic Selection Mode ==========
    
    void Update()
    {
        // 키보드 입력 감지 (1, 2, 3, 4)
        if (_localPlayer == null || _localPlayer.IsDead) return;
        if (_localPlayer.HasDashSkill) return; // 돌진 중에는 마법 선택 불가
        
        // 키보드 1, 2, 3, 4로 마법 선택 모드 진입
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            SelectMagicByIndex(0);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
        {
            SelectMagicByIndex(1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
        {
            SelectMagicByIndex(2);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
        {
            SelectMagicByIndex(3);
        }
        
        // 선택 모드에서 마우스 클릭 처리
        if (_isMagicSelectionMode && _selectedMagicCode != -1)
        {
            HandleMagicSelectionInput();
            
            // ESC 키로 선택 모드 취소
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ExitMagicSelectionMode();
            }
        }
    }
    
    /// <summary>
    /// 마법 버튼 클릭 시 호출됩니다.
    /// </summary>
    public void OnMagicButtonSelected(MagicSelectBtn button)
    {
        if (button == null || button.MagicData == null) return;
        if (_localPlayer == null || _localPlayer.IsDead) return;
        if (_localPlayer.HasDashSkill) return; // 돌진 중에는 마법 선택 불가
        
        // 선택 모드 진입
        EnterMagicSelectionMode(button);
    }
    
    /// <summary>
    /// 인덱스로 마법 선택 모드 진입 (키보드 입력용).
    /// </summary>
    private void SelectMagicByIndex(int index)
    {
        if (index < 0 || index >= _magicSelectButtons.Count) return;
        
        MagicSelectBtn button = _magicSelectButtons[index];
        if (button != null && button.MagicData != null)
        {
            EnterMagicSelectionMode(button);
        }
    }
    
    /// <summary>
    /// 마법 선택 모드에 진입합니다.
    /// </summary>
    private void EnterMagicSelectionMode(MagicSelectBtn button)
    {
        // 이전 선택 해제
        if (_selectedMagicButton != null)
        {
            _selectedMagicButton.SetSelected(false);
        }
        
        // 새 선택
        _selectedMagicButton = button;
        _selectedMagicCode = button.MagicCode;
        _isMagicSelectionMode = true;
        
        // 버튼 크기 증가
        button.SetSelected(true);
    }
    
    /// <summary>
    /// 선택 모드에서 마우스 입력을 처리합니다.
    /// </summary>
    private void HandleMagicSelectionInput()
    {
        if (_localPlayer == null || !_localPlayer.Object.HasInputAuthority) return;
        
        bool leftClick = Input.GetMouseButtonDown(0);
        bool rightClick = Input.GetMouseButtonDown(1);
        
        if (leftClick)
        {
            // 왼쪽 클릭: 슬롯 1에 선택한 마법 영구 변경
            ChangeMagicCodeInSlot(1, _selectedMagicCode);
            ExitMagicSelectionMode();
        }
        else if (rightClick)
        {
            // 오른쪽 클릭: 슬롯 2에 선택한 마법 영구 변경
            ChangeMagicCodeInSlot(2, _selectedMagicCode);
            ExitMagicSelectionMode();
        }
    }
    
    /// <summary>
    /// 지정된 슬롯에 마법 코드를 영구적으로 변경합니다.
    /// </summary>
    private void ChangeMagicCodeInSlot(int slot, int magicCode)
    {
        if (_localPlayer == null || !_localPlayer.Object.HasInputAuthority) return;
        if (magicCode == -1) return;
        
        // RPC 호출하여 마법 코드 영구 변경
        _localPlayer.RPC_ChangeMagicCode(slot, magicCode);
    }
    
    /// <summary>
    /// 마법 선택 모드를 종료합니다.
    /// </summary>
    private void ExitMagicSelectionMode()
    {
        if (_selectedMagicButton != null)
        {
            _selectedMagicButton.SetSelected(false);
        }
        
        _isMagicSelectionMode = false;
        _selectedMagicButton = null;
        _selectedMagicCode = -1;
    }
}
