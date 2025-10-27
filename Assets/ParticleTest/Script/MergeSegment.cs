using UnityEngine;

public class MergeSegment : MonoBehaviour
{
    private float _life;

    public void Init(float life) => _life = life;

    void Update()
    {
        _life -= Time.deltaTime;
        if (_life <= 0f) Destroy(gameObject);
    }
}
