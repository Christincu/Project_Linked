using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;

public class MainCanvas : MonoBehaviour, ICanvas
{
    [Header("Magic Selection UI")]
    [SerializeField] private GameObject _magicSelectBtnPrefab;
    [SerializeField] private Transform _magicSelectBtnContentTransform;
    [SerializeField] private Image _localMagicIcon1;
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

    public Transform CanvasTransform => transform;

    private GameManager _gameManager;
    private GameDataManager _gameDataManager;
    private MainGameManager _mainGameManager;
    private PlayerController _localPlayer;
    private PlayerController _otherPlayer;

    private bool _isInitialized = false;
    private Coroutine _initCoroutine;

    private List<Image> _hpImages = new List<Image>();

    public void SetMainGameManager(MainGameManager mainGameManager)
    {
        _mainGameManager = mainGameManager;
    }

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
        bool isTestMode = _mainGameManager != null && _mainGameManager.IsTestMode;
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
        float timeOut = 5f;
        float timer = 0f;
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

        // 4. 상대방 찾기 시도
        FindAndCacheOtherPlayer();

        _initCoroutine = null;
    }

    private void SubscribeToPlayer()
    {
        if (_localPlayer == null) return;
        _localPlayer.OnHealthChanged += OnHealthChanged;
        _localPlayer.OnDeath += OnPlayerDeath;
        _localPlayer.OnRespawned += OnPlayerRespawned;
    }

    private void UnsubscribeFromPlayer()
    {
        if (_localPlayer == null) return;
        _localPlayer.OnHealthChanged -= OnHealthChanged;
        _localPlayer.OnDeath -= OnPlayerDeath;
        _localPlayer.OnRespawned -= OnPlayerRespawned;
    }

    private void Update()
    {
        if (!IsValidLocalPlayer()) return;

        HandleInput();
    }

    private void LateUpdate()
    {
        if (!IsValidLocalPlayer()) return;
        CheckOtherPlayerChanges();
    }

    private bool IsValidLocalPlayer()
    {
        return _localPlayer != null && _localPlayer.Object != null && _localPlayer.Object.IsValid;
    }

    private void HandleInput()
    {
        if (_localPlayer.IsDead) return;
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

        UpdateOtherPlayerUI();
    }

    private void FindAndCacheOtherPlayer()
    {
        if (_mainGameManager == null) return;
        
        var foundPlayer = _mainGameManager.FindOtherPlayer(_localPlayer);
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

    private void UpdateOtherPlayerUI(bool force = false)
    {
        bool hasOther = _otherPlayer != null && _otherPlayer.Object.IsValid;
        
        if (_otherPlayerIcon) _otherPlayerIcon.gameObject.SetActive(hasOther);
        if (_otherPlayerMagicIcon1) _otherPlayerMagicIcon1.gameObject.SetActive(hasOther);
        if (_otherPlayerMagicIcon2) _otherPlayerMagicIcon2.gameObject.SetActive(hasOther);

        // 캐릭터 아이콘
        if (_gameDataManager != null)
        {
            var charData = _gameDataManager.CharacterService.GetCharacter(_otherPlayer.CharacterIndex);
            if (_otherPlayerIcon && charData != null) 
                _otherPlayerIcon.sprite = charData.profileIcon;
        }
    }

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
}