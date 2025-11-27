using System;
using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "New Stage", menuName = "Game/Stage Data")]
public class StageData : ScriptableObject
{
    public string code;

    [Header("Stage Info")]
    [Tooltip("Stage name")]
    public string stageName = string.Empty;
    
    [Header("Scene Settings")]
#if UNITY_EDITOR
    [Tooltip("Scene asset for this stage (에디터에서 할당)")]
    [SerializeField] private SceneAsset _sceneAsset;
#endif
    
    [Tooltip("Scene name to load (런타임에서 사용)")]
    [SerializeField] private string _sceneName = string.Empty;
    
    /// <summary>
    /// 이 스테이지의 씬 이름을 반환합니다.
    /// </summary>
    public string SceneName
    {
        get
        {
#if UNITY_EDITOR
            // 에디터에서는 SceneAsset에서 자동으로 씬 이름 추출
            if (_sceneAsset != null)
            {
                return _sceneAsset.name;
            }
#endif
            // 런타임에서는 저장된 씬 이름 사용
            return _sceneName;
        }
    }
    
    [Header("Wave Data")]
    [Tooltip("List of waves for this stage")]
    public List<WaveData> waveDataList = new List<WaveData>();

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(code))
        {
            code = Guid.NewGuid().ToString();
        }
        
        // SceneAsset이 할당되면 자동으로 씬 이름 업데이트
        if (_sceneAsset != null && _sceneName != _sceneAsset.name)
        {
            _sceneName = _sceneAsset.name;
            EditorUtility.SetDirty(this);
        }
    }
#endif
}