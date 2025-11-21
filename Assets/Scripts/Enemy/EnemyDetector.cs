using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 적의 플레이어 탐지 시스템입니다.
/// 시야 범위와 벽 장애물을 고려하여 플레이어를 탐지합니다.
/// </summary>
public class EnemyDetector : MonoBehaviour
{
    #region Private Fields
    private EnemyController _controller;
    private EnemyData _enemyData;
    private PlayerController _detectedPlayer;
    private Vector2 _lastDetectedPosition;
    
    // 런타임 시각화용 Mesh
    private MeshFilter _detectionRangeMeshFilter;
    private MeshRenderer _detectionRangeMeshRenderer;
    private Mesh _fieldOfViewMesh;
    private bool _isRangeVisible = false;
    private float _lastDetectionRange = -1f;
    private Vector3 _lastPosition;
    private float _lastRotation;
    [Header("Performance Settings")]
    [Tooltip("시야 범위 업데이트 주기 (초) - 유휴 상태")]
    [SerializeField] private float _updateIntervalIdle = 0.2f;
    [Tooltip("시야 범위 업데이트 주기 (초) - 탐지 중")]
    [SerializeField] private float _updateIntervalDetected = 0.08f;
    private float _lastUpdateTime = 0f;
    private Dictionary<PlayerController, Vector3> _lastPlayerPositions = new Dictionary<PlayerController, Vector3>();
    private bool _wasPlayerDetected = false;
    
    [Header("Detection Settings")]
    [SerializeField] private LayerMask _playerLayerMask;
    [SerializeField] private LayerMask _obstacleLayerMask;
    
    [Header("Visualization Settings")]
    [Tooltip("에디터에서 탐지 범위 표시 여부")]
    [SerializeField] private bool _showDetectionRange = true;
    
    [Tooltip("런타임에서 탐지 범위 표시 여부")]
    [SerializeField] private bool _showRuntimeRange = true;
    
    [Tooltip("시야 범위를 표시할 트리거 범위 (플레이어가 이 범위 안에 들어가면 시야 표시)")]
    [SerializeField] private float _visualizationTriggerRange = 10f;
    
    [Tooltip("항상 시야 범위를 표시할지 여부 (false면 플레이어가 범위 안에 있을 때만 표시)")]
    [SerializeField] private bool _alwaysShowRange = false;
    
    [Tooltip("탐지 범위 표시 색상")]
    [SerializeField] private Color _detectionRangeColor = new Color(1f, 0f, 0f, 0.3f);
    
    [Tooltip("탐지 중인 플레이어 표시 색상")]
    [SerializeField] private Color _detectedPlayerColor = new Color(1f, 0.5f, 0f, 0.5f);
    
    [Tooltip("트리거 범위 표시 색상 (선택 시 표시)")]
    [SerializeField] private Color _triggerRangeColor = new Color(0f, 1f, 0f, 0.2f);
    
    [Tooltip("원형 범위를 그릴 세그먼트 수 (유휴 상태)")]
    [SerializeField] private int _segmentsWhenIdle = 24;
    [Tooltip("원형 범위를 그릴 세그먼트 수 (탐지 중)")]
    [SerializeField] private int _segmentsWhenDetected = 64;
    #endregion

    #region Properties
    public PlayerController DetectedPlayer => _detectedPlayer;
    public bool HasDetectedPlayer => _detectedPlayer != null;
    public Vector2 LastDetectedPosition => _lastDetectedPosition;
    #endregion

    #region Initialization
    /// <summary>
    /// 초기화합니다.
    /// </summary>
    public void Initialize(EnemyController controller, EnemyData enemyData)
    {
        _controller = controller;
        _enemyData = enemyData;
        
        // 기본 레이어 설정
        if (_playerLayerMask.value == 0)
        {
            _playerLayerMask = LayerMask.GetMask("Player");
        }
        
        if (_obstacleLayerMask.value == 0)
        {
            _obstacleLayerMask = LayerMask.GetMask("Ignore Raycast"); // 벽이 있는 레이어
        }
        
        // EnemyData의 시각화 트리거 범위를 사용 (설정되어 있으면)
        if (_enemyData != null && _enemyData.visualizationTriggerRange > 0f)
        {
            _visualizationTriggerRange = _enemyData.visualizationTriggerRange;
        }
        
        // 런타임 시각화 초기화
        InitializeRuntimeVisualization();
    }
    
