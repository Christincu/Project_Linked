using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로딩 화면 패널을 관리합니다.
/// 페이드 인/아웃 애니메이션을 제공합니다.
/// </summary>
public class LoadingPanel : MonoBehaviour
{
    private Animator _animator = null;

    private void Awake()
    {
        _animator = GetComponent<Animator>();

        if (_animator == null)
        {
            Debug.LogError("[LoadingPanel] Animator component not found!");
        }
    }

    private void Start()
    {
        if (_animator != null)
        {
            _animator.Play("Idle");
            Debug.Log("[LoadingPanel] Initialized and playing Idle animation");
        }
    }

    /// <summary>
    /// 로딩 화면을 엽니다 (페이드 인).
    /// </summary>
    public void Open()
    {
        if (_animator != null)
        {
            _animator.Play("Open");
            Debug.Log("[LoadingPanel] Open animation started (Fade In)");
        }
    }

    /// <summary>
    /// 로딩 화면을 닫습니다 (페이드 아웃).
    /// </summary>
    public void Close()
    {
        if (_animator != null)
        {
            _animator.Play("Close");
            Debug.Log("[LoadingPanel] Close animation started (Fade Out)");
        }
    }
}
