using UnityEngine;
public interface ICanvas
{
    public Transform CanvasTransform { get; }
    public void Initialize(GameManager gameManager, GameDataManager gameDataManager);
}
