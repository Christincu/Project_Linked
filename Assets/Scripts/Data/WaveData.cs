using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class WaveData
{
    public int waveIndex = 0;
    public WaveGoalType waveGoalType = new WaveGoalType();
    public List<EnemySpawnData> enemySpawnDataList = new List<EnemySpawnData>();
}
