using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어의 적 감시 범위 트리거를 관리하는 컴포넌트입니다.
/// 별도의 GameObject에 부착하여 다른 트리거와 충돌하지 않도록 합니다.
/// </summary>
public class PlayerDetectionTrigger : MonoBehaviour
{
    private PlayerController _controller;
    private GameDataManager _gameDataManager;
    private HashSet<EnemyDetector> _nearbyEnemies = new HashSet<EnemyDetector>();
    private CircleCollider2D _detectionTriggerCollider;
    private float _detectionTriggerRange = 10f;

    public float DetectionTriggerRange => _detectionTriggerRange;

    /// <summary>
    /// 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController controller, GameDataManager gameDataManager)
    {
        _controller = controller;
        _gameDataManager = gameDataManager;
        
        InitializeDetectionTrigger();
        UpdateDetectionTriggerRange();
    }

    /// <summary>
    /// 적 감시 범위 트리거 콜리더를 초기화합니다.
    /// </summary>
    private void InitializeDetectionTrigger()
    {
        if (_detectionTriggerCollider == null)
        {
            _detectionTriggerCollider = GetComponent<CircleCollider2D>();
            if (_detectionTriggerCollider == null)
            {
                _detectionTriggerCollider = gameObject.AddComponent<CircleCollider2D>();
            }
            
            _detectionTriggerCollider.isTrigger = true;
            _detectionTriggerCollider.radius = _detectionTriggerRange;
            
            if (gameObject.layer == 0)
            {
                gameObject.layer = 15;
            }
        }
    }
    
    /// <summary>
    /// 적 감시 범위 트리거 반경을 설정합니다.
    /// </summary>
    public void SetDetectionTriggerRange(float range)
    {
        _detectionTriggerRange = range;
        if (_detectionTriggerCollider != null)
        {
            _detectionTriggerCollider.radius = range;
        }
    }
    
    /// <summary>
    /// 모든 적의 트리거 범위를 확인하여 플레이어의 트리거 범위를 업데이트합니다.
    /// </summary>
    public void UpdateDetectionTriggerRange()
    {
        if (_gameDataManager == null) return;
        
        float maxTriggerRange = 10f; // 기본값
        
        // 모든 적 데이터 확인
        var allEnemies = _gameDataManager.EnemyService.GetAllEnemies();
        foreach (var enemyData in allEnemies)
        {
            if (enemyData != null && enemyData.visualizationTriggerRange > maxTriggerRange)
            {
                maxTriggerRange = enemyData.visualizationTriggerRange;
            }
        }
        
        // 트리거 범위 설정
        SetDetectionTriggerRange(maxTriggerRange);
    }

    /// <summary>
    /// 특정 적이 근처에 있는지 확인합니다.
    /// </summary>
    public bool IsEnemyNearby(EnemyDetector enemy)
    {
        return _nearbyEnemies.Contains(enemy);
    }
    
    /// <summary>
    /// 트리거에 진입한 적을 등록합니다.
    /// </summary>
    private void OnEnemyEnter(EnemyDetector enemyDetector)
    {
        if (enemyDetector != null)
        {
            _nearbyEnemies.Add(enemyDetector);
        }
    }
    
    /// <summary>
    /// 트리거에서 나간 적을 제거합니다.
    /// </summary>
    private void OnEnemyExit(EnemyDetector enemyDetector)
    {
        if (enemyDetector != null)
        {
            _nearbyEnemies.Remove(enemyDetector);
        }
    }
    
    /// <summary>
    /// 트리거에 진입한 적을 등록합니다.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        // MapTeleporter나 다른 트리거 오브젝트는 무시
        if (other.GetComponent<MapTeleporter>() != null) return;
        
        // 적의 루트나 부모에서 EnemyDetector 찾기
        EnemyDetector enemyDetector = other.GetComponent<EnemyDetector>();
        if (enemyDetector == null)
        {
            enemyDetector = other.GetComponentInParent<EnemyDetector>();
        }
        if (enemyDetector == null && other.attachedRigidbody != null)
        {
            enemyDetector = other.attachedRigidbody.GetComponent<EnemyDetector>();
        }
        if (enemyDetector == null && other.transform.root != null)
        {
            enemyDetector = other.transform.root.GetComponent<EnemyDetector>();
        }
        
        // EnemyDetector가 있는 경우에만 처리 (적만 감지)
        if (enemyDetector != null)
        {
            OnEnemyEnter(enemyDetector);
        }
    }
    
    /// <summary>
    /// 트리거에서 나간 적을 제거합니다.
    /// </summary>
    private void OnTriggerExit2D(Collider2D other)
    {
        // MapTeleporter나 다른 트리거 오브젝트는 무시
        if (other.GetComponent<MapTeleporter>() != null) return;
        
        // 적의 루트나 부모에서 EnemyDetector 찾기
        EnemyDetector enemyDetector = other.GetComponent<EnemyDetector>();
        if (enemyDetector == null)
        {
            enemyDetector = other.GetComponentInParent<EnemyDetector>();
        }
        if (enemyDetector == null && other.attachedRigidbody != null)
        {
            enemyDetector = other.attachedRigidbody.GetComponent<EnemyDetector>();
        }
        if (enemyDetector == null && other.transform.root != null)
        {
            enemyDetector = other.transform.root.GetComponent<EnemyDetector>();
        }
        
        // EnemyDetector가 있는 경우에만 처리 (적만 감지)
        if (enemyDetector != null)
        {
            OnEnemyExit(enemyDetector);
        }
    }
}
