using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Character", menuName = "Game/Character Data")]
public class CharacterData : ScriptableObject
{
    [Header("Character Info")]
    [Tooltip("Character name")]
    public string characterName;
    
    [Header("Character Images")]
    [Tooltip("Character profile icon (for UI)")]
    public Sprite profileIcon;
    
    [Tooltip("Character sprite (for game)")]
    public Sprite characterSprite;
    public RuntimeAnimatorController characterAnimator;

    [Header("Optional Info")]
    [TextArea(3, 5)]
    [Tooltip("Character description")]
    public string description;
}

