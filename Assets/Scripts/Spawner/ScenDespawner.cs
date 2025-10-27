using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 씬 전환 트리거 (전체 플레이어 강제 이동)
/// 한 명의 플레이어가 도달하면 모든 플레이어가 새로운 씬으로 이동합니다.
/// 서버만 씬 전환을 실행할 수 있습니다.
/// </summary>
public class ScenDespawner : MonoBehaviour
{
    #region Serialized Fields
    [Header("Scene Transition Settings")]
    [Tooltip("이동할 씬 이름")]
    [SerializeField] private string targetSceneName;

    [Tooltip("씬 전환 쿨다운 (초) - 중복 트리거 방지")]
    [SerializeField] private float cooldown = 2f;
    
    [Tooltip("씬 로드 실행 전 대기 시간 (로딩 화면 페이드 인)")]
    [SerializeField] private float sceneLoadDelay = 0.3f;

    [Header("Visual Settings")]
    [Tooltip("트리거 영역 표시 색상")]
    [SerializeField] private Color gizmoColor = new Color(0f, 0.5f, 1f, 0.3f);

    [Header("UI Settings")]
    [Tooltip("씬 전환 시 표시할 메시지")]
    [SerializeField] private string transitionMessage = "다음 지역으로 이동 중...";
    #endregion

    #region Private Fields
    private BoxCollider2D _triggerCollider;
    private bool _isOnCooldown = false;
    private float _cooldownTimer = 0f;
    private NetworkRunner _runner;
    #endregion

    #region Unity Lifecycle
    void Start()
    {
        InitializeTrigger();
    }

    void Update()
    {
        UpdateCooldown();
    }

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

        if (string.IsNullOrEmpty(targetSceneName))
        {
            Debug.LogWarning($"[ScenDespawner] targetSceneName is not set on {gameObject.name}!");
        }
    }
    #endregion

    #region Trigger Handling
    private void HandlePlayerEnter(Collider2D other)
    {
        // 쿨다운 확인
        if (_isOnCooldown) return;

        // PlayerController 확인
        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) return;

        // NetworkRunner 확인
        if (_runner == null)
        {
            _runner = FusionManager.LocalRunner ?? FindObjectOfType<NetworkRunner>();
        }

        if (_runner == null)
        {
            Debug.LogError($"[ScenDespawner] NetworkRunner not found!");
            return;
        }

        // 서버만 씬 전환 실행 (클라이언트는 서버의 씬 로드를 따라감)
        if (!_runner.IsServer)
        {
            return;
        }

        // 씬 이름 확인
        if (string.IsNullOrEmpty(targetSceneName))
        {
            Debug.LogWarning($"[ScenDespawner] targetSceneName is not set!");
            return;
        }

        // 씬 전환 실행
        TransitionToScene();
    }

    private void TransitionToScene()
    {
        _isOnCooldown = true;
        _cooldownTimer = cooldown;

        Debug.Log($"[ScenDespawner] Scene transition started: {targetSceneName}");

        LoadingPanel.Show();
        StartCoroutine(LoadSceneAfterDelay());
    }
    
    private IEnumerator LoadSceneAfterDelay()
    {
        // 로딩 화면 페이드 인 대기
        yield return new WaitForSeconds(sceneLoadDelay);
        
        _runner.LoadScene(targetSceneName);
    }
    #endregion

    #region Cooldown Management
    private void UpdateCooldown()
    {
        if (_isOnCooldown)
        {
            _cooldownTimer -= Time.deltaTime;

            if (_cooldownTimer <= 0f)
            {
                _isOnCooldown = false;
            }
        }
    }
    #endregion

    #region Gizmos
#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null)
        {
            Gizmos.color = gizmoColor;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(col.offset, col.size);
        }
        
        // 씬 전환 표시
        Gizmos.color = Color.cyan;
        Vector3 pos = transform.position;
        
        // 포털 모양 표시
        float size = 2f;
        DrawPortalEffect(pos, size);
    }
    
    void OnDrawGizmosSelected()
    {
        // 선택 시 씬 이름 표시 (디버깅용)
        if (!string.IsNullOrEmpty(targetSceneName))
        {
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, $"→ {targetSceneName}");
        }
    }
    
    private void DrawPortalEffect(Vector3 center, float size)
    {
        // 포털 링 그리기
        int segments = 20;
        Vector3 prevPoint = center + new Vector3(Mathf.Cos(0) * size, Mathf.Sin(0) * size, 0);
        
        for (int i = 1; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 newPoint = center + new Vector3(Mathf.Cos(angle) * size, Mathf.Sin(angle) * size, 0);
            Gizmos.DrawLine(prevPoint, newPoint);
            prevPoint = newPoint;
        }
    }
#endif
    #endregion
}