    /// <summary>
    /// 런타임 시각화를 위한 Mesh를 초기화합니다.
    /// </summary>
    private void InitializeRuntimeVisualization()
    {
        if (!_showRuntimeRange) return;
        
        // 탐지 범위 Mesh 생성
        GameObject detectionRangeObj = new GameObject("DetectionRangeMesh");
        detectionRangeObj.transform.SetParent(transform);
        detectionRangeObj.transform.localPosition = Vector3.zero;
        
        _detectionRangeMeshFilter = detectionRangeObj.AddComponent<MeshFilter>();
        _detectionRangeMeshRenderer = detectionRangeObj.AddComponent<MeshRenderer>();
        
        // 메시 생성
        _fieldOfViewMesh = new Mesh();
        _fieldOfViewMesh.name = "FieldOfViewMesh";
        _detectionRangeMeshFilter.mesh = _fieldOfViewMesh;
        
        // 머티리얼 설정 (반투명)
        Material material = new Material(Shader.Find("Sprites/Default"));
        material.color = _detectionRangeColor;
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000; // Transparent
        _detectionRangeMeshRenderer.material = material;
        _detectionRangeMeshRenderer.sortingOrder = 1;
        
        // 초기화
        _lastDetectionRange = -1f; // 강제로 첫 번째 그리기 실행
        _lastPosition = transform.position;
        _lastRotation = transform.eulerAngles.z;
        _lastUpdateTime = Time.time;
        UpdateRangeVisualization();
        UpdateRangeVisibility(false); // 초기에는 숨김
    }
    #endregion

    #region Detection
    /// <summary>
    /// 플레이어를 탐지합니다.
    /// </summary>
    public bool DetectPlayer(out PlayerController detectedPlayer)
    {
        detectedPlayer = null;
        
        if (_controller == null || _controller.Runner == null) return false;
        if (_enemyData == null) return false; // EnemyData가 없으면 탐지 불가

        // 모든 플레이어를 가져옴
        List<PlayerController> allPlayers = new List<PlayerController>();
        
        if (MainGameManager.Instance != null)
        {
            allPlayers = MainGameManager.Instance.GetAllPlayers();
        }
        
        // 보조 안전망: 리스트가 비면 씬에서 직접 검색
        if (allPlayers == null || allPlayers.Count == 0)
        {
            allPlayers = new List<PlayerController>(FindObjectsOfType<PlayerController>());
        }

        // 위협점수 기반으로 플레이어 목록 정렬
        List<(PlayerController player, float threatScore, float distance)> detectablePlayers = new List<(PlayerController, float, float)>();
        
        foreach (var player in allPlayers)
        {
            if (player == null || player.IsDead) continue;

            Vector2 playerPos = (Vector2)player.transform.position;
            Vector2 enemyPos = (Vector2)transform.position;
            Vector2 direction = playerPos - enemyPos;
            float distance = direction.magnitude;

            // 1. 거리 체크
            if (distance > _enemyData.detectionRange) continue;

            // 2. 각도 체크 제거 (360도 전방위 탐지)

            // 3. 시야 체크 (레이캐스트로 장애물 확인)
            if (!HasLineOfSight(enemyPos, playerPos)) continue;

            // 탐지 가능한 플레이어 목록에 추가 (위협점수와 거리 정보 포함)
            float threatScore = player.ThreatScore;
            detectablePlayers.Add((player, threatScore, distance));
        }
        
        // 탐지 가능한 플레이어가 없으면 false 반환
        if (detectablePlayers.Count == 0)
        {
            return false;
        }
        
        // 위협점수 높은 순으로 정렬 (같으면 거리 가까운 순)
        detectablePlayers.Sort((a, b) =>
        {
            // 위협점수 비교 (높은 순)
            int threatComparison = b.threatScore.CompareTo(a.threatScore);
            if (threatComparison != 0) return threatComparison;
            
            // 위협점수가 같으면 거리 가까운 순
            return a.distance.CompareTo(b.distance);
        });
        
        // 가장 위협점수가 높은 플레이어 선택
        var selectedPlayer = detectablePlayers[0];
        detectedPlayer = selectedPlayer.player;
        _detectedPlayer = selectedPlayer.player;
        _lastDetectedPosition = (Vector2)selectedPlayer.player.transform.position;
        
        return true;
    }

    // 전방위 탐지이므로 전방 계산 함수는 불필요

    /// <summary>
    /// 두 지점 사이에 시야가 있는지 확인합니다 (벽 체크).
    /// </summary>
    private bool HasLineOfSight(Vector2 from, Vector2 to)
    {
        Vector2 direction = to - from;
        float distance = direction.magnitude;
        
        // Raycast로 장애물 체크
        RaycastHit2D hit = Physics2D.Raycast(from, direction.normalized, distance, _obstacleLayerMask);
        
        // 장애물이 없으면 시야 있음
        return hit.collider == null;
    }

