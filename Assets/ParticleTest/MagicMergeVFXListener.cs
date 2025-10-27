// MagicMergeVFXListener.cs

using UnityEngine;
using System.Collections;

public class MagicMergeVFXListener : MonoBehaviour
{
    [Header("����� �ʿ��� �л��� ��ƼŬ(���� �����ϴ� �ν��Ͻ�)")]
    [SerializeField] private ParticleSystem[] flameSystems;

    private PlayerMagicController _mc;
    private Transform _follow;   // ������� MagicViewObj or Transform
    private bool _isPlaying;
    private Coroutine _autoStopCR;   // �� 5�� Ÿ�̸� �ڵ�

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

        // ���� Ÿ�̸Ӱ� ���� ������ ����
        if (_autoStopCR != null) StopCoroutine(_autoStopCR);
        // 5�� �� �ڵ� ����
        _autoStopCR = StartCoroutine(AutoStopAfterSeconds(5f));
    }

    private void HandleMergeStopped(PlayerController absorber)
    {
        // ��ƼŬ ����
        foreach (var ps in flameSystems)
        {
            if (!ps) continue;
            ps.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmitting);
        }

        _isPlaying = false;
        _follow = null;

        // Ÿ�̸� Ŭ����
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

        // ������ ��� ���̸� (��, Merge ���� ���̸�) ���� ���� ��û
        if (_isPlaying && _mc != null)
        {
            _mc.ForceStopMerge();   // �� �̺�Ʈ�� �ùٸ��� Ʈ�����ϴ� ���� �޼���
        }
        _autoStopCR = null;
    }
}
