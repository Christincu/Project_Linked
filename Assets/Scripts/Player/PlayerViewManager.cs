using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;

/// <summary>
/// 플레이어의 ViewObj 생성 및 관리를 담당하는 컴포넌트
/// </summary>
public class PlayerViewManager : MonoBehaviour
{
    #region Private Fields
    private PlayerController _controller;
    private GameDataManager _gameDataManager;
    private GameObject _viewObj;
    #endregion

    #region Properties
    public GameObject ViewObj => _viewObj;
    #endregion

    #region Initialization
    /// <summary>
    /// 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController controller, GameDataManager gameDataManager)
    {
        _controller = controller;
        _gameDataManager = gameDataManager;
        
        // ViewObjParent 생성 및 Interpolation Target 설정
        EnsureViewObjParentExists();
    }
    #endregion

    #region ViewObj Management
    /// <summary>
    /// ViewObjParent가 존재하는지 확인하고 없으면 생성합니다.
    /// NetworkRigidbody2D의 Interpolation Target으로 설정합니다.
    /// </summary>
    private void EnsureViewObjParentExists()
    {
        // ViewObjParent가 이미 존재하는지 확인
        Transform viewObjParent = _controller.transform.Find("ViewObjParent");
        
        if (viewObjParent == null)
        {
            // ViewObjParent 생성
            GameObject viewObjParentObj = new GameObject("ViewObjParent");
            viewObjParentObj.transform.SetParent(_controller.transform, false);
            viewObjParentObj.transform.localPosition = Vector3.zero;
            viewObjParentObj.transform.localRotation = Quaternion.identity;
            viewObjParentObj.transform.localScale = Vector3.one;
            
            viewObjParent = viewObjParentObj.transform;
            
            Debug.Log($"[PlayerViewManager] ViewObjParent created for Player {_controller.PlayerSlot}");
        }
        
        // NetworkRigidbody2D의 Interpolation Target 설정
        var networkRb = _controller.GetComponent<NetworkRigidbody2D>();
        if (networkRb != null)
        {
            if (networkRb.InterpolationTarget == null || networkRb.InterpolationTarget != viewObjParent)
            {
                networkRb.InterpolationTarget = viewObjParent;
                Debug.Log($"[PlayerViewManager] Interpolation Target set to ViewObjParent for Player {_controller.PlayerSlot}");
            }
        }
        else
        {
            Debug.LogWarning($"[PlayerViewManager] NetworkRigidbody2D not found on Player {_controller.PlayerSlot}!");
        }
    }

    /// <summary>
    /// 캐릭터 뷰 오브젝트를 생성합니다. (CharacterIndex 동기화 후 호출)
    /// </summary>
    public void TryCreateView()
    {
        if (_viewObj != null || _gameDataManager == null) return;

        var data = _gameDataManager.CharacterService.GetCharacter(_controller.CharacterIndex);

        if (data != null)
        {
            if (_viewObj != null) Destroy(_viewObj);

            GameObject instance = new GameObject("ViewObj");
            
            // ViewObjParent를 찾아서 그 자식으로 설정
            Transform viewObjParent = _controller.transform.Find("ViewObjParent");
            if (viewObjParent != null)
            {
                instance.transform.SetParent(viewObjParent, false);
                Debug.Log($"[PlayerViewManager] ViewObj created under ViewObjParent for Player {_controller.PlayerSlot}");
            }
            else
            {
                // ViewObjParent가 없으면 루트에 생성 (fallback)
                Debug.LogWarning($"[PlayerViewManager] ViewObjParent not found! Creating ViewObj at root for Player {_controller.PlayerSlot}");
                instance.transform.SetParent(_controller.transform, false);
            }

            _viewObj = Instantiate(data.viewObj, instance.transform);
            
            // ViewObj 생성 후 다른 컴포넌트에 알림
            NotifyViewObjCreated(_viewObj);
        }
    }
    
    /// <summary>
    /// ViewObj가 생성되었을 때 다른 컴포넌트에 알립니다.
    /// </summary>
    private void NotifyViewObjCreated(GameObject viewObj)
    {
        // PlayerAnimationController에 알림
        var animationController = _controller.GetComponent<PlayerAnimationController>();
        if (animationController != null)
        {
            animationController.OnViewObjChanged(viewObj);
        }
        
        // PlayerBarrierVisual에 알림
        var barrierVisual = _controller.GetComponent<PlayerBarrierVisual>();
        if (barrierVisual != null)
        {
            barrierVisual.OnViewObjChanged(viewObj);
        }
    }
    #endregion

    #region Render
    /// <summary>
    /// 렌더링 시 ViewObj 위치를 동기화합니다.
    /// </summary>
    public void SyncViewObjPosition()
    {
        if (_viewObj != null)
        {
            _viewObj.transform.localPosition = Vector3.zero;
        }
    }
    #endregion
}

