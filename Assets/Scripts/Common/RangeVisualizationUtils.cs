using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 범위 시각화를 위한 유틸리티 클래스
/// Gizmos와 Mesh를 사용한 원형 범위 그리기 기능을 제공합니다.
/// </summary>
public static class RangeVisualizationUtils
{
    #region Gizmos Drawing
#if UNITY_EDITOR
    /// <summary>
    /// 원의 외곽선을 그립니다 (Gizmos).
    /// </summary>
    /// <param name="center">원의 중심점</param>
    /// <param name="radius">원의 반지름</param>
    /// <param name="segments">원을 그릴 세그먼트 수</param>
    public static void DrawWireCircle(Vector3 center, float radius, int segments)
    {
        if (segments < 3) segments = 3;
        
        Vector3 prevPoint = center + new Vector3(Mathf.Cos(0) * radius, Mathf.Sin(0) * radius, 0);
        
        for (int i = 1; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 newPoint = center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
            Gizmos.DrawLine(prevPoint, newPoint);
            prevPoint = newPoint;
        }
    }
    
    /// <summary>
    /// 원의 내부를 채웁니다 (반투명, Gizmos).
    /// </summary>
    /// <param name="center">원의 중심점</param>
    /// <param name="radius">원의 반지름</param>
    /// <param name="segments">원을 그릴 세그먼트 수</param>
    public static void DrawSolidCircle(Vector3 center, float radius, int segments)
    {
        if (segments < 3) segments = 3;
        
        // 중심에서 각 점까지 삼각형으로 채우기
        for (int i = 0; i < segments; i++)
        {
            float angle1 = (i / (float)segments) * Mathf.PI * 2f;
            float angle2 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
            
            Vector3 point1 = center + new Vector3(Mathf.Cos(angle1) * radius, Mathf.Sin(angle1) * radius, 0);
            Vector3 point2 = center + new Vector3(Mathf.Cos(angle2) * radius, Mathf.Sin(angle2) * radius, 0);
            
            // 삼각형 그리기 (Gizmos는 직접 삼각형을 그릴 수 없으므로 외곽선으로 표현)
            Gizmos.DrawLine(center, point1);
            Gizmos.DrawLine(point1, point2);
        }
    }
#endif
    #endregion
    
    #region Mesh Drawing
    /// <summary>
    /// 원형 Mesh를 생성합니다.
    /// </summary>
    /// <param name="radius">원의 반지름</param>
    /// <param name="segments">원을 그릴 세그먼트 수</param>
    /// <returns>생성된 Mesh</returns>
    public static Mesh CreateCircleMesh(float radius, int segments)
    {
        if (segments < 3) segments = 3;
        
        Vector3 center = Vector3.zero; // 로컬 좌표 기준
        List<Vector3> circlePoints = new List<Vector3>();
        
        // 원의 외곽 점들 생성
        for (int i = 0; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 point = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
            circlePoints.Add(point);
        }
        
        // Mesh 생성
        int vertexCount = circlePoints.Count + 1; // 중심점 + 외곽점들
        Vector3[] vertices = new Vector3[vertexCount];
        int[] triangles = new int[(vertexCount - 1) * 3];
        Vector2[] uv = new Vector2[vertexCount];
        
        // 중심점
        vertices[0] = center;
        uv[0] = Vector2.zero;
        
        // 외곽 점들
        for (int i = 0; i < circlePoints.Count; i++)
        {
            vertices[i + 1] = circlePoints[i];
            uv[i + 1] = new Vector2((float)i / circlePoints.Count, 1f);
        }
        
        // 삼각형 생성 (중심에서 각 외곽점까지)
        for (int i = 0; i < circlePoints.Count - 1; i++)
        {
            int triIndex = i * 3;
            triangles[triIndex] = 0; // 중심점
            triangles[triIndex + 1] = i + 1;
            triangles[triIndex + 2] = i + 2;
        }
        
        // 마지막 삼각형 (마지막 점에서 첫 점으로)
        int lastTriIndex = (circlePoints.Count - 1) * 3;
        triangles[lastTriIndex] = 0; // 중심점
        triangles[lastTriIndex + 1] = circlePoints.Count;
        triangles[lastTriIndex + 2] = 1;
        
        // Mesh 생성 및 반환
        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        
        return mesh;
    }
    
    /// <summary>
    /// 기존 Mesh를 원형으로 업데이트합니다.
    /// </summary>
    /// <param name="mesh">업데이트할 Mesh</param>
    /// <param name="radius">원의 반지름</param>
    /// <param name="segments">원을 그릴 세그먼트 수</param>
    public static void UpdateCircleMesh(Mesh mesh, float radius, int segments)
    {
        if (mesh == null) return;
        
        if (segments < 3) segments = 3;
        
        Vector3 center = Vector3.zero; // 로컬 좌표 기준
        List<Vector3> circlePoints = new List<Vector3>();
        
        // 원의 외곽 점들 생성
        for (int i = 0; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 point = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
            circlePoints.Add(point);
        }
        
        // Mesh 생성
        int vertexCount = circlePoints.Count + 1; // 중심점 + 외곽점들
        Vector3[] vertices = new Vector3[vertexCount];
        int[] triangles = new int[(vertexCount - 1) * 3];
        Vector2[] uv = new Vector2[vertexCount];
        
        // 중심점
        vertices[0] = center;
        uv[0] = Vector2.zero;
        
        // 외곽 점들
        for (int i = 0; i < circlePoints.Count; i++)
        {
            vertices[i + 1] = circlePoints[i];
            uv[i + 1] = new Vector2((float)i / circlePoints.Count, 1f);
        }
        
        // 삼각형 생성 (중심에서 각 외곽점까지)
        for (int i = 0; i < circlePoints.Count - 1; i++)
        {
            int triIndex = i * 3;
            triangles[triIndex] = 0; // 중심점
            triangles[triIndex + 1] = i + 1;
            triangles[triIndex + 2] = i + 2;
        }
        
        // 마지막 삼각형 (마지막 점에서 첫 점으로)
        int lastTriIndex = (circlePoints.Count - 1) * 3;
        triangles[lastTriIndex] = 0; // 중심점
        triangles[lastTriIndex + 1] = circlePoints.Count;
        triangles[lastTriIndex + 2] = 1;
        
        // Mesh 업데이트
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
    }
    #endregion
}

