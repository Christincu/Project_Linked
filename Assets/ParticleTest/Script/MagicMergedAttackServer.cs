using UnityEngine;
using Fusion;

public class MagicMergedAttackServer : MonoBehaviour
{
    [Header("Attack Prefabs/Refs")]
    [SerializeField] private GameObject damageAreaPrefab;  
    [SerializeField] private float tickDamage = 5.0f;
    [SerializeField] private float tickInterval = 0.25f;

    private readonly System.Collections.Generic.Dictionary<int, GameObject> _activeAreas = new();
    private readonly System.Collections.Generic.Dictionary<int, float> _nextTickAt = new();

    public void StartServerAttack(PlayerController absorber, int idA, int idB, Vector2 dir)
    {
        if (absorber == null) { Debug.LogError("[AttackServer] absorber NULL"); return; }
        if (absorber.Object == null) { Debug.LogError("[AttackServer] absorber.Object NULL"); return; }
        if (!absorber.Object.HasStateAuthority) { Debug.Log("[AttackServer] no StateAuthority -> ignore"); return; }

        if (damageAreaPrefab == null)
        {
            Debug.LogError("[AttackServer] damageAreaPrefab is NULL (Assign in Inspector)");
            return;
        }

        Transform absTr = absorber.transform;
        if (absTr == null) { Debug.LogError("[AttackServer] absorber.transform NULL"); return; }

        int key = unchecked((int)absorber.Object.Id.Raw);

        if (_activeAreas.TryGetValue(key, out var oldArea) && oldArea)
            Destroy(oldArea);

        Vector3 spawnPos = absTr.position + (Vector3)(dir.normalized * 1.0f);
        var area = Instantiate(damageAreaPrefab, spawnPos, Quaternion.identity);
        if (area == null) { Debug.LogError("[AttackServer] failed to Instantiate damageArea"); return; }

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        area.transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);

        _activeAreas[key] = area;
        _nextTickAt[key] = Time.time; 

        Debug.Log($"[AttackServer] START key={key}, A={idA}, B={idB}, dir={dir}, pos={spawnPos}");
    }

    public void StopServerAttack(PlayerController absorber)
    {
        if (absorber == null || absorber.Object == null) return;
        if (!absorber.Object.HasStateAuthority) return;

        int key = unchecked((int)absorber.Object.Id.Raw);

        if (_activeAreas.TryGetValue(key, out var area))
        {
            if (area) Destroy(area);
            _activeAreas.Remove(key);
        }

        _nextTickAt.Remove(key);

        Debug.Log($"[AttackServer] STOP key={key}");
    }

    void FixedUpdate()
    {
        foreach (var kv in _activeAreas)
        {
            int key = kv.Key;
            var area = kv.Value;
            if (!area) continue;

            if (!_nextTickAt.TryGetValue(key, out var nextAt))
                _nextTickAt[key] = Time.time;

            if (Time.time >= _nextTickAt[key])
            {
                _nextTickAt[key] = Time.time + tickInterval;
            }
        }
    }
}
