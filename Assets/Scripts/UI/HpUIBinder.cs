using UnityEngine;

public class HpUIBinder : MonoBehaviour
{
    [SerializeField] private HpHeartsOverlayUI heartsUI;

    private void Start()
    {
        if (!heartsUI) heartsUI = GetComponentInChildren<HpHeartsOverlayUI>(true);

        var player = FindObjectOfType<PlayerCtx>();
        if (!player) return;

        var hp = player.GetComponent<HealthComponent>();
        if (hp) heartsUI.Bind(hp);
    }
}
