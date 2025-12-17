using UnityEngine;

/// <summary>
/// 전역 비주얼 리소스를 관리하는 싱글톤 매니저입니다.
/// - 폭발 범위 표시용 1x1 원형 스프라이트 등을 보관합니다.
/// - GameManager에서 프리팹으로 생성되어 DontDestroyOnLoad 됩니다.
/// </summary>
public class VisualManager : MonoBehaviour
{
    private static VisualManager _instance;
    public static VisualManager Instance
    {
        get => _instance;
    }

    [Header("Explosion Range Visual")]
    [SerializeField] private GameObject _explosionRangePrefab;

    /// <summary>
    /// 폭발 범위 표시용 오브젝트 프리팹 (null일 수 있음)
    /// </summary>
    public GameObject ExplosionRangePrefab => _explosionRangePrefab;

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    public void OnInitialize(GameManager gameManager)
    {

    }
}

