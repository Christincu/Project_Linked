using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class WaveData
{
    public WaveGoalType waveGoalType = WaveGoalType.Kill;
    public int waveGoalCount = 0;
    public List<EnemySpawnData> enemySpawnDataList = new List<EnemySpawnData>();
    public List<GoalSpawnData> goalSpawnDataList = new List<GoalSpawnData>();
}


