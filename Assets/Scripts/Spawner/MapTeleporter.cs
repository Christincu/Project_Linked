using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion; // Fusion 사용을 위해 추가

/// <summary>
/// 맵 내부 순간이동 시스템 (양방향 지원)
/// 플레이어가 이 위치에 도달하면 연결된 다른 MapTeleporter로 순간이동합니다.
/// </summary>
public class MapTeleporter : MonoBehaviour
{
    #region Serialized Fields
    [Header("Teleport Settings")]
    [Tooltip("순간이동 목적지 (다른 MapTeleporter)")]
    [SerializeField] private MapTeleporter targetTeleporter;
    
    [Tooltip("양방향 텔레포트 활성화 (목적지에서도 이곳으로 돌아올 수 있음)")]
    [SerializeField] private bool isBidirectional = false;
    
    [Tooltip("순간이동 쿨다운 (초)")]
    [SerializeField] private float cooldown = 1f;
    
    [Tooltip("텔레포트 실행 전 대기 시간 (로딩 화면 페이드 인)")]
    [SerializeField] private float teleportDelay = 0.3f;
    
    [Tooltip("스폰 위치 오프셋")]
    [SerializeField] private Vector2 spawnOffset = Vector2.zero;
    
    [Header("Visual Settings")]
    [Tooltip("트리거 영역 표시 색상")]
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 1f, 0.3f);
    
    [Tooltip("스폰 지점 표시 색상")]
    [SerializeField] private Color spawnGizmoColor = new Color(0f, 1f, 0f, 0.5f);
    
    [Tooltip("Gizmo 크기")]
    [SerializeField] private float gizmoSize = 1f;
    #endregion
    
    #region Private Fields
    private BoxCollider2D _triggerCollider;
    #endregion
    
    #region Unity Lifecycle
    void Start()
    {
        InitializeTrigger();
        SetupBidirectional();
    }
    
    // Update에서 쿨다운 관리 로직 제거
    
    void OnTriggerEnter2D(Collider2D other)
    {
        HandlePlayerEnter(other);
    }
    #endregion
    
    #region Initialization
    private void InitializeTrigger()
    {
        // BoxCollider2D가 없으면 추가
        _triggerCollider = GetComponent<BoxCollider2D>();
        if (_triggerCollider == null)
        {
            _triggerCollider = gameObject.AddComponent<BoxCollider2D>();
        }
        
        _triggerCollider.isTrigger = true;
        
        if (targetTeleporter == null)
        {
            Debug.LogWarning($"[MapTeleporter] targetTeleporter is not set on {gameObject.name}!");
        }
    }
    
    private void SetupBidirectional()
    {
        if (isBidirectional && targetTeleporter != null)
        {
            if (targetTeleporter.targetTeleporter == null)
            {
                // 목적지가 이곳을 타겟으로 설정
                targetTeleporter.targetTeleporter = this;
            }
        }
    }
    #endregion
    
    #region Trigger Handling
    private void HandlePlayerEnter(Collider2D other)
    {
        // PlayerController 확인
        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) return;
        
        // 네트워크 권한 확인 - 서버나 State Authority만 텔레포트 실행
        // 이 로직은 FixedUpdateNetwork 외부에서 서버가 텔레포트 권한을 갖도록 보장합니다.
        if (player.Object == null || !player.Object.HasStateAuthority)
        {
            return;
        }
        
        if (targetTeleporter == null)
        {
            Debug.LogWarning($"[MapTeleporter] targetTeleporter is not set!");
            return;
        }
        
        if (!CanTeleport(player))
        {
            return;
        }
        
        TeleportPlayer(player);
    }
    
    private bool CanTeleport(PlayerController player)
    {
        if (player.Runner == null) return false;
        
        bool canTeleport = player.TeleportCooldownTimer.ExpiredOrNotRunning(player.Runner);
        
        return canTeleport;
    }
    
    private void TeleportPlayer(PlayerController player)
    {
        StartCoroutine(TeleportPlayerCoroutine(player));
    }
    
    private IEnumerator TeleportPlayerCoroutine(PlayerController player)
    {
        // 로딩 화면 표시 (자동으로 Show -> 대기 -> Hide)
        LoadingPanel.ShowForSeconds(teleportDelay);
        
        // 로딩 화면 페이드 인 대기
        yield return new WaitForSeconds(teleportDelay);
        
        Vector3 spawnPosition = targetTeleporter.GetSpawnPosition();
        player.RequestTeleport(spawnPosition);
        
        SetCooldown(player);
        targetTeleporter.SetCooldown(player);
        
        Debug.Log($"[MapTeleporter] Teleported {player.name} to {targetTeleporter.gameObject.name}. Cooldown: {cooldown}s");
    }
    #endregion
    
    #region Public Methods
    /// <summary>
    /// 스폰 위치를 반환합니다.
    /// </summary>
    public Vector3 GetSpawnPosition()
    {
        return transform.position + (Vector3)spawnOffset;
    }
    
    /// <summary>
    /// 특정 플레이어에게 쿨다운을 설정합니다. (TickTimer 사용)
    /// </summary>
    public void SetCooldown(PlayerController player)
    {
        if (player != null && player.Runner != null)
        {
            player.TeleportCooldownTimer = TickTimer.CreateFromSeconds(player.Runner, cooldown);
        }
    }
    #endregion
    
    #region Gizmos
    void OnDrawGizmos()
    {
        // 트리거 영역 표시
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null)
        {
            Gizmos.color = gizmoColor;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(col.offset, col.size);
        }
        
        // 스폰 지점 표시
        Vector3 spawnPos = GetSpawnPosition();
        Gizmos.color = spawnGizmoColor;
        Gizmos.DrawWireSphere(spawnPos, gizmoSize);
        Gizmos.DrawSphere(spawnPos, gizmoSize * 0.2f);
        
        // 목적지 화살표 표시
        if (targetTeleporter != null)
        {
            Gizmos.color = isBidirectional ? Color.yellow : Color.cyan;
            Vector3 start = transform.position;
            Vector3 end = targetTeleporter.transform.position;
            DrawArrow(start, end);
            
            // 양방향이면 반대 화살표도 표시
            if (isBidirectional)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
                DrawArrow(end, start);
            }
        }
    }
    
    void OnDrawGizmosSelected()
    {
        // 선택 시 더 명확한 표시
        Vector3 spawnPos = GetSpawnPosition();
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(spawnPos, gizmoSize * 1.5f);
        
        // 위쪽 화살표 표시 (방향 표시)
        Vector3 arrowStart = spawnPos;
        Vector3 arrowEnd = spawnPos + Vector3.up * gizmoSize * 0.5f;
        
        Gizmos.DrawLine(arrowStart, arrowEnd);
        
        Vector3 left = Quaternion.Euler(0, 0, 30) * Vector3.down * gizmoSize * 0.3f;
        Vector3 right = Quaternion.Euler(0, 0, -30) * Vector3.down * gizmoSize * 0.3f;
        
        Gizmos.DrawLine(arrowEnd, arrowEnd + left);
        Gizmos.DrawLine(arrowEnd, arrowEnd + right);
    }
    
    private void DrawArrow(Vector3 start, Vector3 end)
    {
        Gizmos.DrawLine(start, end);
        
        Vector3 direction = (end - start).normalized;
        Vector3 right = Quaternion.Euler(0, 0, 20) * -direction * 0.5f;
        Vector3 left = Quaternion.Euler(0, 0, -20) * -direction * 0.5f;
        
        Gizmos.DrawLine(end, end + right);
        Gizmos.DrawLine(end, end + left);
    }
    #endregion
}
