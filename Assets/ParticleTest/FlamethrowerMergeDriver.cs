using System.Collections;
using UnityEngine;

public class FlamethrowerMergeDriver : MonoBehaviour
{
    [SerializeField] private FlamethrowerEmitter emitter;
    [SerializeField] private PlayerMagicController pmc;

    [Header("Auto Stop")]
    [SerializeField] private float sustainSeconds = 5f;

    private Coroutine _autoStopCo;

    void Reset()
    {
        if (!emitter) emitter = GetComponent<FlamethrowerEmitter>();
        if (!pmc) pmc = GetComponentInParent<PlayerMagicController>();
    }

    void OnEnable()
    {
        if (!pmc) pmc = GetComponentInParent<PlayerMagicController>();
        if (pmc != null)
        {
            pmc.OnMergeStarted += HandleMergeStart;
            pmc.OnMergeStopped += HandleMergeStop;
        }
    }

    void OnDisable()
    {
        if (pmc != null)
        {
            pmc.OnMergeStarted -= HandleMergeStart;
            pmc.OnMergeStopped -= HandleMergeStop;
        }
    }

    private void HandleMergeStart(PlayerController absorber, PlayerController other)
    {
        // 이 드라이버는 '내가 흡수자일 때'만 동작
        if (!pmc || !emitter) return;
        if (absorber != pmc.Controller) return;

        emitter.BeginHold();

        if (_autoStopCo != null) StopCoroutine(_autoStopCo);
        _autoStopCo = StartCoroutine(AutoStop(absorber, other));
    }

    private void HandleMergeStop(PlayerController absorber)
    {
        if (_autoStopCo != null)
        {
            StopCoroutine(_autoStopCo);
            _autoStopCo = null;
        }
        if (emitter) emitter.StopAll();
    }

    private IEnumerator AutoStop(PlayerController absorber, PlayerController other)
    {
        yield return new WaitForSeconds(sustainSeconds);

        Debug.Log("[FlameDriver] Auto stop triggered after sustainSeconds");

        if (emitter) emitter.StopAll();

        absorber?.MagicController?.ForceStopMerge();
        other?.MagicController?.ForceStopMerge();

        _autoStopCo = null;
    }
}
