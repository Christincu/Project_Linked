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

    #region Zone Lifetime
    [Header("Zone Lifetime")]
    [Tooltip("체크 시: 컷신이 끝난 뒤에도 이 존을 다시 사용할 수 있음 (반복 가능)")]
    public bool repeatable = false;
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

    [Header("Movement Stop Helper")]
    [Tooltip("컨트롤 잠글 때 Rigidbody2D 속도를 0으로 리셋할지 여부")]
    public bool resetRigidbody2DOnLock = true;
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

    public enum DialogueTargetMode
    {
        AllLocalPlayers,
        ZonePlayersOnly,
        LeaderOnly
    }

    [Header("Dialogue (Optional)")]
    [Tooltip("기본 CutSceneDialogue (필수는 아님, fallback 용)")]
    public CutSceneDialogue dialogue;

    [Tooltip("playByCsvOrder=false 일 때 기본 카테고리 이름")]
    public string dialogueCategory = "1ch_Test1";

    [Header("Dialogue Target")]
    [Tooltip("어느 플레이어에게 대사를 보여줄지 모드")]
    public DialogueTargetMode dialogueTargetMode = DialogueTargetMode.AllLocalPlayers;

    [HideInInspector] public bool showCutsceneForAllLocalPlayers = true;

    [System.Serializable]
    public class CharacterDialogueConfig
    {
        [Tooltip("플레이어의 PlayerDialogueOwner.characterId 와 동일한 값 (예: Lafi, Garo)")]
        public string characterId;

        [Tooltip("이 존에서 해당 캐릭터가 들을 CSV 카테고리 이름")]
        public string categoryName;
    }

    [Header("캐릭터별 대사 카테고리(존 전용)")]
    public List<CharacterDialogueConfig> perCharacterCategories = new();
    #endregion

    #region Timeline
    [Header("Timeline (Optional)")]
    [Tooltip("첫 번째 컷신 타임라인(적 등장, 카메라 연출 등)")]
    public PlayableDirector cutscene1;

    [Tooltip("두 번째 컷신 타임라인(카메라 이동, 문 파괴 등)")]
    public PlayableDirector cutscene2;

    [Header("Dialogue Mid-Timeline (Optional)")]
    [Tooltip("대사 중간에 한 번 재생할 타임라인 (카메라, NPC 연출 등)")]
    public PlayableDirector midTimeline;
    #endregion

    #region Enemy / Battle
    [Header("Enemies / Battle (Optional)")]
    [Tooltip("체크 시: cutscene1 이후 전투를 하고, 적이 모두 사라지면 cutscene2를 재생")]
    public bool useEnemyClearForSecondCutscene = false;

    [Tooltip("감시할 적들(프리팹이 아니라 씬에 있는 Enemy 오브젝트들)")]
    public List<GameObject> enemiesToWatch = new List<GameObject>();
    #endregion

    #region Leader Move Path (중간 연출용)

    [Header("Leader Move Path (Optional)")]
    [Tooltip("중간 컷신에서 1P(리더)를 이동시킬 경로 포인트들")]
    public Transform[] leaderMovePoints;

    [Tooltip("리더 이동에 걸리는 전체 시간(초)")]
    public float leaderMoveDuration = 1.5f;
    #endregion

    #region Obstacle / Signal
    [Header("Obstacle (Signal Target)")]
    [Tooltip("cutscene2 안에서 Signal로 비활성화할 오브젝트 (문/장애물 등)")]
    public GameObject obstacleToDisable;
    #endregion

    #region Fade & Lineup
    [Header("Fade / Lineup (Optional)")]
    [Tooltip("컷신 시작 전에 잠깐 화면을 암전시킬 CanvasGroup (검은 패널)")]
    public CanvasGroup fadeCanvasGroup;

    [Tooltip("암전/복귀에 걸리는 시간")]
    public float fadeDuration = 0.4f;

    [Tooltip("컷신 시작 시 플레이어를 배치할 자리들 (0:리더, 1:그 외 순서)")]
    public Transform[] lineupPositions;

    [Tooltip("라인업 시 플레이어를 천천히 이동시킬지 여부 (false면 순간이동)")]
    public bool smoothMoveToLineup = true;

    [Tooltip("라인업 이동 시간")]
    public float lineupMoveDuration = 0.5f;

    [Tooltip("라인업 후 서로 바라보게 할지 여부(2인 기준)")]
    public bool faceEachOtherAfterLineup = true;
    #endregion

    #region Events
    [Header("Events (Optional)")]
    [Tooltip("전체 시퀀스 끝났을 때 호출되는 이벤트")]
    public UnityEvent onSequenceFinished;
    #endregion

    #region Dialogue Mid-Timeline Trigger 설정

    [Header("Mid Timeline Trigger (대사 중간에 자동 실행)")]
    [Tooltip("체크 시: 지정한 카테고리 / 줄 번호에서 midTimeline + 리더 이동 실행")]
    public bool useMidTimelineTrigger = false;

    [Tooltip("비워두면 모든 카테고리에서 동작, 채우면 해당 카테고리에서만 동작")]
    public string midTimelineCategoryFilter = "";   // 예: "1ch_3"

    [Tooltip("몇 번째 줄을 넘길 때 트리거할지 (1부터 시작). 0 이하이면 사용 안 함")]
    public int midTimelineLineIndex = 0;

    [Tooltip("한 번만 실행할지 여부")]
    public bool midTimelineTriggerOnce = true;

    private bool _midTimelinePlayed = false;
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
        ToggleGameplayUI(false);
        LockControls(true);

        yield return FadeAndLineupPlayers();

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
        SetTimelineUI(false);

        onSequenceFinished?.Invoke();

        if (repeatable)
        {
            _started = false;
            _inZonePlayers.Clear();
            _leader = null;
            _midTimelinePlayed = false;
        }
    }
    #endregion

    #region Dialogue

    private IEnumerator RunDialogue()
    {
        CutSceneDialogue defaultDlg = dialogue;

        List<CutSceneDialogue> targets = new List<CutSceneDialogue>();
        List<string> targetCharIds = new List<string>();

        switch (dialogueTargetMode)
        {
            case DialogueTargetMode.AllLocalPlayers:
                {
                    var allDialogues = FindObjectsOfType<CutSceneDialogue>();
                    foreach (var dlg in allDialogues)
                    {
                        targets.Add(dlg);
                        targetCharIds.Add(null);
                    }
                    break;
                }

            case DialogueTargetMode.ZonePlayersOnly:
                {
                    foreach (var p in _inZonePlayers)
                    {
                        if (!p) continue;
                        var owner = PlayerDialogueOwner.GetOrAdd(p);
                        string charId = owner ? owner.characterId : null;

                        if (defaultDlg)
                        {
                            targets.Add(defaultDlg);
                            targetCharIds.Add(charId);
                        }
                    }
                    break;
                }

            case DialogueTargetMode.LeaderOnly:
                {
                    if (_leader)
                    {
                        var owner = PlayerDialogueOwner.GetOrAdd(_leader);
                        string charId = owner ? owner.characterId : null;

                        if (defaultDlg)
                        {
                            targets.Add(defaultDlg);
                            targetCharIds.Add(charId);
                        }
                    }
                    break;
                }
        }

        if (targets.Count == 0 && defaultDlg)
        {
            targets.Add(defaultDlg);

            string leaderId = null;
            if (_leader)
            {
                var owner = PlayerDialogueOwner.GetOrAdd(_leader);
                if (owner) leaderId = owner.characterId;
            }
            targetCharIds.Add(leaderId);
        }

        if (targets.Count == 0)
        {
            Debug.LogWarning("[CutsceneZoneTimeline] RunDialogue 대상 CutSceneDialogue 없음");
            yield break;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            var dlg = targets[i];
            if (!dlg) continue;

            string charId = (i < targetCharIds.Count) ? targetCharIds[i] : null;

            string category = dialogueCategory;

            if (!string.IsNullOrEmpty(charId))
            {
                string overrideCat = GetCategoryForCharacter(charId);
                if (!string.IsNullOrEmpty(overrideCat))
                    category = overrideCat;
            }

            if (!dlg.playByCsvOrder)
                dlg.defaultCategory = category;
        }

        bool closed = false;
        var mainDlg = targets[0];

        void OnDlgClosed()
        {
            closed = true;
        }

        mainDlg.OnClosed -= OnDlgClosed;
        mainDlg.OnClosed += OnDlgClosed;

        foreach (var dlg in targets)
        {
            if (!dlg) continue;
            dlg.StartCutscene(dlg.defaultCategory);
        }

        while (mainDlg && mainDlg.IsPlaying)
            yield return null;

        if (mainDlg)
            mainDlg.EndCutscene();

        while (!closed)
            yield return null;

        mainDlg.OnClosed -= OnDlgClosed;
    }

    private string GetCategoryForCharacter(string characterId)
    {
        if (perCharacterCategories == null) return null;

        foreach (var cfg in perCharacterCategories)
        {
            if (cfg != null && cfg.characterId == characterId)
                return cfg.categoryName;
        }

        return null;
    }
    #endregion

    #region Timeline + Battle

    private IEnumerator RunTimelineSequence()
    {
        if (cutscene1)
            yield return PlayTimelineWithUI(cutscene1);

        if (useEnemyClearForSecondCutscene && HasEnemyToWatch())
        {
            LockControls(false, forceAll: true);
            ToggleGameplayUI(true);
            SetTimelineUI(false);

            yield return WaitEnemiesCleared();

            ToggleGameplayUI(false);
            LockControls(true, forceAll: true);
        }

        if (cutscene2)
            yield return PlayTimelineWithUI(cutscene2);
    }

    private IEnumerator PlayTimelineWithUI(PlayableDirector director)
    {
        if (!director) yield break;

        ToggleGameplayUI(false);
        SetTimelineUI(true);

        director.Play();

        while (director.state == PlayState.Playing)
        {
            ToggleGameplayUI(false);
            SetTimelineUI(true);
            yield return null;
        }

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

    private IEnumerator MoveLeaderAlongPath()
    {
        if (_leader == null) yield break;
        if (leaderMovePoints == null || leaderMovePoints.Length == 0) yield break;

        Transform t = _leader.transform;

        Vector3 prevPos = t.position;

        int segmentCount = leaderMovePoints.Length;
        float perSegment = leaderMoveDuration / Mathf.Max(1, segmentCount);

        for (int i = 0; i < leaderMovePoints.Length; i++)
        {
            var target = leaderMovePoints[i];
            if (!target) continue;

            Vector3 start = prevPos;
            Vector3 end = target.position;

            float time = 0f;
            while (time < perSegment)
            {
                time += Time.deltaTime;
                float lerp = Mathf.Clamp01(time / perSegment);

                t.position = Vector3.Lerp(start, end, lerp);

                Vector3 dir = end - start;
                if (Mathf.Abs(dir.x) > 0.01f)
                {
                    var s = t.localScale;
                    s.x = Mathf.Abs(s.x) * (dir.x < 0 ? -1f : 1f);
                    t.localScale = s;
                }

                yield return null;
            }

            t.position = end;
            prevPos = end;
        }
    }

    /// <summary>
    /// CutSceneDialogue 쪽에서 "지금 카테고리 / 줄 index 이렇다" 하고 호출하면
    /// 설정에 맞으면 midTimeline + 리더 이동을 실행하고 true 반환.
    /// 안 맞으면 false 반환.
    /// </summary>
    public bool TryPlayMidTimeline(CutSceneDialogue dlg, string category, int lineIndex)
    {
        if (!useMidTimelineTrigger) return false;
        if (!dlg) return false;
        if (midTimelineLineIndex <= 0) return false;

        if (!string.IsNullOrEmpty(midTimelineCategoryFilter))
        {
            if (!string.Equals(midTimelineCategoryFilter, category))
                return false;
        }

        if (lineIndex != midTimelineLineIndex)
            return false;

        if (midTimelineTriggerOnce && _midTimelinePlayed)
            return false;

        _midTimelinePlayed = true;

        StartCoroutine(CoPlayMidTimelineAndResume(dlg));
        return true;
    }
    #endregion

    #region Fade & Lineup helpers

    private IEnumerator FadeAndLineupPlayers()
    {
        if (!fadeCanvasGroup && (lineupPositions == null || lineupPositions.Length == 0))
            yield break;

        if (fadeCanvasGroup)
            yield return FadeCanvas(1f);

        if (lineupPositions != null && lineupPositions.Length > 0)
            yield return LineupPlayers();

        if (fadeCanvasGroup)
            yield return FadeCanvas(0f);
    }

    private IEnumerator FadeCanvas(float targetAlpha)
    {
        if (!fadeCanvasGroup) yield break;

        float start = fadeCanvasGroup.alpha;
        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float lerp = Mathf.Clamp01(t / fadeDuration);
            fadeCanvasGroup.alpha = Mathf.Lerp(start, targetAlpha, lerp);
            yield return null;
        }

        fadeCanvasGroup.alpha = targetAlpha;
    }

    private IEnumerator LineupPlayers()
    {
        if (_inZonePlayers.Count == 0)
            yield break;

        int count = Mathf.Min(_inZonePlayers.Count, lineupPositions.Length);

        List<Vector3> from = new();
        List<Transform> targets = new();

        for (int i = 0; i < count; i++)
        {
            var p = _inZonePlayers[i];
            if (!p) continue;

            var tr = p.transform;
            from.Add(tr.position);
            targets.Add(tr);
        }

        if (!smoothMoveToLineup)
        {
            for (int i = 0; i < targets.Count; i++)
                targets[i].position = lineupPositions[i].position;
        }
        else
        {
            float t = 0f;
            while (t < lineupMoveDuration)
            {
                t += Time.deltaTime;
                float lerp = Mathf.Clamp01(t / lineupMoveDuration);

                for (int i = 0; i < targets.Count; i++)
                {
                    if (!targets[i]) continue;
                    Vector3 pos = Vector3.Lerp(from[i], lineupPositions[i].position, lerp);
                    targets[i].position = pos;
                }

                yield return null;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (!targets[i]) continue;
                targets[i].position = lineupPositions[i].position;
            }
        }

        if (faceEachOtherAfterLineup)
        {
            FacePlayersEachOther(targets);
        }

        yield return null;
    }

    private void FacePlayersEachOther(List<Transform> players)
    {
        if (players == null || players.Count < 2)
            return;

        var p0 = players[0];
        var p1 = players[1];

        Vector3 dir01 = (p1.position - p0.position);
        Vector3 dir10 = -dir01;

        void Face(Transform tr, Vector3 dir)
        {
            if (!tr) return;
            if (Mathf.Abs(dir.x) < 0.01f) return;

            var s = tr.localScale;
            s.x = Mathf.Abs(s.x) * (dir.x < 0 ? -1f : 1f);
            tr.localScale = s;
        }

        Face(p0, dir01);
        Face(p1, dir10);
    }
    #endregion

    #region Control & UI helpers

    private void LockControls(bool on, bool forceAll = false)
    {
        foreach (var b in controlsToDisable)
        {
            if (b) b.enabled = !on;
        }

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

        if (on && resetRigidbody2DOnLock)
        {
            var allPCs = FindObjectsOfType<PlayerController>();
            foreach (var pc in allPCs)
            {
                if (!pc) continue;
                var rb = pc.GetComponent<Rigidbody2D>();
                if (rb)
                {
                    rb.velocity = Vector2.zero;
                    rb.angularVelocity = 0f;
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

    private void SetTimelineUI(bool on)
    {
        if (timelineUIRoot)
            timelineUIRoot.SetActive(on);
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

    public void DisableObstacleFromSignal()
    {
        if (obstacleToDisable && obstacleToDisable.activeSelf)
            obstacleToDisable.SetActive(false);
    }
    #endregion

    #region Dialogue Mid-Timeline API

    public void PlayMidTimelineAndResume(CutSceneDialogue dlg)
    {
        if (!isActiveAndEnabled) return;
        StartCoroutine(CoPlayMidTimelineAndResume(dlg));
    }

    private IEnumerator CoPlayMidTimelineAndResume(CutSceneDialogue dlg)
    {
        if (!dlg) yield break;

        dlg.SendMessage("PauseDialogue", SendMessageOptions.DontRequireReceiver);

        ToggleGameplayUI(false);
        LockControls(true, forceAll: true);
        SetTimelineUI(true);

        // midTimeline 먼저 재생
        if (midTimeline)
        {
            midTimeline.Play();
            while (midTimeline.state == PlayState.Playing)
                yield return null;
        }

        // 필요하면 리더를 웨이포인트 경로로 이동
        if (leaderMovePoints != null && leaderMovePoints.Length > 0)
            yield return MoveLeaderAlongPath();

        SetTimelineUI(false);

        dlg.SendMessage("ResumeDialogue", SendMessageOptions.DontRequireReceiver);
    }
    #endregion
}
