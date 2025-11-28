using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class WaveData
{
    public int waveIndex = 0;
    public WaveGoalType waveGoalType = new WaveGoalType();
    public int waveGoalCount = 0;
    public List<EnemySpawnData> enemySpawnDataList = new List<EnemySpawnData>();
}
