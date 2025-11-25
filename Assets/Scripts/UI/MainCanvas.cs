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
    [Header("UI References")]
    [SerializeField] private Sprite _emptyHeart;
    [SerializeField] private Sprite _filledHeart;
    [SerializeField] private GameObject _hpImgObjPrefab;
    [SerializeField] private Transform _hpContent;
    [SerializeField] private TextMeshProUGUI _playerNameText;

    public Transform CanvasTransform => transform;

    private List<Image> _hpImages = new List<Image>();
    private GameManager _gameManager;
    private GameDataManager _gameDataManager;
    private PlayerController _localPlayer;
    private bool _isInitialized = false;

    public void Initialize(GameManager gameManager, GameDataManager gameDataManager)
    {
        if (_isInitialized) return;

        _gameManager = gameManager;
        _gameDataManager = gameDataManager;

        _isInitialized = true;
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
        
        // 테스트 모드가 아닐 때만 Input Authority 체크
        if (MainGameManager.Instance == null || !MainGameManager.Instance.IsTestMode)
        {
            // Input Authority가 있는 플레이어만 등록
            if (!player.Object.HasInputAuthority)
            {
                Debug.Log($"[MainCanvas] Ignoring player - no input authority: {player.name}");
                return;
            }
        }

        Debug.Log($"[MainCanvas] Registering player: {player.name} (TestMode: {MainGameManager.Instance?.IsTestMode ?? false})");
        SetupPlayer(player);
    }

    /// <summary>
    /// 플레이어를 설정하고 이벤트에 구독합니다.
    /// </summary>
    private void SetupPlayer(PlayerController player)
    {
        if (player == null || player.State == null) return;

        // 기존 플레이어가 있으면 이벤트 구독 해제
        UnsubscribeFromPlayer();

        _localPlayer = player;

        // 이벤트 구독
        _localPlayer.State.OnHealthChanged += OnHealthChanged;
        _localPlayer.State.OnDeath += OnPlayerDeath;
        _localPlayer.State.OnRespawned += OnPlayerRespawned;

        // 초기 HP UI 생성 및 플레이어 이름 설정
        InitializeHealthUI();
        
        // 플레이어 이름 설정 (PlayerData에서 닉네임 가져오기)
        UpdatePlayerName();
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
    /// HP UI를 초기화합니다.
    /// </summary>
    private void InitializeHealthUI()
    {
        if (_localPlayer == null) return;

        // 기존 하트 이미지 제거
        ClearHealthUI();

        // 최대 체력에 맞춰 하트 이미지 생성 (1HP당 하트 1개로 가정)
        int maxHearts = Mathf.CeilToInt(_localPlayer.MaxHealth);
        
        // MaxHealth가 아직 초기화되지 않았으면 재시도
        if (maxHearts <= 0)
        {
            // 초기화 대기 후 재시도
            StartCoroutine(WaitForMaxHealthAndInitialize());
            return;
        }
        
        for (int i = 0; i < maxHearts; i++)
        {
            GameObject hpObj = Instantiate(_hpImgObjPrefab, _hpContent);
            Image hpImage = hpObj.GetComponent<Image>() ?? hpObj.AddComponent<Image>();
            
            // Filled 타입 설정
            hpImage.type = Image.Type.Filled;
            hpImage.fillMethod = Image.FillMethod.Horizontal;
            hpImage.sprite = _filledHeart;
            hpImage.fillAmount = 1f;
            
            _hpImages.Add(hpImage);
        }

        // 초기 상태 업데이트
        UpdateHealthUI(_localPlayer.CurrentHealth, _localPlayer.MaxHealth);
    }

    /// <summary>
    /// MaxHealth가 초기화될 때까지 대기한 후 HP UI를 초기화합니다.
    /// </summary>
    private IEnumerator WaitForMaxHealthAndInitialize()
    {
        int maxAttempts = 30; // 3초 대기 (0.1초 * 30)
        int attempts = 0;
        
        while (attempts < maxAttempts && (_localPlayer == null || _localPlayer.MaxHealth <= 0))
        {
            yield return new WaitForSeconds(0.1f);
            attempts++;
        }
        
        // 다시 시도
        if (_localPlayer != null && _localPlayer.MaxHealth > 0)
        {
            InitializeHealthUI();
        }
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
        // MaxHealth가 변경되었으면 하트 UI를 다시 생성
        if (_localPlayer != null && _hpImages.Count != Mathf.CeilToInt(max))
        {
            InitializeHealthUI();
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
        if (_hpImages.Count == 0 || maxHealth <= 0) return;

        float healthPerHeart = 1f; // 하트 1개당 체력 1로 가정

        for (int i = 0; i < _hpImages.Count; i++)
        {
            float heartStartHealth = i * healthPerHeart;
            float heartEndHealth = (i + 1) * healthPerHeart;
            
            if (currentHealth >= heartEndHealth)
            {
                // 완전히 채워진 하트
                _hpImages[i].sprite = _filledHeart;
                _hpImages[i].fillAmount = 1f;
            }
            else if (currentHealth > heartStartHealth)
            {
                // 부분적으로 채워진 하트
                _hpImages[i].sprite = _filledHeart;
                float fillAmount = (currentHealth - heartStartHealth) / healthPerHeart;
                _hpImages[i].fillAmount = fillAmount;
            }
            else
            {
                // 빈 하트
                _hpImages[i].sprite = _emptyHeart;
                _hpImages[i].fillAmount = 1f; // 스프라이트 자체가 빈 모양이므로 fillAmount는 1
            }
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

        var playerData = _gameManager.GetPlayerData(_localPlayer.Object.InputAuthority, _localPlayer.Runner);
        string nickName = playerData?.Nick.ToString() ?? $"Player {_localPlayer.Object.InputAuthority.AsIndex}";

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
}
