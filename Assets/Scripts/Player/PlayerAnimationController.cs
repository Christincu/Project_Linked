using UnityEngine;
using Fusion;

/// <summary>
/// 플레이어의 애니메이션을 관리하는 컴포넌트
/// </summary>
public class PlayerAnimationController : MonoBehaviour
{
    private const float MIN_MOVEMENT_SPEED = 0.1f;
    private PlayerController _controller;
    private Animator _animator;
    private GameObject _viewObj;
    private Vector2 _previousPosition;
    private string _lastAnimationState = "";

    public Animator Animator => _animator;

    /// <summary>
    /// 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController controller)
    {
        _controller = controller;

        if (_viewObj != null)
        {
            _animator = _viewObj.GetComponent<Animator>();
        }
        
        _previousPosition = controller.transform.position;
    }

    /// <summary>
    /// 실제 이동 거리를 기반으로 애니메이션 상태를 업데이트합니다. (서버 전용)
    /// </summary>
    public void UpdateAnimation()
    {
        if (_controller == null || _animator == null || _controller.IsDead) return;
        if (_controller.Runner == null) return;

        Vector2 currentPos = _controller.transform.position;
        Vector2 actualMovement = currentPos - _previousPosition;
        float actualSpeed = actualMovement.magnitude / _controller.Runner.DeltaTime;

        if (actualSpeed < MIN_MOVEMENT_SPEED)
        {
            _controller.AnimationState = "idle";
        }
        else
        {
            if (Mathf.Abs(actualMovement.y) > Mathf.Abs(actualMovement.x))
            {
                _controller.AnimationState = actualMovement.y > 0 ? "up" : "down";
            }
            else
            {
                _controller.AnimationState = "horizontal";
                _controller.ScaleX = actualMovement.x < 0 ? 1f : -1f;
            }
        }
        
        _previousPosition = currentPos;
    }

    /// <summary>
    /// 애니메이션을 재생합니다 (중복 재생 방지).
    /// </summary>
    public void PlayAnimation(string stateName)
    {
        if (_animator != null && !string.IsNullOrEmpty(stateName) && _lastAnimationState != stateName)
        {
            _animator.Play(stateName);
            _lastAnimationState = stateName;
        }
    }

    /// <summary>
    /// 뷰 오브젝트의 스케일을 업데이트합니다 (좌우 반전).
    /// </summary>
    public void UpdateScale()
    {
        if (_viewObj != null)
        {
            Vector3 scale = _viewObj.transform.localScale;
            scale.x = _controller.ScaleX;
            _viewObj.transform.localScale = scale;
        }
    }
    
    /// <summary>
    /// ViewObj가 변경되었을 때 호출합니다.
    /// </summary>
    public void OnViewObjChanged(GameObject newViewObj)
    {
        _viewObj = newViewObj;
        if (_viewObj != null)
        {
            _animator = _viewObj.GetComponent<Animator>();
        }
        else
        {
            _animator = null;
        }
    }
}