    /// <summary>
    /// 탐지된 플레이어를 잃었을 때 처리합니다.
    /// </summary>
    public void OnPlayerLost()
    {
        _detectedPlayer = null;
    }
    
    /// <summary>
    /// 로컬 플레이어가 트리거 범위 안에 있는지 확인합니다 (시각화용).
    /// </summary>
    private bool IsLocalPlayerInTriggerRange()
    {
        if (_visualizationTriggerRange <= 0f) return true; // 범위가 0이면 항상 표시
        
        // 로컬 플레이어만 확인
        PlayerController localPlayer = null;
        if (MainGameManager.Instance != null)
        {
            localPlayer = MainGameManager.Instance.GetLocalPlayer();
        }
        
        if (localPlayer == null || localPlayer.IsDead) return false;
        
        // 로컬 플레이어가 이 적을 근처에 등록했는지 확인
        return localPlayer.IsEnemyNearby(this);
    }
    #endregion

    #region Runtime Visualization
    /// <summary>
    /// 런타임 시각화를 업데이트합니다.
    /// </summary>
    void Update()
    {
        if (!_showRuntimeRange) return;
        if (_fieldOfViewMesh == null) return;
        
        // 로컬 플레이어가 트리거 범위 안에 있는지 확인
        bool shouldShow = _alwaysShowRange;
        if (!shouldShow)
        {
            shouldShow = IsLocalPlayerInTriggerRange();
        }
        
        // 범위 표시 상태 업데이트
        if (shouldShow != _isRangeVisible)
        {
            UpdateRangeVisibility(shouldShow);
            _isRangeVisible = shouldShow;
        }
        
        // 범위 시각화 업데이트 (범위가 보일 때만)
        if (_isRangeVisible)
        {
            Vector3 currentPosition = transform.position;
            float currentRotation = transform.eulerAngles.z;
            
            // 적이 이동했거나 회전했는지 확인
            bool hasMoved = Vector3.Distance(currentPosition, _lastPosition) > 0.01f;
            bool hasRotated = Mathf.Abs(Mathf.DeltaAngle(currentRotation, _lastRotation)) > 0.1f;
            // 상태에 따른 업데이트 주기 적용
            bool playerDetectedNow = _detectedPlayer != null;
            float targetInterval = playerDetectedNow ? _updateIntervalDetected : _updateIntervalIdle;
            bool timePassed = (Time.time - _lastUpdateTime) >= targetInterval;
            
            // 플레이어 위치 변경 확인
            bool playerMoved = CheckPlayerMovement();
            
            // 플레이어 탐지 상태가 변경되었는지 확인
            bool playerDetectedChanged = playerDetectedNow != _wasPlayerDetected;
            
            // 플레이어가 범위 안에 있을 때는 항상 업데이트
            bool shouldUpdate = hasMoved || hasRotated || timePassed || playerMoved || playerDetectedChanged;
            
            if (shouldUpdate)
            {
                int segmentsToUse = playerDetectedNow ? _segmentsWhenDetected : _segmentsWhenIdle;
                UpdateRangeVisualization(true, segmentsToUse);
                _lastPosition = currentPosition;
                _lastRotation = currentRotation;
                _lastUpdateTime = Time.time;
            }

            // 탐지 상태 기록 업데이트
            _wasPlayerDetected = playerDetectedNow;
        }
    }
    
    /// <summary>
    /// 범위 표시 여부를 업데이트합니다.
    /// </summary>
    private void UpdateRangeVisibility(bool visible)
    {
        if (_detectionRangeMeshRenderer != null)
        {
            _detectionRangeMeshRenderer.enabled = visible;
        }
    }
    
