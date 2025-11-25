using UnityEngine;
using Fusion;

/// <summary>
/// 합체 마법 핸들러 인터페이스
/// 각 합체 마법은 이 인터페이스를 구현하여 독립적으로 동작합니다.
/// </summary>
public interface ICombinedMagicHandler
{
    /// <summary>
    /// 이 핸들러가 처리하는 마법 코드
    /// </summary>
    int MagicCode { get; }
    
    /// <summary>
    /// 핸들러 초기화
    /// </summary>
    void Initialize(PlayerMagicController magicController, GameDataManager gameDataManager);
    
    /// <summary>
    /// 핸들러가 활성화될 때 호출 (마법 활성화 시)
    /// </summary>
    void OnMagicActivated();
    
    /// <summary>
    /// 마법 시전 전 처리 (시전 가능 여부 확인 등)
    /// </summary>
    /// <returns>시전 가능하면 true</returns>
    bool CanCast(Vector3 targetPosition);
    
    /// <summary>
    /// 마법 시전 처리
    /// </summary>
    /// <param name="targetPosition">목표 위치</param>
    /// <returns>시전 성공 여부</returns>
    bool CastMagic(Vector3 targetPosition);
    
    /// <summary>
    /// 입력 처리 (마법 활성화 상태에서 매 프레임 호출)
    /// </summary>
    /// <param name="inputData">입력 데이터</param>
    /// <param name="mouseWorldPos">마우스 월드 위치</param>
    void ProcessInput(InputData inputData, Vector3 mouseWorldPos);
    
    /// <summary>
    /// 업데이트 (매 프레임 호출, 렌더링 등)
    /// </summary>
    void Update();
    
    /// <summary>
    /// 마법 비활성화 시 호출
    /// </summary>
    void OnMagicDeactivated();
    
    /// <summary>
    /// 마법이 현재 시전 중인지 확인합니다.
    /// </summary>
    /// <returns>시전 중이면 true, 아니면 false</returns>
    bool IsCasting();
}

