using UnityEngine;

/// <summary>
/// 플레이어의 적 감시 범위 트리거 이벤트를 처리하는 컴포넌트입니다.
/// 별도의 GameObject에 부착하여 다른 트리거와 충돌하지 않도록 합니다.
/// </summary>
public class PlayerDetectionTrigger : MonoBehaviour
{
    private PlayerDetectionManager _detectionManager;
    
    /// <summary>
    /// 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController playerController)
    {
        if (playerController != null)
        {
            _detectionManager = playerController.GetComponent<PlayerDetectionManager>();
        }
    }
    
    /// <summary>
    /// 트리거에 진입한 적을 등록합니다.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_detectionManager == null) return;
        
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
            _detectionManager.OnEnemyEnter(enemyDetector);
        }
    }
    
    /// <summary>
    /// 트리거에서 나간 적을 제거합니다.
    /// </summary>
    private void OnTriggerExit2D(Collider2D other)
    {
        if (_detectionManager == null) return;
        
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
            _detectionManager.OnEnemyExit(enemyDetector);
        }
    }
}
