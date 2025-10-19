using UnityEngine;

public class MainCanvas : MonoBehaviour
{
    [Header("Health UI")]
    [SerializeField] private HpHeartsOverlayUI healthUI;

    void Start()
    {
        if (!healthUI) healthUI = GetComponentInChildren<HpHeartsOverlayUI>(true);

        var player = FindObjectOfType<PlayerController>();
        if (player)
        {
            var behaviour = player.GetComponent<PlayerBehaviour>();
            if (behaviour && healthUI)
            {
                healthUI.Bind(behaviour);
            }
        }
    }
}
