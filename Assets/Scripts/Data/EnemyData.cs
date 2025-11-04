using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적의 데이터를 정의하는 ScriptableObject입니다.
/// 뷰 오브젝트, 이름, 설명, 그리고 각 적의 고유한 설정을 포함합니다.
/// </summary>
[CreateAssetMenu(fileName = "New Enemy", menuName = "Game/Enemy Data")]
public class EnemyData : ScriptableObject
{
    [Header("Enemy Info")]
    [Tooltip("Enemy name")]
    public string enemyName;
    
    [Header("Enemy View")]
    [Tooltip("Enemy sprite (for game)")]
    public Sprite enemySprite;
    
    [Tooltip("Enemy view object prefab")]
    public GameObject viewObj;

    [Header("Optional Info")]
    [TextArea(3, 5)]
    [Tooltip("Enemy description")]
    public string description;

    [Header("Movement Settings")]
    [Tooltip("기본 이동 속도")]
    public float moveSpeed = 3f;
    
    [Tooltip("최대 이동 속도")]
    public float maxVelocity = 5f;
    
    [Tooltip("가속도 (1이면 즉시 반응, 0에 가까울수록 부드러운 가속)")]
    [Range(0.1f, 1f)] public float acceleration = 0.8f;

    [Header("Detection Settings")]
    [Tooltip("플레이어 탐지 범위")]
    public float detectionRange = 5f;
    
    [Tooltip("시야 각도 (0 ~ 180도)")]
    [Range(0f, 180f)] public float detectionAngle = 120f;
    
    [Header("Visualization Settings")]
    [Tooltip("시야 범위 표시 트리거 범위 (플레이어가 이 범위 안에 들어가면 시야 범위 표시, 0이면 항상 표시)")]
    public float visualizationTriggerRange = 10f;

    [Header("Behavior Settings")]
    [Tooltip("탐지 후 대기 시간 (초)")]
    public float investigationWaitTime = 3f;
    
    [Tooltip("탐지 시 이동 멈춤 거리")]
    public float stopDistance = 0.5f;

    [Header("Health Settings")]
    [Tooltip("최대 체력")]
    public float maxHealth = 10f;
    
    [Tooltip("시작 체력")]
    public float startingHealth = 10f;
}
