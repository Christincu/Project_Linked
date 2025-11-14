using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Events;

/// <summary>
/// 트리거 존에 플레이어가 들어오면
/// - 대사(CutSceneDialogue)
/// - 타임라인(PlayableDirector)
/// 를 지정한 순서대로 실행하고, 실행 중에는 조작/게임 UI를 잠시 막는다.
///
/// 모드:
/// - DialogueOnly
/// - TimelineOnly
/// - DialogueThenTimeline
/// - TimelineThenDialogue
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))] // IsTrigger = true
public class CutsceneZoneTimeline : MonoBehaviour
{
    #region ▣ Mode
    public enum FlowMode
    {
        DialogueOnly,          // 존 진입 → 대사만
        TimelineOnly,          // 존 진입 → 타임라인만
        DialogueThenTimeline,  // 존 진입 → 대사 → 타임라인
        TimelineThenDialogue   // 존 진입 → 타임라인 → 대사
    }

    [Header("Flow")]
    [Tooltip("이 존에서 어떤 순서로 진행할지 선택")]
    public FlowMode mode = FlowMode.DialogueThenTimeline;

    [Header("Start Delay")]
    [Tooltip("존에 들어온 뒤 스토리 시작까지 대기(초)")]
    public float startDelaySeconds = 3f;
    #endregion

    #region ▣ Zone / Players
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

    #region ▣ Control Lock
    [Header("Control Lock")]
    [Tooltip("컷신/대사 중 비활성화할 플레이어 컨트롤 스크립트들 (예: PlayerController 등)")]
    public List<Behaviour> controlsToDisable = new();

    [Tooltip("플레이어 이동 컴포넌트 타입 하나를 지정하면 on/off 일괄 처리")]
    public Behaviour leaderMoveComponent;
    #endregion

    #region ▣ Gameplay UI
    [Header("Gameplay UI (Optional)")]
    [Tooltip("컷신/대사 진행 중 숨길 HUD, 조작 UI 루트들")]
    public List<GameObject> gameplayUIRoots = new List<GameObject>();
    #endregion

    #region ▣ Dialogue
    [Header("Dialogue (Optional)")]
    [Tooltip("씬에 있는 CutSceneDialogue 컴포넌트")]
    public CutSceneDialogue dialogue;

    [Tooltip("playByCsvOrder=false 일 때 사용할 카테고리 이름")]
    public string dialogueCategory = "1ch_Test1";
    #endregion

    #region ▣ Timeline
    [Header("Timeline (Optional)")]
    [Tooltip("첫 번째 컷신 타임라인(적 등장, 카메라 연출 등)")]
    public PlayableDirector cutscene1;

    [Tooltip("두 번째 컷신 타임라인(카메라 이동, 문 파괴 등)")]
    public PlayableDirector cutscene2;
    #endregion

    #region ▣ Events
    [Header("Events (Optional)")]
    [Tooltip("전체 시퀀스(Dialogue+Timeline) 끝났을 때 호출되는 이벤트")]
    public UnityEvent onSequenceFinished;
    #endregion

    #region ▣ Unity
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

    #region ▣ Flow
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
        // 0) HUD/조작 UI 숨기기
        ToggleGameplayUI(false);

        // 1) 시작할 때 조작 잠금
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

        // 2) 전체 시퀀스 끝 → 조작 해제 + HUD 복구
        LockControls(false, forceAll: true);
        ToggleGameplayUI(true);

        onSequenceFinished?.Invoke();
    }
    #endregion

    #region ▣ Dialogue
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

    #region ▣ Timeline
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

    #region ▣ Control & UI helpers
    private void LockControls(bool on, bool forceAll = false)
    {
        // 1) 명시한 컨트롤러 on/off
        foreach (var b in controlsToDisable)
            if (b) b.enabled = !on;

        // 2) 타입 단위 일괄 on/off
        if (leaderMoveComponent)
        {
            var type = leaderMoveComponent.GetType();
            var found = FindObjectsOfType(type) as Behaviour[];

            if (found != null)
            {
                if (on)
                {
                    // 전체 비활성
                    foreach (var comp in found)
                        comp.enabled = false;

                    // 리더만 예외적으로 다시 켜기
                    if (!forceAll && _leader)
                    {
                        var keep = _leader.GetComponent(type) as Behaviour;
                        if (keep) keep.enabled = true;
                    }
                }
                else
                {
                    // 전체 활성
                    foreach (var c in found)
                        c.enabled = true;
                }
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

    // ===== Timeline Signal용 헬퍼 =====

    /// <summary>타임라인 Signal에서 호출해서 "조작 잠금"</summary>
    public void LockPlayerControlFromSignal()
    {
        LockControls(true, forceAll: true);
    }

    /// <summary>타임라인 Signal에서 호출해서 "조작 해제"</summary>
    public void UnlockPlayerControlFromSignal()
    {
        LockControls(false, forceAll: true);
    }

    /// <summary>타임라인 Signal에서 호출해서 HUD/조작 UI 숨김</summary>
    public void HideGameplayUIFromSignal()
    {
        ToggleGameplayUI(false);
    }

    /// <summary>타임라인 Signal에서 호출해서 HUD/조작 UI 복구</summary>
    public void ShowGameplayUIFromSignal()
    {
        ToggleGameplayUI(true);
    }
    #endregion
}
