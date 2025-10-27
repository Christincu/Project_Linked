// MagicMergeVFXListener.cs

using UnityEngine;
using System.Collections;

public class MagicMergeVFXListener : MonoBehaviour
{
    [Header("흡수자 쪽에서 분사할 파티클(씬에 존재하는 인스턴스)")]
    [SerializeField] private ParticleSystem[] flameSystems;

    private PlayerMagicController _mc;
    private Transform _follow;   // 흡수자의 MagicViewObj or Transform
    private bool _isPlaying;
    private Coroutine _autoStopCR;   // ← 5초 타이머 핸들

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
        _follow = (absorber.MagicController && absorber.MagicController.MagicViewObj)
            ? absorber.MagicController.MagicViewObj.transform
            : absorber.transform;

        foreach (var ps in flameSystems)
        {
            if (!ps) continue;
            ps.transform.position = _follow.position;

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ps.Play(true);
        }

        _isPlaying = true;

        // 이전 타이머가 돌고 있으면 정지
        if (_autoStopCR != null) StopCoroutine(_autoStopCR);
        // 5초 뒤 자동 종료
        _autoStopCR = StartCoroutine(AutoStopAfterSeconds(5f));
    }

    private void HandleMergeStopped(PlayerController absorber)
    {
        // 파티클 정지
        foreach (var ps in flameSystems)
        {
            if (!ps) continue;
            ps.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmitting);
        }

        _isPlaying = false;
        _follow = null;

        // 타이머 클리어
        if (_autoStopCR != null)
        {
            StopCoroutine(_autoStopCR);
            _autoStopCR = null;
        }
    }

    private void LateUpdate()
    {
        if (!_isPlaying || _follow == null) return;

        foreach (var ps in flameSystems)
        {
            if (!ps) continue;
            ps.transform.position = _follow.position;
        }
    }

    private IEnumerator AutoStopAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        // 아직도 재생 중이면 (즉, Merge 유지 중이면) 강제 종료 요청
        if (_isPlaying && _mc != null)
        {
            _mc.ForceStopMerge();   // ← 이벤트를 올바르게 트리거하는 공개 메서드
        }
        _autoStopCR = null;
    }
}
