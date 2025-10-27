using UnityEngine;

public class MagicMergeVFXListener : MonoBehaviour
{
    [Header("����� �ʿ��� �л��� ��ƼŬ(���� �����ϴ� �ν��Ͻ�)")]
    [SerializeField] private ParticleSystem[] flameSystems;

    private PlayerMagicController _mc;
    private Transform _follow;   // ������� MagicViewObj or Transform
    private bool _isPlaying;

    /// <summary>
    /// PlayerMagicController�� �̺�Ʈ ���ε�
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
        // ����� ���� ������
        _follow = (absorber.MagicController && absorber.MagicController.MagicViewObj)
            ? absorber.MagicController.MagicViewObj.transform
            : absorber.transform;

        // ��ƼŬ�� "���� ���� �״��" ����ϵ�, ��ġ�� ������
        foreach (var ps in flameSystems)
        {
            if (!ps) continue;

            // ��ġ ����ȭ
            ps.transform.position = _follow.position;

            // ���� �������� �����ϵ�, World Space�� ���� (��ä�� ������ ����)
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Play On Awake�� ���ΰ�, ���⼭ ���� ���
            ps.Play(true);
        }

        _isPlaying = true;
    }

    private void HandleMergeStopped(PlayerController absorber)
    {
        foreach (var ps in flameSystems)
        {
            if (!ps) continue;
            // �̹� ���� ��ƼŬ�� �ڿ� �Ҹ�, �� ������ ����
            ps.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmitting);
        }
        _isPlaying = false;
        _follow = null;
    }

    private void LateUpdate()
    {
        if (!_isPlaying || _follow == null) return;

        // World Space�� ��ġ�� �������ָ� �� (ȸ��/�������� ��ƼŬ ������ �״��)
        foreach (var ps in flameSystems)
        {
            if (!ps) continue;
            ps.transform.position = _follow.position;
        }
    }
}
