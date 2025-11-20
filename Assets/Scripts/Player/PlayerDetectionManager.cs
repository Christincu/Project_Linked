using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어의 적 감시 범위 트리거를 관리하는 컴포넌트
/// </summary>
public class PlayerDetectionManager : MonoBehaviour
{
    #region Private Fields
    private PlayerController _controller;
    private GameDataManager _gameDataManager;
    private HashSet<EnemyDetector> _nearbyEnemies = new HashSet<EnemyDetector>();
    private GameObject _detectionTriggerObj;
    private CircleCollider2D _detectionTriggerCollider;
    private float _detectionTriggerRange = 10f;
    #endregion

    #region Properties
    public float DetectionTriggerRange => _detectionTriggerRange;
    #endregion

    #region Initialization
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
    #endregion

    #region Detection Trigger Setup
    /// <summary>
    /// 적 감시 범위 트리거 콜리더를 초기화합니다.
    /// </summary>
    private void InitializeDetectionTrigger()
    {
        // 별도의 자식 GameObject 생성
        if (_detectionTriggerObj == null)
        {
            _detectionTriggerObj = new GameObject("DetectionTrigger");
            _detectionTriggerObj.transform.SetParent(_controller.transform);
            _detectionTriggerObj.transform.localPosition = Vector3.zero;
            _detectionTriggerObj.transform.localRotation = Quaternion.identity;
            _detectionTriggerObj.transform.localScale = Vector3.one;
            
            // 별도의 레이어로 설정 (기본 레이어 사용, Physics2D 설정에서 충돌 제어)
            _detectionTriggerObj.layer = _controller.gameObject.layer; // 플레이어와 같은 레이어 사용
            
            // CircleCollider2D 추가
            _detectionTriggerCollider = _detectionTriggerObj.AddComponent<CircleCollider2D>();
            _detectionTriggerCollider.isTrigger = true;
            _detectionTriggerCollider.radius = _detectionTriggerRange;
            
            // Rigidbody2D 추가 (트리거 작동을 위해 필요)
            Rigidbody2D rb = _detectionTriggerObj.AddComponent<Rigidbody2D>();
            rb.isKinematic = true;
            rb.gravityScale = 0f;
            
            // 트리거 이벤트 처리 컴포넌트 추가
            PlayerDetectionTrigger triggerComponent = _detectionTriggerObj.AddComponent<PlayerDetectionTrigger>();
            triggerComponent.Initialize(_controller);
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
    #endregion

    #region Enemy Detection
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
    public void OnEnemyEnter(EnemyDetector enemyDetector)
    {
        if (enemyDetector != null)
        {
            _nearbyEnemies.Add(enemyDetector);
        }
    }
    
    /// <summary>
    /// 트리거에서 나간 적을 제거합니다.
    /// </summary>
    public void OnEnemyExit(EnemyDetector enemyDetector)
    {
        if (enemyDetector != null)
        {
            _nearbyEnemies.Remove(enemyDetector);
        }
    }
    #endregion
}

