using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MagicSelectBtn : MonoBehaviour
{
    private MagicData _magicData;
    [SerializeField] private Image _magicImage;
    [SerializeField] private TextMeshProUGUI _magicNameText;
    
    private RectTransform _rectTransform;
    private Vector3 _originalScale;
    private bool _isSelected = false;
    
    public MagicData MagicData => _magicData;
    public int MagicCode => _magicData != null ? _magicData.magicCode : -1;
    public bool IsSelected => _isSelected;

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        if (_rectTransform != null)
        {
            _originalScale = _rectTransform.localScale;
        }
    }

    public void OnInitialized(MagicData magicData) 
    {
        if (magicData == null) return;
        
        _magicData = magicData;
        
        // 마법 아이콘 설정
        if (_magicImage != null && magicData.magicCombinedSprite != null)
        {
            _magicImage.sprite = magicData.magicCombinedSprite;
        }
        
        // 마법 이름 설정
        if (_magicNameText != null && !string.IsNullOrEmpty(magicData.magicName))
        {
            _magicNameText.text = (magicData.magicCode + 1).ToString();
        }
        
        // 버튼 클릭 이벤트 연결
        Button button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }
    }
    
    /// <summary>
    /// 버튼 클릭 시 호출됩니다.
    /// </summary>
    public void OnClick()
    {
        if (_magicData == null) return;
        
        // MainCanvas에 선택 이벤트 전달
        MainCanvas mainCanvas = FindObjectOfType<MainCanvas>();
        if (mainCanvas != null)
        {
            mainCanvas.OnMagicButtonSelected(this);
        }
    }
    
    /// <summary>
    /// 선택 상태로 변경합니다 (버튼 크기 증가).
    /// </summary>
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        
        if (_rectTransform != null)
        {
            if (selected)
            {
                // 선택 시 크기 약간 증가 (1.1배)
                _rectTransform.localScale = _originalScale * 1.1f;
            }
            else
            {
                // 원래 크기로 복귀
                _rectTransform.localScale = _originalScale;
            }
        }
    }
}
