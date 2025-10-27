// MagicVFXRecipe.cs
using UnityEngine;

[CreateAssetMenu(fileName = "MagicVFXRecipe", menuName = "Magic/Merge VFX Recipe", order = 0)]
public class MagicVFXRecipe : ScriptableObject
{
    [System.Serializable]
    public struct Pair
    {
        public int magicIdA; 
        public int magicIdB;   
        public ParticleSystem vfxPrefab; 
    }

    [Header("Unordered pairs -> merged VFX")]
    public Pair[] pairs;

    public ParticleSystem GetVFX(int idA, int idB)
    {
        int a = Mathf.Min(idA, idB);
        int b = Mathf.Max(idA, idB);
        foreach (var p in pairs)
        {
            int pa = Mathf.Min(p.magicIdA, p.magicIdB);
            int pb = Mathf.Max(p.magicIdA, p.magicIdB);
            if (pa == a && pb == b)
                return p.vfxPrefab;
        }
        return null;
    }
}
