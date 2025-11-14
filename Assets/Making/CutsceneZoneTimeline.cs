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
    [Header("Control Lock")]
    [Tooltip("컷신/대사 중 비활성화할 플레이어 컨트롤 스크립트들만 넣어두기")]
    public List<Behaviour> controlsToDisable = new();
    #endregion

    #region Gameplay UI
    [Header("Gameplay UI (Optional)")]
    [Tooltip("컷신/대사 진행 중 숨길 HUD, 조작 UI 루트들")]
    public List<GameObject> gameplayUIRoots = new List<GameObject>();
    #endregion

    #region Dialogue
    [Header("Dialogue (Optional)")]
    [Tooltip("씬에 있는 CutSceneDialogue 컴포넌트")]
    public CutSceneDialogue dialogue;

    [Tooltip("playByCsvOrder=false 일 때 사용할 카테고리 이름")]
    public string dialogueCategory = "1ch_Test1";
    #endregion

    #region Timeline
    [Header("Timeline (Optional)")]
    [Tooltip("첫 번째 컷신 타임라인(적 등장, 카메라 연출 등)")]
    public PlayableDirector cutscene1;

    [Tooltip("두 번째 컷신 타임라인(카메라 이동, 문 파괴 등)")]
    public PlayableDirector cutscene2;
    #endregion

    #region Multiplayer Sync
    [Header("Multiplayer Sync")]
    [Tooltip("체크하면 '모든 플레이어에게 동시에 컷신' 모드로 동작")]
    public bool syncCutsceneForAllPlayers = false;

    [Tooltip("syncCutsceneForAllPlayers=true 일 때, 트리거에 들어온 쪽에서 한 번만 호출되는 이벤트.\n")]
    public UnityEvent onRequestSyncStart;
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

    #region Flow 진입부

    private void TryStart()
    {
        if (_started) return;

        if (_inZonePlayers.Count < Mathf.Max(1, requiredPlayersInZone))
            return;

        if (!useFirstEnteredAsLeader && _inZonePlayers.Count > 0)
            _leader = _inZonePlayers[0];

        _started = true;

        if (syncCutsceneForAllPlayers)
        {
            onRequestSyncStart?.Invoke();
        }
        else
        {
            StartCoroutine(RunFlowWithDelay());
        }
    }

    public void StartSequenceForAll()
    {
        if (_started && !syncCutsceneForAllPlayers)
        {
            return;
        }

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

        LockControls(false, forceAll: true);
        ToggleGameplayUI(true);

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

        // 중복 구독 방지 후 등록
        dialogue.OnClosed -= OnDlgClosed;
        dialogue.OnClosed += OnDlgClosed;

        // 대사 시작
        dialogue.StartCutscene(dialogue.defaultCategory);

        // 내부 재생(IsPlaying) 끝날 때까지 대기
        while (dialogue && dialogue.IsPlaying)
            yield return null;

        // 혹시 안 꺼져 있으면 안전하게 종료 호출
        if (dialogue)
            dialogue.EndCutscene();

        // UI 완전 비활성(OnClosed 이벤트)까지 대기
        while (!closed)
            yield return null;

        // 한 프레임 버퍼 (레이아웃/캔버스 정리)
        yield return null;

        dialogue.OnClosed -= OnDlgClosed;
    }
    #endregion

    #region Timeline
    private IEnumerator RunTimelineSequence()
    {
        // cutscene1 → cutscene2 순서대로 실행
        if (cutscene1)
        {
            cutscene1.Play();
            while (cutscene1.state == PlayState.Playing)
                yield return null;
        }

        if (cutscene2)
        {
            cutscene2.Play();
            while (cutscene2.state == PlayState.Playing)
                yield return null;
        }
    }
    #endregion

    #region Control & UI helpers

    private void LockControls(bool on, bool forceAll = false)
    {
        if (controlsToDisable == null) return;

        foreach (var comp in controlsToDisable)
        {
            if (!comp) continue;
            comp.enabled = !on;
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
    #endregion
}
