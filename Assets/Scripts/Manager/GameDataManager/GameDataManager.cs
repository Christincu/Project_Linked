using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 전반에 걸쳐 사용되는 데이터를 관리하는 매니저입니다.
/// 플레이어 초기 데이터, 캐릭터 서비스 등을 제공합니다.
/// </summary>
public class GameDataManager : MonoBehaviour
{
    [Header("Sprite")]
    [SerializeField] private Material _defaltSpriteMat;

    [Header("Player Data")]
    [SerializeField] private InitialPlayerData _initialPlayerData = new InitialPlayerData();
    
    [Header("Services")]
    [HideInInspector]
    [SerializeField] private CharacterService _characterService = new CharacterService();
    [HideInInspector]
    [SerializeField] private MagicService _magicService = new MagicService();
    [HideInInspector]
    [SerializeField] private EnemyService _enemyService = new EnemyService();
    [HideInInspector]
    [SerializeField] private StageService _stageService = new StageService();

    #region Properties
    public Material DefaltSpriteMat => _defaltSpriteMat;

    /// <summary>
    /// 플레이어 초기 데이터에 대한 읽기 전용 접근을 제공합니다.
    /// </summary>
    public InitialPlayerData InitialPlayerData => _initialPlayerData;
    
    /// <summary>
    /// 캐릭터 서비스에 대한 접근을 제공합니다.
    /// </summary>
    public CharacterService CharacterService => _characterService;
    
    /// <summary>
    /// 마법 서비스에 대한 접근을 제공합니다.
    /// </summary>
    public MagicService MagicService => _magicService;
    
    /// <summary>
    /// 적 서비스에 대한 접근을 제공합니다.
    /// </summary>
    public EnemyService EnemyService => _enemyService;
    
    /// <summary>
    /// 스테이지 서비스에 대한 접근을 제공합니다.
    /// </summary>
    public StageService StageService => _stageService;
    #endregion

    #region Singleton
    /// <summary>
    /// GameDataManager의 싱글톤 인스턴스입니다.
    /// </summary>
    public static GameDataManager Instance { get; private set; }
    
    /// <summary>
    /// 초기화 완료 여부를 나타냅니다.
    /// </summary>
    public bool IsInitialized { get; private set; }
    #endregion
    
    void Awake()
    {
        // 싱글톤 인스턴스만 설정하고, 초기화는 GameManager에서 제어
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// GameDataManager를 초기화합니다. (GameManager에서 호출)
    /// </summary>
    public void Initialize()
    {
        if (IsInitialized)
        {
            Debug.LogWarning("[GameDataManager] Already initialized. Skipping.");
            return;
        }
        
        ValidateData();
        
        // 각 서비스에서 데이터 자동 로드 (에디터에서만 작동)
        _characterService.LoadDataFromAssets();
        _enemyService.LoadDataFromAssets();
        _magicService.LoadDataFromAssets();
        _stageService.LoadDataFromAssets();
        
        // 각 서비스의 딕셔너리 초기화
        _characterService.InitializeDictionary();
        _magicService.InitializeDictionary();
        _enemyService.InitializeDictionary();
        _stageService.InitializeDictionary();
        
        IsInitialized = true;
        Debug.Log("[GameDataManager] Initialized");
    }

    /// <summary>
    /// 모든 데이터의 유효성을 검증합니다.
    /// </summary>
    private void ValidateData()
    {
        if (_initialPlayerData != null)
        {
            _initialPlayerData.Validate();
        }
        else
        {
            Debug.LogWarning("[GameDataManager] InitialPlayerData가 null입니다. 기본값으로 초기화합니다.");
            _initialPlayerData = new InitialPlayerData();
            _initialPlayerData.SetDefaults();
        }
    }

    #region Editor Methods
#if UNITY_EDITOR
    /// <summary>
    /// Unity Inspector에서 Reset 버튼을 누를 때 호출됩니다.
    /// </summary>
    private void Reset()
    {
        if (_initialPlayerData == null)
        {
            _initialPlayerData = new InitialPlayerData();
        }
        _initialPlayerData.SetDefaults();
        Debug.Log("[GameDataManager] 데이터가 기본값으로 리셋되었습니다.");
    }

    /// <summary>
    /// Unity Inspector에서 값이 변경될 때 호출됩니다.
    /// </summary>
    private void OnValidate()
    {
        if (_initialPlayerData != null)
        {
            _initialPlayerData.Validate();
        }
    }
#endif
    #endregion
}
