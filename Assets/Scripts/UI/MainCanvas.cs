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
    [SerializeField] private TextMeshProUGUI _waveText;
    [SerializeField] private TextMeshProUGUI _goalText;

    public Transform CanvasTransform => transform;

    private List<Image> _hpImages = new List<Image>();
    private GameManager _gameManager;
    private GameDataManager _gameDataManager;
    private PlayerController _localPlayer;
    private bool _isInitialized = false;
    
    // 코루틴 중복 실행 방지를 위한 변수
    private Coroutine _initCoroutine;
    

    public void Initialize(GameManager gameManager, GameDataManager gameDataManager)
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
}
