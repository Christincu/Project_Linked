using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class RoundData
{
    [Tooltip("이 라운드에 포함된 웨이브 리스트")]
    public List<WaveData> waveDataList = new List<WaveData>();
}


