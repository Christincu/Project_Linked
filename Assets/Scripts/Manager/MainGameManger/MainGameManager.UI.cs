using UnityEngine;
using Fusion;
using WaveGoalTypeEnum = WaveGoalType; // 네트워크 변수 WaveGoalType(int)과 구분하기 위한 별칭

/// <summary>
/// UI 업데이트 관련 기능을 모은 partial 클래스입니다.
/// </summary>
public partial class MainGameManager
{
    // Note: UpdateUIFromNetworkedVariables() 메서드는 사용되지 않으므로 제거됨
    // UpdateWaveUI() 메서드가 동일한 기능을 수행함
    
    /// <summary>
    /// 서버에서 네트워크 변수를 업데이트합니다. (웨이브 정보 동기화)
    /// 주의: 서버에서만 호출 가능하며, 클라이언트는 자동으로 동기화된 값을 받습니다.
    /// </summary>
    private void UpdateNetworkedWaveVariables(int roundIndex, int waveIndex, WaveGoalTypeEnum goalType, int currentGoal, int totalGoal, float elapsedTime)
    {
        if (_runner == null)
        {
            Debug.LogError("[MainGameManager] UpdateNetworkedWaveVariables: _runner is null!");
            return;
        }
        
        if (!_runner.IsServer)
        {
            Debug.LogWarning("[MainGameManager] UpdateNetworkedWaveVariables: Called on client! Only server can update networked variables.");
            return;
        }
        
        if (Object == null || !Object.IsValid)
        {
            Debug.LogWarning("[MainGameManager] UpdateNetworkedWaveVariables: NetworkObject is null or not valid!");
            return;
        }
        
        RoundIndex = roundIndex;
        WaveIndex = waveIndex;
        WaveGoalType = (int)goalType;
        WaveCurrentGoal = currentGoal;
        WaveTotalGoal = totalGoal;
        WaveElapsedTime = elapsedTime;
    }
    
    /// <summary>
    /// 서버에서 네트워크 변수를 초기화합니다. (웨이브/라운드 종료 시)
    /// 주의: 서버에서만 호출 가능합니다.
    /// </summary>
    private void ResetNetworkedWaveVariables()
    {
        if (_runner == null)
        {
            Debug.LogError("[MainGameManager] ResetNetworkedWaveVariables: _runner is null!");
            return;
        }
        
        if (!_runner.IsServer)
        {
            Debug.LogWarning("[MainGameManager] ResetNetworkedWaveVariables: Called on client! Only server can reset networked variables.");
            return;
        }
        
        if (Object == null || !Object.IsValid)
        {
            Debug.LogWarning("[MainGameManager] ResetNetworkedWaveVariables: NetworkObject is null or not valid!");
            return;
        }
        
        RoundIndex = -1;
        WaveIndex = -1;
        WaveGoalType = -1;
        WaveCurrentGoal = 0;
        WaveTotalGoal = 0;
        WaveElapsedTime = 0f;
    }
}

