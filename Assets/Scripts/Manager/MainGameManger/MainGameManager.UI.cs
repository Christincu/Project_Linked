using UnityEngine;

/// <summary>
/// UI 업데이트 관련 기능을 모은 partial 클래스입니다.
/// </summary>
public partial class MainGameManager
{
    /// <summary>
    /// 네트워크 변수에서 UI를 업데이트합니다. (클라이언트에서 호출)
    /// </summary>
    private void UpdateUIFromNetworkedVariables()
    {
        if (GameManager.Instance?.Canvas is not MainCanvas canvas) return;
        
        string waveLabel = (NetworkRoundIndex >= 0 && NetworkWaveIndex >= 0) 
            ? $"Wave {NetworkWaveIndex + 1}" 
            : "Wave";
        canvas.SetWaveText(waveLabel);
        
        if (NetworkWaveGoalType < 0 || NetworkWaveTotalGoal <= 0)
        {
            canvas.SetGoalText(string.Empty);
            return;
        }
        
        string goalText = string.Empty;
        WaveGoalType goalType = (WaveGoalType)NetworkWaveGoalType;
        switch (goalType)
        {
            case WaveGoalType.Kill:
                goalText = $"Kill {NetworkWaveCurrentGoal}/{NetworkWaveTotalGoal}";
                break;
            case WaveGoalType.Collect:
                goalText = $"Collect {NetworkWaveCurrentGoal}/{NetworkWaveTotalGoal}";
                break;
            case WaveGoalType.Survive:
                int elapsedSec = Mathf.FloorToInt(NetworkWaveElapsedTime);
                goalText = $"Survive {elapsedSec}/{NetworkWaveTotalGoal} sec";
                break;
            default:
                goalText = string.Empty;
                break;
        }
        canvas.SetGoalText(goalText);
    }
    
    /// <summary>
    /// 서버에서 네트워크 변수를 업데이트합니다. (웨이브 정보 동기화)
    /// </summary>
    private void UpdateNetworkedWaveVariables(int roundIndex, int waveIndex, WaveGoalType goalType, int currentGoal, int totalGoal, float elapsedTime)
    {
        if (Runner == null || !Runner.IsServer || !Object.IsValid) return;
        
        NetworkRoundIndex = roundIndex;
        NetworkWaveIndex = waveIndex;
        NetworkWaveGoalType = (int)goalType;
        NetworkWaveCurrentGoal = currentGoal;
        NetworkWaveTotalGoal = totalGoal;
        NetworkWaveElapsedTime = elapsedTime;
    }
    
    /// <summary>
    /// 서버에서 네트워크 변수를 초기화합니다. (웨이브/라운드 종료 시)
    /// </summary>
    private void ResetNetworkedWaveVariables()
    {
        if (Runner == null || !Runner.IsServer || !Object.IsValid) return;
        
        NetworkRoundIndex = -1;
        NetworkWaveIndex = -1;
        NetworkWaveGoalType = -1;
        NetworkWaveCurrentGoal = 0;
        NetworkWaveTotalGoal = 0;
        NetworkWaveElapsedTime = 0f;
    }
}

