using UnityEngine;

/// <summary>
/// 씬별 매니저 인터페이스 (TitleGameManager, MainGameManager 등)
/// </summary>
public interface ISceneGameManager
{
    /// <summary>
    /// 씬 매니저 초기화 메서드
    /// </summary>
    void OnInitialize(GameManager gameManager, GameDataManager gameDataManager);
}
