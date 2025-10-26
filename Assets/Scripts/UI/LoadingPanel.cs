using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로딩 화면 패널을 관리합니다.
/// 싱글톤 패턴으로 어디서든 접근 가능하며, 페이드 인/아웃 애니메이션을 제공합니다.
/// </summary>
public class LoadingPanel : MonoBehaviour
{
    public static LoadingPanel Instance { get; private set; }

    private CanvasGroup _canvasGroup;
    private Animator _animator;
    private bool _isHiding = false;

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
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_animator == null)
        {
            Debug.LogError("[LoadingPanel] Animator component not found!");
        }
    }

    void Start()
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public static void Show()
    {
        if (Instance == null)
        {
            Debug.LogWarning("[LoadingPanel] Instance is null! Make sure LoadingPanel prefab is set in GameManager.");
            return;
        }

        Instance._isHiding = false;

        if (Instance._animator != null)
        {
            Instance._animator.Play("Open");
        }
        Debug.Log("[LoadingPanel] Show - Open Animation Started");
    }

    public static void Hide()
    {
        if (Instance == null)
        {
            Debug.LogWarning("[LoadingPanel] Instance is null, cannot hide");
            return;
        }

        if (Instance._isHiding) return;

        Instance._isHiding = true;

        if (Instance._animator != null)
        {
            Instance._animator.Play("Close");
            Instance.StartCoroutine(Instance.DisableAfterAnimation(1.5f));
        }
        Debug.Log("[LoadingPanel] Hide - Close Animation Started");
    }

    private IEnumerator DisableAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (_isHiding)
        {
            gameObject.SetActive(false);
            Debug.Log("[LoadingPanel] Disabled");
        }
    }

    private IEnumerator DisableAfterAnimation(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (_isHiding && _canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            Debug.Log("[LoadingPanel] Fully Hidden (CanvasGroup Alpha 0)");
        }
        _isHiding = false;
    }

    public void Open()
    {
        Show();
    }

    public void Close()
    {
        Hide();
    }
}
