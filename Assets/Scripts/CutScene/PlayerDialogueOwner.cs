using UnityEngine;

/// <summary>
/// 각 플레이어 오브젝트에 붙여서
/// - 캐릭터 ID(Lafi/Garo 등)를 들고 있게 하는 스크립트.
/// UI는 존에서 공통 CutSceneDialogue 하나만 쓰므로 여기선 안 들고 있어도 됨.
/// </summary>
public class PlayerDialogueOwner : MonoBehaviour
{
    [Tooltip("이 플레이어의 캐릭터 ID (예: Lafi, Garo)")]
    public string characterId;

    // 전역 순번 카운터 (1번 = Lafi, 2번 = Garo ...)
    private static int _globalOrder = 0;

    private void Awake()
    {
        // 이미 세팅돼 있으면 건드리지 않음
        if (!string.IsNullOrEmpty(characterId))
            return;

        AssignCharacterId();
    }

    /// <summary>
    /// 아직 characterId가 비어 있으면 순번에 따라 자동 부여
    /// </summary>
    private void AssignCharacterId()
    {
        if (!string.IsNullOrEmpty(characterId))
            return;

        _globalOrder++;
        int index = _globalOrder;

        switch (index)
        {
            case 1:
                characterId = "Lafi";
                break;
            case 2:
                characterId = "Garo";
                break;
            default:
                characterId = "Lafi";
                break;
        }

        Debug.Log($"[PlayerDialogueOwner] #{index} 에게 characterId = {characterId} 자동 부여", this);
    }

    /// <summary>
    /// 외부에서 편하게 호출:
    /// - 아직 PlayerDialogueOwner가 없으면 AddComponent
    /// - characterId 비어 있으면 여기서 자동 부여
    /// </summary>
    public static PlayerDialogueOwner GetOrAdd(GameObject go)
    {
        if (!go) return null;

        var owner = go.GetComponent<PlayerDialogueOwner>();
        if (!owner)
            owner = go.AddComponent<PlayerDialogueOwner>();

        if (string.IsNullOrEmpty(owner.characterId))
            owner.AssignCharacterId();

        return owner;
    }
}
