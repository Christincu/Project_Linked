using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class WarningPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _warningText = null;

    public void Initialize(string warningText)
    {
        gameObject.SetActive(true);

        _warningText.text = warningText;
    }

    public void Close()
    {
        Destroy(gameObject);
    }
}
