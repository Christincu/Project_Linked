using UnityEngine;

/// <summary>
/// 씬별 매니저 인터페이스 (TitleGameManager, MainGameManager 등)
/// </summary>
public interface ISceneManager
{
    /// <summary>
    /// 씬 매니저 초기화 메서드
    /// </summary>
    void Initialize(GameManager gameManager, GameDataManager gameDataManager);
}
