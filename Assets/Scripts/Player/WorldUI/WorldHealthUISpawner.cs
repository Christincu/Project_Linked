using UnityEngine;

[DisallowMultipleComponent]
public class WorldHealthUISpawner : MonoBehaviour
{
    [SerializeField] private GameObject worldHealthUIPrefab; 
    [SerializeField] private Vector2 offset = new(0.0f, 1.2f); 
    [SerializeField] private float canvasZ = 0f;             

    GameObject _inst;

    void Start()
    {
        var hp = GetComponent<HealthComponent>();
        if (!hp || !worldHealthUIPrefab) return;

        _inst = Instantiate(worldHealthUIPrefab);

        var canvas = _inst.GetComponentInChildren<Canvas>(true);
        if (canvas && canvas.renderMode == RenderMode.WorldSpace && !canvas.worldCamera)
            canvas.worldCamera = Camera.main;

        var follow = _inst.GetComponent<WorldUIFollow>();
        if (!follow) follow = _inst.AddComponent<WorldUIFollow>();
        follow.target = transform;
        follow.worldOffset = offset;

        var t = _inst.transform;
        t.position = new Vector3(transform.position.x + offset.x,
                                 transform.position.y + offset.y,
                                 canvasZ);

        var hearts = _inst.GetComponentInChildren<HpHeartsOverlayUI>(true);
        if (hearts) hearts.Bind(hp);
    }

    void OnDestroy()
    {
        if (_inst) Destroy(_inst);
    }
}
