using System;

[Serializable]
public class EnemySpawnData
{
    public int spawnerIndex = 0;
    public int enemySpawnIndex = 0;
    public int enemyCount = 0;
    public float spawnInterval = 0f;
    public float spawnDelay = 0f;
    public string enemyCode = string.Empty;
}
