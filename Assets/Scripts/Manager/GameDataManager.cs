using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameDataManager : MonoBehaviour
{
    [Header("Services")]
    [SerializeField] private CharacterService _characterService = new CharacterService();

    // Public access to CharacterService
    public CharacterService CharacterService => _characterService;

    // Singleton instance
    public static GameDataManager Instance;
    
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
        }
    }
}