    /// <summary>
    /// 플레이어들의 위치 변경을 확인합니다.
    /// </summary>
    private bool CheckPlayerMovement()
    {
        // 모든 플레이어 가져오기
        List<PlayerController> allPlayers = new List<PlayerController>();
        
        if (MainGameManager.Instance != null)
        {
            allPlayers = MainGameManager.Instance.GetAllPlayers();
        }
        
        if (allPlayers == null || allPlayers.Count == 0)
        {
            allPlayers = new List<PlayerController>(FindObjectsOfType<PlayerController>());
        }
        
        bool anyPlayerMoved = false;
        Vector2 enemyPos = transform.position;
        
        // 현재 플레이어들 위치 확인
        foreach (var player in allPlayers)
        {
            if (player == null || player.IsDead) continue;
            
            Vector3 playerPos = player.transform.position;
            
            // 트리거 범위 안에 있는 플레이어만 체크
            float distance = Vector2.Distance(enemyPos, playerPos);
            if (distance <= _visualizationTriggerRange || _alwaysShowRange)
            {
                // 이전 위치와 비교
                if (_lastPlayerPositions.ContainsKey(player))
                {
                    Vector3 lastPos = _lastPlayerPositions[player];
                    if (Vector3.Distance(playerPos, lastPos) > 0.01f)
                    {
                        anyPlayerMoved = true;
                    }
                }
                else
                {
                    // 처음 발견된 플레이어면 이동한 것으로 간주
                    anyPlayerMoved = true;
                }
                
                // 현재 위치 저장
                _lastPlayerPositions[player] = playerPos;
            }
        }
        
        // 사라진 플레이어 제거
        var keysToRemove = new List<PlayerController>();
        foreach (var kvp in _lastPlayerPositions)
        {
            if (!allPlayers.Contains(kvp.Key) || kvp.Key == null || kvp.Key.IsDead)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            _lastPlayerPositions.Remove(key);
        }
        
        return anyPlayerMoved;
    }
    
    /// <summary>
    /// 범위 시각화를 업데이트합니다 (실제 시야 범위 그리기).
    /// </summary>
    private void UpdateRangeVisualization(bool forceRedraw = false, int segmentsOverride = -1)
    {
        if (_enemyData == null) return;
        if (_fieldOfViewMesh == null) return;
        
        float detectionRange = _enemyData.detectionRange;
        
        // 강제 갱신 또는 범위 값 변경 시에만 다시 그리기 (성능 최적화)
        if (forceRedraw || Mathf.Abs(detectionRange - _lastDetectionRange) > 0.01f)
        {
            // 실제 시야 범위 그리기 (벽에 막히는 부분 제외)
            int segments = segmentsOverride > 0 ? segmentsOverride : _segmentsWhenDetected;
            DrawFieldOfViewMesh(detectionRange, segments);
            _lastDetectionRange = detectionRange;
        }
    }
    
    /// <summary>
    /// 실제 시야 범위를 Mesh로 그립니다 (벽에 막히는 부분 제외, 색으로 채움).
    /// </summary>
    private void DrawFieldOfViewMesh(float radius, int segments)
    {
        if (_fieldOfViewMesh == null) return;
        
        Vector3 center = Vector3.zero; // 로컬 좌표 기준 (부모가 적이므로)
        List<Vector3> viewPoints = new List<Vector3>();
        
        // 각 방향으로 레이캐스트를 쏴서 벽에 닿는 지점 찾기
        Vector3 worldCenter = transform.position;
        for (int i = 0; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            
            // 레이캐스트로 장애물 확인
            RaycastHit2D hit = Physics2D.Raycast(worldCenter, direction, radius, _obstacleLayerMask);
            
            Vector3 endPoint;
            if (hit.collider != null)
            {
                // 벽에 닿은 지점까지
                endPoint = hit.point;
            }
            else
            {
                // 최대 범위까지
                endPoint = worldCenter + (Vector3)(direction * radius);
            }
            
            // 로컬 좌표로 변환
            viewPoints.Add(transform.InverseTransformPoint(endPoint));
        }
        
        // Mesh 생성
        int vertexCount = viewPoints.Count + 1; // 중심점 + 외곽점들
        Vector3[] vertices = new Vector3[vertexCount];
        int[] triangles = new int[(vertexCount - 1) * 3];
        Vector2[] uv = new Vector2[vertexCount];
        
        // 중심점
        vertices[0] = center;
        uv[0] = Vector2.zero;
        
        // 외곽 점들
        for (int i = 0; i < viewPoints.Count; i++)
        {
            vertices[i + 1] = viewPoints[i];
            uv[i + 1] = new Vector2((float)i / viewPoints.Count, 1f);
        }
        
        // 삼각형 생성 (중심에서 각 외곽점까지)
        for (int i = 0; i < viewPoints.Count - 1; i++)
        {
            int triIndex = i * 3;
            triangles[triIndex] = 0; // 중심점
            triangles[triIndex + 1] = i + 1;
            triangles[triIndex + 2] = i + 2;
        }
        
        // 마지막 삼각형 (마지막 점에서 첫 점으로)
        int lastTriIndex = (viewPoints.Count - 1) * 3;
        triangles[lastTriIndex] = 0; // 중심점
        triangles[lastTriIndex + 1] = viewPoints.Count;
        triangles[lastTriIndex + 2] = 1;
        
        // Mesh 업데이트
        _fieldOfViewMesh.Clear();
        _fieldOfViewMesh.vertices = vertices;
        _fieldOfViewMesh.triangles = triangles;
        _fieldOfViewMesh.uv = uv;
        _fieldOfViewMesh.RecalculateNormals();
    }
    #endregion

