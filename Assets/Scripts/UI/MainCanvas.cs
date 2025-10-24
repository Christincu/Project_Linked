using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MainCanvas : MonoBehaviour, ICanvas
{
    [Header("UI References")]
    [SerializeField] private Sprite _emptyHeart;
    [SerializeField] private Sprite _filledHeart;
    [SerializeField] private GameObject _hpImgObjPrefab;
    [SerializeField] private Transform _hpContent;
    [SerializeField] private TextMeshProUGUI _playerNameText;

    [SerializeField] private bool _isTestMode = true;

    public Transform CanvasTransform => transform;

    private List<Image> _hpImages = new List<Image>();
    private GameManager _gameManager;
    private GameDataManager _gameDataManager;
    private PlayerController _localPlayer;

    public void Initialize(GameManager gameManager, GameDataManager gameDataManager)
    {
        _gameManager = gameManager;
        _gameDataManager = gameDataManager;

        // 테스트 모드 확인
        _isTestMode = TestGameManager.Instance != null;

        Debug.Log($"[MainCanvas] Initialized - TestMode: {_isTestMode}");

        // 테스트 모드에서는 플레이어 전환 감지
        if (_isTestMode)
        {
            StartCoroutine(WatchPlayerSwitch());
        }
    }

    /// <summary>
    /// PlayerController가 스폰될 때 호출합니다.
    /// </summary>
    public void RegisterPlayer(PlayerController player)
    {
        if (player == null) return;

        // 테스트 모드에서는 로컬 플레이어만 등록 (선택된 슬롯)
        if (_isTestMode)
        {
            // TestGameManager의 선택된 슬롯과 일치하는 플레이어만 등록
            if (TestGameManager.Instance != null)
            {
                var selectedPlayer = TestGameManager.Instance.GetSelectedPlayer();
                if (selectedPlayer != player)
                {
                    Debug.Log($"[MainCanvas] Ignoring player - not selected slot");
                    return;
                }
            }
        }
        else
        {
            // 일반 모드에서는 InputAuthority가 있는 플레이어만 등록
            if (!player.Object.HasInputAuthority)
            {
                Debug.Log($"[MainCanvas] Ignoring player - no input authority");
                return;
            }
        }

        Debug.Log($"[MainCanvas] Registering player: {player.name}");
        SetupPlayer(player);
    }

    /// <summary>
    /// 테스트 모드에서 플레이어 전환을 감지합니다.
    /// </summary>
    private IEnumerator WatchPlayerSwitch()
    {
        int lastSelectedSlot = TestGameManager.SelectedSlot;

        while (_isTestMode && TestGameManager.Instance != null)
        {
            // 슬롯이 변경되었는지 확인
            if (lastSelectedSlot != TestGameManager.SelectedSlot)
            {
                lastSelectedSlot = TestGameManager.SelectedSlot;
                
                // 새로운 플레이어로 전환
                var newPlayer = TestGameManager.Instance.GetSelectedPlayer();
                if (newPlayer != null && newPlayer != _localPlayer)
                {
                    SwitchPlayer(newPlayer);
                }
            }

            yield return new WaitForSeconds(0.1f);
        }
    }

    /// <summary>
    /// 플레이어를 설정합니다.
    /// </summary>
    private void SetupPlayer(PlayerController player)
    {
        if (player == null) return;

        // 이전 플레이어가 있으면 이벤트 구독 해제
        UnsubscribeFromPlayer();

        _localPlayer = player;

        if (_localPlayer.State == null)
        {
            Debug.LogError("[MainCanvas] PlayerState is null!");
            return;
        }

        // 이벤트 구독
        _localPlayer.State.OnHealthChanged += OnHealthChanged;
        _localPlayer.State.OnDeath += OnPlayerDeath;
        _localPlayer.State.OnRespawned += OnPlayerRespawned;

        // 초기 HP UI 생성
        InitializeHealthUI();

        // 플레이어 이름 설정
        if (_playerNameText != null)
        {
            if (_isTestMode)
            {
                _playerNameText.text = $"Player {TestGameManager.SelectedSlot + 1}";
            }
            else
            {
                _playerNameText.text = $"Player {player.Object.InputAuthority}";
            }
            _playerNameText.color = Color.white;
        }
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
        }
    }

    /// <summary>
    /// 다른 플레이어로 전환합니다 (테스트 모드).
    /// </summary>
    private void SwitchPlayer(PlayerController newPlayer)
    {
        Debug.Log($"[MainCanvas] Switching to Player {TestGameManager.SelectedSlot + 1}");
        SetupPlayer(newPlayer);
    }

    /// <summary>
    /// HP UI를 초기화합니다.
    /// </summary>
    private void InitializeHealthUI()
    {
        if (_localPlayer == null)
        {
            Debug.LogError("[MainCanvas] Cannot initialize health UI - PlayerController is null");
            return;
        }

        // 기존 하트 이미지 제거
        ClearHealthUI();

        // 최대 체력에 맞춰 하트 이미지 생성
        int maxHearts = Mathf.CeilToInt(_localPlayer.MaxHealth); // 1HP당 하트 1개
        
        if (maxHearts <= 0)
        {
            Debug.LogWarning($"[MainCanvas] MaxHealth is {_localPlayer.MaxHealth}, no hearts to create");
            return;
        }
        
        for (int i = 0; i < maxHearts; i++)
        {
            GameObject hpObj = Instantiate(_hpImgObjPrefab, _hpContent);
            Image hpImage = hpObj.GetComponent<Image>();
            
            if (hpImage == null)
            {
                hpImage = hpObj.AddComponent<Image>();
            }
            
            // Filled 타입으로 설정 (fillAmount 사용을 위해)
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

    /// <summary>
    /// HP 변경 이벤트 핸들러
    /// </summary>
    private void OnHealthChanged(float current, float max)
    {
        UpdateHealthUI(current, max);
    }

    /// <summary>
    /// HP UI를 업데이트합니다.
    /// </summary>
    private void UpdateHealthUI(float currentHealth, float maxHealth)
    {
        if (_hpImages.Count == 0) return;

        // 하트당 HP 계산 (1HP = 1하트)
        float healthPerHeart = 1f;
        float totalHearts = maxHealth / healthPerHeart;

        for (int i = 0; i < _hpImages.Count; i++)
        {
            float heartThreshold = (i + 1) * healthPerHeart;
            
            if (currentHealth >= heartThreshold)
            {
                // 완전히 채워진 하트
                _hpImages[i].sprite = _filledHeart;
                _hpImages[i].fillAmount = 1f;
            }
            else if (currentHealth > i * healthPerHeart)
            {
                // 부분적으로 채워진 하트 (소수점 체력인 경우)
                _hpImages[i].sprite = _filledHeart;
                float fillAmount = (currentHealth - (i * healthPerHeart)) / healthPerHeart;
                _hpImages[i].fillAmount = fillAmount;
            }
            else
            {
                // 빈 하트
                _hpImages[i].sprite = _emptyHeart;
                _hpImages[i].fillAmount = 1f;
            }
        }
    }

    /// <summary>
    /// 플레이어 사망 이벤트 핸들러
    /// </summary>
    private void OnPlayerDeath(Fusion.PlayerRef killer)
    {
        Debug.Log($"[MainCanvas] Player died! Killer: {killer}");
        
        // 사망 UI 표시 (추가 구현 가능)
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
        Debug.Log($"[MainCanvas] Player respawned!");
        
        // UI 초기화
        if (_playerNameText != null)
        {
            if (_isTestMode)
            {
                _playerNameText.text = $"Player {TestGameManager.SelectedSlot + 1}";
            }
            else
            {
                _playerNameText.text = $"Player {_localPlayer.Object.InputAuthority}";
            }
            _playerNameText.color = Color.white;
        }

        UpdateHealthUI(_localPlayer.CurrentHealth, _localPlayer.MaxHealth);
    }

    void OnDestroy()
    {
        // 이벤트 구독 해제
        UnsubscribeFromPlayer();
    }

    #region Additional UI Methods
    /// <summary>
    /// 플레이어 이름을 설정합니다.
    /// </summary>
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
    #endregion
}
