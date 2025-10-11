using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class ProjCtx : MonoBehaviour
{
    [HideInInspector] public Rigidbody2D RB;
    [HideInInspector] public Transform TF;

    [Header("Runtime")]
    public int CastOrder;
    public Vector3 MouseWorld;
    public List<ProjectileModule> modules = new();

    public GameObject Owner { get; private set; } // 시전자
    public float SpawnTime { get; private set; }   // 동시성 판정

    Collider2D _myCol;

    void Awake()
    {
        RB = GetComponent<Rigidbody2D>();
        TF = transform;
        _myCol = GetComponent<Collider2D>();
        SpawnTime = Time.time;
    }

    void OnEnable()
    {
        if (Owner != null && _myCol != null) ApplyIgnoreWithOwner();
    }

    public void InitOwner(GameObject owner)
    {
        Owner = owner;
        if (_myCol != null && Owner != null) ApplyIgnoreWithOwner();
    }

    void ApplyIgnoreWithOwner()
    {
        var ownerCols = Owner.GetComponentsInChildren<Collider2D>(true);
        foreach (var c in ownerCols)
            if (c && c.enabled) Physics2D.IgnoreCollision(_myCol, c, true);
    }

    void Update() { for (int i = 0; i < modules.Count; i++) modules[i].OnTick(this); }
    void OnTriggerEnter2D(Collider2D col) { for (int i = 0; i < modules.Count; i++) modules[i].OnHit(this, col); }
}
