using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로딩 화면 패널을 관리합니다.
/// 싱글톤 패턴으로 어디서든 접근 가능하며, 페이드 인/아웃 애니메이션을 제공합니다.
/// </summary>
public class LoadingPanel : MonoBehaviour
{
    public static LoadingPanel Instance { get; private set; }

    [SerializeField] private float _fadeInDuration = 0.3f;
    [SerializeField] private float _fadeOutDuration = 0.5f;

    private Image _image;
    private Animator _animator;
    private Coroutine _currentOperation = null;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        _animator = GetComponent<Animator>();
        _image = GetComponent<Image>();
        
        if (_animator == null)
        {
            Debug.LogError("[LoadingPanel] Animator component not found!");
        }
        if (_image == null)
        {
            Debug.LogError("[LoadingPanel] Image component not found!");
        }
    }

    void Start()
    {
        if (_image != null)
        {
            _image.raycastTarget = false;
        }

        Close();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    #region Public API
    /// <summary>
    /// 로딩 화면을 표시합니다 (수동 제어용).
    /// </summary>
    public static void Show()
    {
        if (Instance == null) return;
        Instance.ShowInternal();
    }

    /// <summary>
    /// 로딩 화면을 숨깁니다 (수동 제어용).
    /// </summary>
    public static void Hide()
    {
        if (Instance == null) return;
        Instance.HideInternal();
    }

    /// <summary>
    /// 지정된 시간 동안 로딩 화면을 표시합니다 (자동으로 숨김).
    /// </summary>
    public static void ShowForSeconds(float duration)
    {
        if (Instance == null) return;
        Instance.ShowForSecondsInternal(duration);
    }

    /// <summary>
    /// 코루틴 작업이 완료될 때까지 로딩 화면을 표시합니다.
    /// </summary>
    public static Coroutine ShowDuring(IEnumerator operation)
    {
        if (Instance == null) return null;
        return Instance.StartCoroutine(Instance.ShowDuringInternal(operation));
    }

    /// <summary>
    /// 비동기 작업이 완료될 때까지 로딩 화면을 표시합니다.
    /// </summary>
    public static void ShowDuringAsync(Func<IEnumerator> operationFunc, Action onComplete = null)
    {
        if (Instance == null) return;
        Instance.StartCoroutine(Instance.ShowDuringAsyncInternal(operationFunc, onComplete));
    }
    #endregion

    #region Internal Methods
    private void ShowInternal()
    {
        if (_image != null)
        {
            _image.raycastTarget = true;
        }

        if (_animator != null)
        {
            _animator.Play("Open");
        }
    }

    private void HideInternal()
    {
        if (_animator != null)
        {
            _animator.Play("Close");
        }

        StartCoroutine(DisableAfterAnimation());
    }

    private void ShowForSecondsInternal(float duration)
    {
        if (_currentOperation != null)
        {
            StopCoroutine(_currentOperation);
        }

        _currentOperation = StartCoroutine(ShowForSecondsCoroutine(duration));
    }

    private IEnumerator ShowForSecondsCoroutine(float duration)
    {
        ShowInternal();
        yield return new WaitForSeconds(_fadeInDuration);
        yield return new WaitForSeconds(duration);
        HideInternal();
        _currentOperation = null;
    }

    private IEnumerator ShowDuringInternal(IEnumerator operation)
    {
        ShowInternal();
        yield return new WaitForSeconds(_fadeInDuration);
        yield return operation;
        yield return new WaitForSeconds(0.3f);
        HideInternal();
    }

    private IEnumerator ShowDuringAsyncInternal(Func<IEnumerator> operationFunc, Action onComplete)
    {
        ShowInternal();
        yield return new WaitForSeconds(_fadeInDuration);
        yield return operationFunc();
        yield return new WaitForSeconds(0.3f);
        HideInternal();
        onComplete?.Invoke();
    }

    private IEnumerator DisableAfterAnimation()
    {
        yield return new WaitForSeconds(_fadeOutDuration);

        if (_image != null)
        {
            _image.raycastTarget = false;
        }
    }
    #endregion

    #region Legacy Support (for Animation Events)
    public void Open() => Show();
    public void Close() => Hide();
    #endregion
}
