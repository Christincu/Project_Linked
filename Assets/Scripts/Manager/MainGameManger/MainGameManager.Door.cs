using System.Collections;
using UnityEngine;
using Fusion;

/// <summary>
/// 맵 문 관리 관련 기능을 모은 partial 클래스입니다.
/// </summary>
public partial class MainGameManager
{
    /// <summary>
    /// 맵 문과 관련된 초기 상태를 설정합니다.
    /// 씬 시작 시 모든 RoundTrigger에 연결된 문을 열린 상태로 둡니다.
    /// </summary>
    private IEnumerator InitializeMapDoorStateAsync(NetworkRunner runner)
    {
        _isMapDoorClosed = false;

        if (runner == null || !runner.IsServer || !runner.IsRunning)
        {
            Debug.LogWarning("[MainGameManager] Cannot initialize door state - Runner not available or not server");
            yield break;
        }

        if (_roundTriggers != null)
        {
            foreach (var trigger in _roundTriggers)
            {
                if (trigger == null) continue;

                var doors = trigger.DoorObjects;
                if (doors == null) continue;

                foreach (var door in doors)
                {
                    if (door == null) continue;
                    
                    int attempts = 0;
                    int maxAttempts = 50;
                    while ((door.Object == null || !door.Object.IsValid) && attempts < maxAttempts)
                    {
                        yield return new WaitForSeconds(0.1f);
                        attempts++;
                    }
                    
                    if (door.Object != null && door.Object.IsValid)
                    {
                        door.SetClosed(false);
                    }
                    else
                    {
                        Debug.LogWarning($"[MainGameManager] Door NetworkObject not spawned in time: {door.name}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// 맵 문을 닫습니다.
    /// </summary>
    private void CloseMapDoor()
    {
        if (_isMapDoorClosed) return;

        _isMapDoorClosed = true;

        if (_currentRoundDoorObjects != null)
        {
            foreach (var door in _currentRoundDoorObjects)
            {
                if (door == null) continue;

                if (FusionManager.LocalRunner == null || FusionManager.LocalRunner.IsServer)
                {
                    door.SetClosed(true);
                }
            }
        }

    }

    /// <summary>
    /// 현재 라운드의 문들을 즉시 닫습니다.
    /// </summary>
    public void CloseCurrentRoundDoors()
    {
        CloseMapDoor();
    }
    
    /// <summary>
    /// 맵 문을 엽니다.
    /// </summary>
    private void OpenMapDoor()
    {
        if (!_isMapDoorClosed) return;

        _isMapDoorClosed = false;

        if (_currentRoundDoorObjects != null)
        {
            foreach (var door in _currentRoundDoorObjects)
            {
                if (door == null) continue;

                if (FusionManager.LocalRunner == null || FusionManager.LocalRunner.IsServer)
                {
                    door.SetClosed(false);
                }
            }
        }

    }
}

