// MagicMergeRecipeByData.cs
using UnityEngine;

[CreateAssetMenu(fileName = "MagicMergeRecipeByData", menuName = "Magic/Merge Recipe (by MagicData)")]
public class MagicMergeRecipeByData : ScriptableObject
{
    [System.Serializable]
    public struct Pair
    {
        public int magicIdA;
        public int magicIdB;         
        public ParticleSystem vfxPrefab;
    }
    public Pair[] pairs;
    public ParticleSystem GetVFX(int idA, int idB)
    {
        int a = Mathf.Min(idA, idB); int b = Mathf.Max(idA, idB);
        foreach (var p in pairs)
        {
            int pa = Mathf.Min(p.magicIdA, p.magicIdB);
            int pb = Mathf.Max(p.magicIdA, p.magicIdB);
            if (pa == a && pb == b) return p.vfxPrefab;
        }
        return null;
    }
}
