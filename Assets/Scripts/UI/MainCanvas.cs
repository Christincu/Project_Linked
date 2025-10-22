using UnityEngine;

public class MainCanvas : MonoBehaviour, ICanvas
{
    public Transform CanvasTransform => transform;

    public void Initialize(GameManager gameManager, GameDataManager gameDataManager)
    {
        
    }
}
