using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Character", menuName = "Game/Character Data")]
public class CharacterData : ScriptableObject
{
    public string code;

    [Header("Character Info")]
    [Tooltip("Character name")]
    public string characterName;
    
    [Header("Character Images")]
    [Tooltip("Character profile icon (for UI)")]
    public Sprite profileIcon;
    
    [Tooltip("Character sprite (for game)")]
    public Sprite characterSprite;
    public GameObject viewObj;

    [Header("Optional Info")]
    [TextArea(3, 5)]
    [Tooltip("Character description")]
    public string description;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(code))
        {
            code = Guid.NewGuid().ToString();
        }
    }
#endif
}

