using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 게임 전반에 걸쳐 사용되는 데이터를 관리하는 매니저입니다.
/// 플레이어 초기 데이터, 캐릭터 서비스 등을 제공합니다.
/// </summary>
public class GameDataManager : MonoBehaviour
{
    [Header("Player Data")]
    [SerializeField] private InitialPlayerData _initialPlayerData = new InitialPlayerData();
    
    [Header("Services")]
    [HideInInspector]
    [SerializeField] private CharacterService _characterService = new CharacterService();
    [HideInInspector]
    [SerializeField] private EnemyService _enemyService = new EnemyService();
    [HideInInspector]
    [SerializeField] private StageService _stageService = new StageService();

    public InitialPlayerData InitialPlayerData => _initialPlayerData;
    public CharacterService CharacterService => _characterService;
    public EnemyService EnemyService => _enemyService;
    public StageService StageService => _stageService;

    public static GameDataManager Instance { get; private set; }
    public bool IsInitialized { get; private set; }

    private GameManager _gameManager;

    /// <summary>
    /// GameDataManager를 초기화합니다. (GameManager에서 호출)
    /// </summary>
    public void OnInitialize(GameManager gameManager)
    {
        _gameManager = gameManager;

        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        _characterService.InitializeDictionary();
        _enemyService.InitializeDictionary();
        _stageService.InitializeDictionary();
        
        IsInitialized = true;
    }

    /// <summary>
    /// 인스펙터 우클릭 메뉴에 추가됩니다.
    /// 에디터에서 이 버튼을 눌러 데이터를 갱신하고 Ctrl+S로 씬을 저장하세요.
    /// </summary>
    [ContextMenu("Load All Data From Assets")]
    public void LoadAllDataFromAssets()
    {
        _characterService.LoadDataFromAssets(this);
        _enemyService.LoadDataFromAssets(this);
        _stageService.LoadDataFromAssets(this);
        EditorUtility.SetDirty(this);
        Debug.Log("[GameDataManager] All data loaded successfully. Please save the scene (Ctrl+S)!");
    }
}