using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class CutsceneZoneTimeline : MonoBehaviour
{
    #region Mode
    public enum FlowMode
    {
        DialogueOnly,
        TimelineOnly,
        DialogueThenTimeline,
        TimelineThenDialogue
    }

    [Header("Flow")]
    [Tooltip("이 존에서 어떤 순서로 진행할지 선택")]
    public FlowMode mode = FlowMode.DialogueThenTimeline;

    [Header("Start Delay")]
    [Tooltip("존에 들어온 뒤 스토리 시작까지 대기(초)")]
    public float startDelaySeconds = 3f;
    #endregion

    #region Zone / Players
    [Header("Zone / Players")]
    [Tooltip("플레이어 태그")]
    public string playerTag = "Player";

    [Tooltip("이 숫자만큼 플레이어가 들어오면 컷신 시작")]
    public int requiredPlayersInZone = 1;

    [Tooltip("먼저 들어온 플레이어를 리더로 사용할지 여부")]
    public bool useFirstEnteredAsLeader = true;

    private readonly List<GameObject> _inZonePlayers = new();
    private GameObject _leader;
    private Collider2D _trigger;
    private bool _started;
    #endregion

    #region Control Lock
    [Header("Control Lock (일반 컴포넌트)")]
    [Tooltip("컷신/대사 중 비활성화할 플레이어 컨트롤 스크립트들")]
    public List<Behaviour> controlsToDisable = new();

    [Tooltip("플레이어 이동 컴포넌트 타입 하나를 지정하면 on/off 일괄 처리")]
    public Behaviour leaderMoveComponent;

    [Header("Control Lock (Fusion PlayerController)")]
    [Tooltip("씬에 있는 PlayerController(NetworkBehaviour)도 같이 on/off 할지 여부")]
    public bool lockFusionPlayerControllers = true;

    [Tooltip("Fusion PlayerController 잠글 때, 이 클라이언트에서 보이는 모든 Player를 잠글지 여부")]
    public bool lockAllPlayersOnThisClient = true;
    #endregion

    #region Gameplay UI
    [Header("Gameplay UI (Optional)")]
    [Tooltip("컷신/대사 진행 중 숨길 HUD, 조작 UI 루트들")]
    public List<GameObject> gameplayUIRoots = new List<GameObject>();
    #endregion

    #region Timeline UI
    [Header("Timeline UI (Optional)")]
    [Tooltip("타임라인 재생 중에만 켤 UI 루트(TL_UI 등)")]
    public GameObject timelineUIRoot;
    #endregion

    #region Dialogue
    [Header("Dialogue (Optional)")]
    [Tooltip("씬에 있는 CutSceneDialogue 컴포넌트")]
    public CutSceneDialogue dialogue;

    [Tooltip("playByCsvOrder=false 일 때 사용할 카테고리 이름")]
    public string dialogueCategory = "1ch_Test1";

    [Header("Multiplayer Cutscene")]
    [Tooltip("체크 시: 이 클라이언트에서 컷신이 시작되면, 같은 씬의 다른 CutSceneDialogue들도 동시에 StartCutscene 호출")]
    public bool showCutsceneForAllLocalPlayers = true;
    #endregion

    #region Timeline
    [Header("Timeline (Optional)")]
    [Tooltip("첫 번째 컷신 타임라인(적 등장, 카메라 연출 등)")]
    public PlayableDirector cutscene1;

    [Tooltip("두 번째 컷신 타임라인(카메라 이동, 문 파괴 등)")]
    public PlayableDirector cutscene2;
    #endregion

    #region Enemy / Battle
    [Header("Enemies / Battle (Optional)")]
    [Tooltip("체크 시: cutscene1 이후 전투를 하고, 적이 모두 사라지면 cutscene2를 재생")]
    public bool useEnemyClearForSecondCutscene = false;

    [Tooltip("감시할 적들(프리팹이 아니라 씬에 있는 Enemy 오브젝트들)")]
    public List<GameObject> enemiesToWatch = new List<GameObject>();
    #endregion

    #region Obstacle / Signal
    [Header("Obstacle (Signal Target)")]
    [Tooltip("cutscene2 안에서 Signal로 비활성화할 오브젝트 (문/장애물 등)")]
    public GameObject obstacleToDisable;
    #endregion

    #region Events
    [Header("Events (Optional)")]
    [Tooltip("전체 시퀀스 끝났을 때 호출되는 이벤트")]
    public UnityEvent onSequenceFinished;
    #endregion

    #region Unity
    private void Awake()
    {
        _trigger = GetComponent<Collider2D>();
        _trigger.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        var go = other.gameObject;
        if (_inZonePlayers.Contains(go)) return;

        _inZonePlayers.Add(go);

        if (useFirstEnteredAsLeader && _leader == null)
            _leader = go;

        TryStart();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag)) return;

        _inZonePlayers.Remove(other.gameObject);
        if (_leader == other.gameObject)
            _leader = null;
    }
    #endregion

    #region Flow
    private void TryStart()
    {
        if (_started) return;

        if (_inZonePlayers.Count < Mathf.Max(1, requiredPlayersInZone))
            return;

        if (!useFirstEnteredAsLeader && _inZonePlayers.Count > 0)
            _leader = _inZonePlayers[0];

        _started = true;
        StartCoroutine(RunFlowWithDelay());
    }

    private IEnumerator RunFlowWithDelay()
    {
        if (startDelaySeconds > 0f)
            yield return new WaitForSeconds(startDelaySeconds);

        yield return RunFlow();
    }

    private IEnumerator RunFlow()
    {
        // 존 시퀀스 시작: HUD 끄고 조작 잠금
        ToggleGameplayUI(false);
        LockControls(true);

        switch (mode)
        {
            case FlowMode.DialogueOnly:
                yield return RunDialogue();
                break;

            case FlowMode.TimelineOnly:
                yield return RunTimelineSequence();
                break;

            case FlowMode.DialogueThenTimeline:
                yield return RunDialogue();
                yield return RunTimelineSequence();
                break;

            case FlowMode.TimelineThenDialogue:
                yield return RunTimelineSequence();
                yield return RunDialogue();
                break;
        }

        // 존 시퀀스 끝: HUD 복구 + 조작 해제
        LockControls(false, forceAll: true);
        ToggleGameplayUI(true);

        SetTimelineUI(false);

        onSequenceFinished?.Invoke();
    }
    #endregion

    #region Dialogue
    private IEnumerator RunDialogue()
    {
        if (!dialogue) yield break;

        if (!dialogue.playByCsvOrder)
            dialogue.defaultCategory = dialogueCategory;

        bool closed = false;
        void OnDlgClosed() => closed = true;

        dialogue.OnClosed -= OnDlgClosed;
        dialogue.OnClosed += OnDlgClosed;

        if (showCutsceneForAllLocalPlayers)
        {
            var allDialogues = FindObjectsOfType<CutSceneDialogue>();
            foreach (var dlg in allDialogues)
            {
                if (!dlg.playByCsvOrder)
                    dlg.defaultCategory = dialogueCategory;
                dlg.StartCutscene(dlg.defaultCategory);
            }
        }
        else
        {
            dialogue.StartCutscene(dialogue.defaultCategory);
        }

        while (dialogue && dialogue.IsPlaying)
            yield return null;

        if (dialogue)
            dialogue.EndCutscene();

        while (!closed)
            yield return null;

        yield return null;

        dialogue.OnClosed -= OnDlgClosed;
    }
    #endregion

    #region Timeline + Battle
    private IEnumerator RunTimelineSequence()
    {
        // cutscene1: 인트로 컷신
        if (cutscene1)
        {
            yield return PlayTimelineWithUI(cutscene1);
        }

        // 전투 구간
        if (useEnemyClearForSecondCutscene && HasEnemyToWatch())
        {
            // 전투 시작: 조작 허용 + HUD 켜기
            LockControls(false, forceAll: true);
            ToggleGameplayUI(true);
            SetTimelineUI(false);

            yield return WaitEnemiesCleared();

            // 전투 끝: 다시 HUD 끄고 조작 잠금 (다음 컷신 대비)
            ToggleGameplayUI(false);
            LockControls(true, forceAll: true);
        }

        // cutscene2: 엔딩 컷신
        if (cutscene2)
        {
            yield return PlayTimelineWithUI(cutscene2);
        }
    }

    // 타임라인 하나를 재생하는 동안:
    // - GameplayUIRoots는 계속 꺼져 있고
    // - timelineUIRoot(TL_UI)는 계속 켜져 있게 유지
    private IEnumerator PlayTimelineWithUI(PlayableDirector director)
    {
        if (!director) yield break;

        // 타임라인 진입 시점
        ToggleGameplayUI(false);
        SetTimelineUI(true);

        director.Play();

        while (director.state == PlayState.Playing)
        {
            // 혹시 다른 곳에서 켰더라도 여기서 다시 강제
            ToggleGameplayUI(false);
            SetTimelineUI(true);
            yield return null;
        }

        // 타임라인 종료 시점
        SetTimelineUI(false);
    }

    private bool HasEnemyToWatch()
    {
        if (enemiesToWatch == null) return false;
        CleanupEnemiesList(enemiesToWatch);
        return enemiesToWatch.Count > 0;
    }

    private IEnumerator WaitEnemiesCleared()
    {
        while (true)
        {
            CleanupEnemiesList(enemiesToWatch);

            if (enemiesToWatch.Count == 0)
                break;

            yield return null;
        }
    }

    private static void CleanupEnemiesList(List<GameObject> list)
    {
        if (list == null) return;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            var go = list[i];
            if (!go || !go.activeInHierarchy)
                list.RemoveAt(i);
        }
    }
    #endregion

    #region Control & UI helpers
    private void LockControls(bool on, bool forceAll = false)
    {
        foreach (var b in controlsToDisable)
            if (b) b.enabled = !on;

        if (leaderMoveComponent)
        {
            var type = leaderMoveComponent.GetType();
            var found = FindObjectsOfType(type) as Behaviour[];

            if (found != null)
            {
                if (on)
                {
                    foreach (var comp in found)
                        comp.enabled = false;

                    if (!forceAll && _leader)
                    {
                        var keep = _leader.GetComponent(type) as Behaviour;
                        if (keep) keep.enabled = true;
                    }
                }
                else
                {
                    foreach (var c in found)
                        c.enabled = true;
                }
            }
        }

        if (lockFusionPlayerControllers)
        {
            var players = FindObjectsOfType<PlayerController>();
            foreach (var pc in players)
            {
                if (!pc) continue;

                if (!lockAllPlayersOnThisClient && _leader != null && pc.gameObject != _leader)
                    continue;

                pc.enabled = !on;
            }
        }
    }

    private void ToggleGameplayUI(bool on)
    {
        if (gameplayUIRoots == null) return;

        foreach (var go in gameplayUIRoots)
        {
            if (!go) continue;
            go.SetActive(on);
        }
    }

    private void SetTimelineUI(bool on)
    {
        if (timelineUIRoot)
            timelineUIRoot.SetActive(on);
    }

    // 기존 타임라인 Signal에서 쓰던 함수들 
    public void LockPlayerControlFromSignal()
    {
        LockControls(true, forceAll: true);
    }

    public void UnlockPlayerControlFromSignal()
    {
        LockControls(false, forceAll: true);
    }

    public void HideGameplayUIFromSignal()
    {
        ToggleGameplayUI(false);
    }

    public void ShowGameplayUIFromSignal()
    {
        ToggleGameplayUI(true);
    }

    public void DisableObstacleFromSignal()
    {
        if (obstacleToDisable && obstacleToDisable.activeSelf)
        {
            obstacleToDisable.SetActive(false);
        }
    }
    #endregion
}
