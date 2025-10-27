using UnityEngine;
using System.Linq;

public class MagicMergeOrchestrator : MonoBehaviour
{

    private MagicVFXSpawner _vfx;
    private MagicMergedAttackServer _server;

    void Awake()
    {
        _vfx = FindObjectOfType<MagicVFXSpawner>(true);
        _server = FindObjectOfType<MagicMergedAttackServer>(true);
    }

    void OnEnable()
    {
        foreach (var pmc in FindObjectsOfType<PlayerMagicController>(true))
            Hook(pmc);
    }
    void OnDisable()
    {
        foreach (var pmc in FindObjectsOfType<PlayerMagicController>(true))
            Unhook(pmc);
    }

    void Hook(PlayerMagicController pmc)
    {
        pmc.OnMergeStarted += HandleMergeStart;
        pmc.OnMergeStopped += HandleMergeStop;
    }
    void Unhook(PlayerMagicController pmc)
    {
        pmc.OnMergeStarted -= HandleMergeStart;
        pmc.OnMergeStopped -= HandleMergeStop;
    }

    void HandleMergeStart(PlayerController absorber, PlayerController other)
    {
        if (!_vfx) return;

        int ownerKey = unchecked((int)absorber.Object.Id.Raw);
        int idA = absorber.CharacterIndex;
        int idB = other.CharacterIndex;

        Transform center = absorber.MagicController?.MagicViewObj
            ? absorber.MagicController.MagicViewObj.transform
            : absorber.transform;

        System.Func<Vector3> getAim = () => {
            var cam = Camera.main;
            if (!cam) return center.position + Vector3.right;
            var w = cam.ScreenToWorldPoint(Input.mousePosition);
            w.z = 0; return w;
        };
        System.Func<Vector2> getForward = () =>
            absorber.ScaleX < 0f ? Vector2.right : Vector2.left;

        _vfx.StartConeEmitter(
            ownerKey, idA, idB, center, getAim, getForward,
            totalDuration: 5f, radius: 1.0f, segInterval: 0.20f,
            angleSpawnDelta: 15f, segmentLife: 0.6f
        );

        absorber?.MagicController?.BeginMergeLock(5f);

        if (_server && absorber.Object.HasStateAuthority)
        {
            var fwd = getForward();
            _server.StartServerAttack(absorber, idA, idB, fwd);
        }
    }

    void HandleMergeStop(PlayerController absorber)
    {
        if (!absorber || absorber.Object == null) return;

        int ownerKey = unchecked((int)absorber.Object.Id.Raw);
        if (_vfx) _vfx.StopConeEmitter(ownerKey);

        absorber?.MagicController?.ClearMergeLock();

        if (_server && absorber.Object.HasStateAuthority)
            _server.StopServerAttack(absorber);
    }

}
