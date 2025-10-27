using System;
using System.Collections.Generic;
using UnityEngine;

public class MagicVFXSpawner : MonoBehaviour
{
    [Serializable]
    public class VfxPreset
    {
        public int magicIdA;               
        public int magicIdB;                
        public GameObject prefab;           
    }

    [Header("Merge 조합별 프리팹 매핑")]
    public List<VfxPreset> pairs = new();

    private readonly Dictionary<int, GameObject> _active = new();

    private VfxPreset GetPreset(int a, int b)
    {
        foreach (var p in pairs)
        {
            if ((p.magicIdA == a && p.magicIdB == b) || (p.magicIdA == b && p.magicIdB == a))
                return p;
        }
        return null;
    }

    public void StartConeEmitter(
        int ownerKey,
        int magicIdA, int magicIdB,
        Transform center,
        Func<Vector3> getAimWorld,
        Func<Vector2> getForward,
        float totalDuration = 5.0f,
        float radius = 1.0f,
        float segInterval = 0.20f,
        float angleSpawnDelta = 15f,
        float segmentLife = 0.6f)
    {
        // 같은 키로 기존 이펙트가 있다면 먼저 정리
        StopConeEmitter(ownerKey);

        var preset = GetPreset(magicIdA, magicIdB);
        if (preset == null || preset.prefab == null)
        {
            Debug.LogWarning($"[MagicVFXSpawner] No preset for ({magicIdA},{magicIdB}).");
            return;
        }

        var go = Instantiate(preset.prefab, center.position, Quaternion.identity, center);
        go.transform.localScale = Vector3.one;

        Vector2 dir;
        try
        {
            var aim = getAimWorld != null ? getAimWorld() : center.position + (Vector3)getForward();
            var v = (Vector2)(aim - center.position);
            dir = v.sqrMagnitude > 0.0001f ? v.normalized : (getForward != null ? getForward() : Vector2.right);
        }
        catch
        {
            dir = getForward != null ? getForward() : Vector2.right;
        }

        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        go.transform.rotation = Quaternion.AngleAxis(ang, Vector3.forward);

        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ps.Play(true);
        }

        _active[ownerKey] = go;
    }

    public void StopConeEmitter(int ownerKey)
    {
        if (_active.TryGetValue(ownerKey, out var go) && go)
        {
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            Destroy(go);
        }
        _active.Remove(ownerKey);
    }
}
