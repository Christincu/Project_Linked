using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 챕터 선택 버튼
/// </summary>
public class ChapterBtn : MonoBehaviour
{
    private string _sceneName;
    private TitleCanvas _titleCanvas;
    private Button _button;
    [SerializeField] private TextMeshProUGUI _buttonText;

    void Awake()
    {
        _button = GetComponent<Button>();
        _buttonText = GetComponentInChildren<TextMeshProUGUI>();
    }

    /// <summary>
    /// 챕터 버튼을 초기화합니다.
    /// </summary>
    public void Initialize(string sceneName, string displayName, TitleCanvas canvas)
    {
        _sceneName = sceneName;
        _titleCanvas = canvas;
        
        if (_buttonText != null)
        {
            _buttonText.text = displayName;
        }
        
        if (_button != null)
        {
            _button.onClick.AddListener(OnClick);
        }
    }

    public void OnClick()
    {
        if (_titleCanvas != null)
        {
            _titleCanvas.OnChapterSelected(_sceneName);
        }
    }

    void OnDestroy()
    {
        if (_button != null)
        {
            _button.onClick.RemoveListener(OnClick);
        }
    }
}