    #region Gizmos
#if UNITY_EDITOR
    /// <summary>
    /// Unity 에디터에서 탐지 범위를 시각화합니다.
    /// </summary>
    void OnDrawGizmos()
    {
        if (!_showDetectionRange) return;
        
        Vector3 center = transform.position;
        bool shouldShowRange = _alwaysShowRange;
        
        // 로컬 플레이어가 트리거 범위 안에 있는지 확인
        if (!shouldShowRange)
        {
            // 에디터 모드에서는 항상 표시, 플레이 모드에서는 조건부 표시
            shouldShowRange = !Application.isPlaying || IsLocalPlayerInTriggerRange();
        }
        
        // 항상 표시 모드가 아니고 로컬 플레이어가 범위 밖에 있으면 표시하지 않음
        if (!shouldShowRange) return;
        
        // EnemyData가 없으면 초기화되지 않았을 수 있으므로 기본값 사용
        float detectionRange = _enemyData != null ? _enemyData.detectionRange : 5f;
        
        // 탐지 범위 표시 (반투명 원)
        Gizmos.color = _detectionRangeColor;
        RangeVisualizationUtils.DrawWireCircle(center, detectionRange, 32);
        
        // 탐지 중인 플레이어가 있으면 해당 위치 표시
        if (_detectedPlayer != null && Application.isPlaying)
        {
            Gizmos.color = _detectedPlayerColor;
            Vector3 playerPos = _detectedPlayer.transform.position;
            
            // 플레이어 위치까지 선 그리기
            Gizmos.DrawLine(center, playerPos);
            
            // 플레이어 위치에 작은 원 표시
            Gizmos.DrawWireSphere(playerPos, 0.3f);
        }
    }
    
    /// <summary>
    /// 오브젝트가 선택되었을 때 더 명확하게 표시합니다.
    /// </summary>
    void OnDrawGizmosSelected()
    {
        if (!_showDetectionRange) return;
        
        Vector3 center = transform.position;
        float detectionRange = _enemyData != null ? _enemyData.detectionRange : 5f;
        
        // 트리거 범위 표시 (항상 표시)
        if (_visualizationTriggerRange > 0f)
        {
            Gizmos.color = _triggerRangeColor;
            RangeVisualizationUtils.DrawWireCircle(center, _visualizationTriggerRange, 32);
            
            // 트리거 범위 텍스트 표시
            Handles.Label(
                center + Vector3.up * (_visualizationTriggerRange + 0.5f),
                $"Trigger Range: {_visualizationTriggerRange:F1}m"
            );
        }
        
        // 로컬 플레이어가 트리거 범위 안에 있는지 확인
        bool shouldShowRange = _alwaysShowRange;
        if (!shouldShowRange)
        {
            // 에디터 모드에서는 항상 표시, 플레이 모드에서는 조건부 표시
            shouldShowRange = !Application.isPlaying || IsLocalPlayerInTriggerRange();
        }
        
        if (shouldShowRange)
        {
            // 외곽선 강조
            Gizmos.color = new Color(_detectionRangeColor.r, _detectionRangeColor.g, _detectionRangeColor.b, 1f);
            RangeVisualizationUtils.DrawWireCircle(center, detectionRange, 32);
            
            // 내부 원 추가 (범위 강조)
            Gizmos.color = new Color(_detectionRangeColor.r, _detectionRangeColor.g, _detectionRangeColor.b, 0.1f);
            RangeVisualizationUtils.DrawSolidCircle(center, detectionRange, 32);
            
            // 중심점 표시
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(center, 0.2f);
            
            // 탐지 범위 텍스트 표시
            Handles.Label(
                center + Vector3.up * (detectionRange + 0.5f),
                $"Detection Range: {detectionRange:F1}m"
            );
        }
        else
        {
            // 범위 밖에 있으면 힌트 텍스트 표시
            Handles.Label(
                center + Vector3.up * 0.5f,
                "시야 범위: 플레이어가 트리거 범위 안에 들어오면 표시됩니다"
            );
        }
    }
    
#endif
    #endregion
}

