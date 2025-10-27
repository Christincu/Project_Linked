using UnityEngine;

// 프래팹 모양 고정

[DisallowMultipleComponent]
public class LockParticleShapeToPrefab : MonoBehaviour
{
    [SerializeField] bool applyEveryEnable = true;

    ParticleSystem ps;
    ParticleSystem.MainModule main0;
    ParticleSystem.ShapeModule shape0;

    float startLifetimeMin, startLifetimeMax;
    float startSpeedMin, startSpeedMax;
    ParticleSystemSimulationSpace simSpace;
    float angle, radius, arc;

    void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        var main = ps.main;
        var shape = ps.shape;

        startLifetimeMin = main.startLifetime.constantMin;
        startLifetimeMax = main.startLifetime.constantMax;
        startSpeedMin = main.startSpeed.constantMin;
        startSpeedMax = main.startSpeed.constantMax;
        simSpace = main.simulationSpace;

        angle = shape.angle;
        radius = shape.radius;
        arc = shape.arc;

        main0 = main;
        shape0 = shape;
    }

    void OnEnable()
    {
        if (applyEveryEnable) Reapply();
    }

#if UNITY_EDITOR
    void OnValidate() { if (ps) Reapply(); }
#endif

    public void Reapply()
    {
        var main = ps.main;
        var shape = ps.shape;

        main.simulationSpace = simSpace;
        main.startLifetime = new ParticleSystem.MinMaxCurve(startLifetimeMin, startLifetimeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(startSpeedMin, startSpeedMax);

        shape.enabled = true;
        shape.angle = angle;
        shape.radius = radius;
        shape.arc = arc;

        ps.Clear(true);
        ps.Play(true);
    }
}
