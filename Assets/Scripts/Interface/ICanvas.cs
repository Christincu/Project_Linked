using UnityEngine;
public interface ICanvas
{
    public Transform CanvasTransform { get; }
    public void OnInitialize(GameManager gameManager, GameDataManager gameDataManager);
}
