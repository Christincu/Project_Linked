using UnityEngine;

[CreateAssetMenu(fileName = "MagicMergeAttackProfile", menuName = "Magic/Merge Attack Profile")]
public class MagicMergeAttackProfile : ScriptableObject
{
    [System.Serializable]
    public struct Pair
    {
        public int magicIdA;
        public int magicIdB;     // ���� ����
        public float damagePerSecond;
        public float duration;
        public float range;      // ���� ����
        public float width;      // �� ��
        public float tickRate;   // �ʴ� ƽ
        public LayerMask hitMask;
    }
    public Pair[] pairs;
    public bool TryGetPair(int a, int b, out Pair p)
    {
        int x = Mathf.Min(a, b), y = Mathf.Max(a, b);
        foreach (var e in pairs)
        {
            int ex = Mathf.Min(e.magicIdA, e.magicIdB), ey = Mathf.Max(e.magicIdA, e.magicIdB);
            if (ex == x && ey == y) { p = e; return true; }
        }
        p = default; return false;
    }
}
