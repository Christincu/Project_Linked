using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

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
    
    [Header("Detection Settings")]
    [SerializeField] private LayerMask _playerLayerMask;
    [SerializeField] private LayerMask _obstacleLayerMask;
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
        else
        {
            allPlayers.AddRange(FindObjectsOfType<PlayerController>());
        }

        foreach (var player in allPlayers)
        {
            if (player == null || player.IsDead) continue;

            Vector2 playerPos = (Vector2)player.transform.position;
            Vector2 enemyPos = (Vector2)transform.position;
            Vector2 direction = playerPos - enemyPos;
            float distance = direction.magnitude;

            // 1. 거리 체크
            if (distance > _enemyData.detectionRange) continue;

            // 2. 각도 체크 (시야각)
            Vector2 enemyForward = GetEnemyForward();
            float angle = Vector2.Angle(direction.normalized, enemyForward);
            if (angle > _enemyData.detectionAngle * 0.5f) continue;

            // 3. 시야 체크 (레이캐스트로 장애물 확인)
            if (!HasLineOfSight(enemyPos, playerPos)) continue;

            // 플레이어 발견!
            detectedPlayer = player;
            _detectedPlayer = player;
            _lastDetectedPosition = playerPos;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 적의 앞 방향을 반환합니다 (위쪽 기본).
    /// </summary>
    private Vector2 GetEnemyForward()
    {
        // 탑뷰 게임이므로 기본적으로 위쪽 방향
        return Vector2.up;
    }

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
    #endregion
}

