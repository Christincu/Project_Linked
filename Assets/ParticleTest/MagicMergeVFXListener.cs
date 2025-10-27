using UnityEngine;

public class MagicMergeVFXListener : MonoBehaviour
{
    [Header("흡수자 쪽에서 분사할 파티클(씬에 존재하는 인스턴스)")]
    [SerializeField] private ParticleSystem[] flameSystems;

    private PlayerMagicController _mc;
    private Transform _follow;   // 흡수자의 MagicViewObj or Transform
    private bool _isPlaying;

    /// <summary>
    /// PlayerMagicController와 이벤트 바인딩
    /// </summary>
    public void Bind(PlayerMagicController mc)
    {
        if (_mc != null)
        {
            _mc.OnMergeStarted -= HandleMergeStarted;
            _mc.OnMergeStopped -= HandleMergeStopped;
        }
        _mc = mc;
        if (_mc != null)
        {
            _mc.OnMergeStarted += HandleMergeStarted;
            _mc.OnMergeStopped += HandleMergeStopped;
        }
    }

    private void OnDestroy()
    {
        if (_mc != null)
        {
            _mc.OnMergeStarted -= HandleMergeStarted;
            _mc.OnMergeStopped -= HandleMergeStopped;
        }
    }

    private void HandleMergeStarted(PlayerController absorber, PlayerController other)
    {
        // 흡수자 기준 기준점
        _follow = (absorber.MagicController && absorber.MagicController.MagicViewObj)
            ? absorber.MagicController.MagicViewObj.transform
            : absorber.transform;

        // 파티클을 "원본 설정 그대로" 재생하되, 위치만 맞춰줌
        foreach (var ps in flameSystems)
        {
            if (!ps) continue;

            // 위치 동기화
            ps.transform.position = _follow.position;

            // 원본 프리셋을 존중하되, World Space를 권장 (부채꼴 유지에 유리)
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Play On Awake는 꺼두고, 여기서 수동 재생
            ps.Play(true);
        }

        _isPlaying = true;
    }

    private void HandleMergeStopped(PlayerController absorber)
    {
        foreach (var ps in flameSystems)
        {
            if (!ps) continue;
            // 이미 나간 파티클은 자연 소멸, 새 배출은 중지
            ps.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmitting);
        }
        _isPlaying = false;
        _follow = null;
    }

    private void LateUpdate()
    {
        if (!_isPlaying || _follow == null) return;

        // World Space면 위치만 추적해주면 됨 (회전/스케일은 파티클 프리셋 그대로)
        foreach (var ps in flameSystems)
        {
            if (!ps) continue;
            ps.transform.position = _follow.position;
        }
    }
}
