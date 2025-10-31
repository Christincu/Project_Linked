using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class CutSceneDialogue : MonoBehaviour
{
    #region General
    [Header("General")]
    [Tooltip("플레이 시작 시 자동 재생할지 여부")]
    public bool autoStartOnPlay = false;

    [Tooltip("playByCsvOrder=false일 때만 의미 있음")]
    public string defaultCategory = "1ch_Test1";

    [Tooltip("마우스 좌클릭으로 다음 대사 진행")]
    public bool useMouseLeftClick = true;

    [Tooltip("ESC로 컷신 즉시 종료")]
    public bool allowSkipWithEscape = true;

    [Header("Playback")]
    [Tooltip("카테고리 무시하고 CSV에 적힌 순서(rowIndex)대로 전체 재생")]
    public bool playByCsvOrder = true;

    [Header("Input Guard")]
    [Tooltip("컷신 시작 직후 클릭 입력을 잠시 무시(초)")]
    public float clickBlockDurationOnStart = 0.2f;
    private float _blockUntilTime = 0f;
    private bool _armedForClick = false;
    #endregion

    #region CSV
    [Header("CSV")]
    [Tooltip("CSV(TextAsset): 구분, 순번, 캐릭터명, 일러스트 슬롯, 일러스트, 대사")]
    public TextAsset csv;

    [Tooltip("CSV에 헤더가 있는가? (예: 구분,순번,...)")]
    public bool hasHeader = true;

    [Tooltip("인코딩 강제(예: \"cp949\"). 비우면 UTF-8/BOM 자동 감지")]
    public string csvEncodingOverride = "";

    [Header("CSV Column Index (헤더 없을 때 사용)")]
    public int colCategory = 0;   // 구분
    public int colOrder = 1;      // 순번
    public int colCharacter = 2;  // 캐릭터명
    public int colSlot = 3;       // 일러스트 슬롯 (무시됨)
    public int colIllustrate = 4; // 일러스트
    public int colLine = 5;       // 대사

    [Serializable]
    private class LineData
    {
        public string category;
        public int order;
        public string character;
        public string slot;
        public string illustrate;
        public string line;
        public int rowIndex; // CSV 원래 행 순서
    }

    private readonly List<LineData> _all = new();
    private List<LineData> _cur = new();
    private int _idx = -1;
    private bool _isPlaying = false;
    #endregion

    #region UI
    [Header("UI Roots")]
    public GameObject cutsceneUIRoot;           // 컷신 UI 루트
    public List<GameObject> gameplayUIRoots;    // 컷신 동안 숨길 게임 UI 루트들

    [Header("TMP Texts")]
    public TMP_Text charNameText;               // 캐릭터명
    public TMP_Text dialogueText;               // 대사

    [Header("Portrait Slots")]
    public Image firstImage;   // 왼쪽(직전 화자 배치)
    public Image secondImage;  // 가운데(현재 화자 배치)
    public Image thirdImage;   // 우측(미사용: 필요시 확장)

    // defaultTransparent는 사용하지 않음(없을 땐 완전 비활성)
    #endregion

    #region Visual Config
    [Header("Illustrate Loading")]
    [Tooltip("Resources 기준 기본 경로 (예: Image/Character)")]
    public string baseResourcesPath = "Image/Character";

    [Tooltip("왼쪽(직전 화자) 톤다운 색상 (RGB=145)")]
    public Color dimColor = new Color(145f / 255f, 145f / 255f, 145f / 255f, 1f);

    [Tooltip("가운데(현재 화자) 원색(변형 없음)")]
    public Color speakerColor = Color.white;
    #endregion

    #region Character Folder Aliases
    [Serializable]
    public struct CharacterFolderAlias
    {
        [Tooltip("CSV 캐릭터명(예: 라피, Lafi, 가로, Garo)")]
        public string characterNameKey;
        [Tooltip("실제 폴더명(예: \"Lafi (1P)\", \"Garo (2P)\")")]
        public string folderName;
    }

    [Header("Character Folder Aliases (선택)")]
    public CharacterFolderAlias[] folderAliases;
    private Dictionary<string, string> _aliasMap;
    #endregion

    #region State for speaker switching
    // 현재 화자 / 직전 화자 상태
    private string _currentSpeaker = null;
    private Sprite _currentSprite = null;
    private string _previousSpeaker = null;
    private Sprite _previousSprite = null;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        BuildAliasMap();
        LoadCSV();
        SetUI(false);
        ResetPortraits();

        // 컷신 UI가 Gameplay UI 리스트에 실수로 들어간 경우 제거
        if (gameplayUIRoots != null && cutsceneUIRoot)
        {
            for (int i = gameplayUIRoots.Count - 1; i >= 0; i--)
            {
                if (gameplayUIRoots[i] == cutsceneUIRoot)
                {
                    Debug.LogWarning("[CutSceneDialogue] Cutscene UI Root가 Gameplay UI Roots에 포함되어 제거했습니다.");
                    gameplayUIRoots.RemoveAt(i);
                }
            }
        }

        if (autoStartOnPlay)
            StartCutscene(defaultCategory);
    }

    private void Update()
    {
        if (!_isPlaying) return;

        // 시작 직후 클릭 블록
        if (Time.unscaledTime < _blockUntilTime) return;

        // 눌려있던 클릭이 모두 떼질 때까지 대기
        if (!_armedForClick)
        {
            if (!Input.GetMouseButton(0)) _armedForClick = true;
            return;
        }

        if (useMouseLeftClick && Input.GetMouseButtonDown(0))
            Advance();

        if (allowSkipWithEscape && Input.GetKeyDown(KeyCode.Escape))
            EndCutscene();
    }
    #endregion

    #region Public API
    public void OnClickStartCutscene()
    {
        StartCutscene(defaultCategory);
    }

    public void StartCutscene(string category)
    {
        if (_isPlaying) return;

        if (playByCsvOrder)
        {
            // CSV에 적힌 순서(rowIndex)대로 전부 재생
            _cur = _all.OrderBy(l => l.rowIndex).ToList();
        }
        else
        {
            // 지정 카테고리만 재생
            _cur = _all.Where(l => l.category.Equals(category, StringComparison.OrdinalIgnoreCase))
                       .OrderBy(l => l.order)
                       .ToList();
        }

        if (_cur.Count == 0)
        {
            Debug.LogWarning($"[CutSceneDialogue] 데이터 없음 (playByCsvOrder={playByCsvOrder}, category='{category}')");
            return;
        }

        _isPlaying = true;
        SetUI(true);
        ResetPortraits();
        _currentSpeaker = _previousSpeaker = null;
        _currentSprite = _previousSprite = null;

        // 시작 직후 입력 블록 & 클릭 무장 해제
        _blockUntilTime = Time.unscaledTime + clickBlockDurationOnStart;
        _armedForClick = false;

        // 첫 줄 즉시 표시
        _idx = 0;
        DrawCurrent();
    }

    public void EndCutscene()
    {
        if (!_isPlaying) return;
        _isPlaying = false;
        SetUI(false);
        _currentSpeaker = _previousSpeaker = null;
        _currentSprite = _previousSprite = null;
    }
    #endregion

    #region Internal Playback
    private void Advance()
    {
        if (!_isPlaying) return;

        _idx++;
        if (_idx >= _cur.Count)
        {
            EndCutscene();
            return;
        }

        DrawCurrent();
    }

    private void DrawCurrent()
    {
        if (!_isPlaying) return;
        _idx = Mathf.Clamp(_idx, 0, _cur.Count - 1);

        var line = _cur[_idx];

        if (charNameText) charNameText.text = line.character;
        if (dialogueText) dialogueText.text = line.line;

        ApplyPortrait(line);
    }

    private void SetUI(bool on)
    {
        if (cutsceneUIRoot) cutsceneUIRoot.SetActive(on);
        if (gameplayUIRoots != null)
            foreach (var go in gameplayUIRoots) if (go) go.SetActive(!on);
    }

    private void ResetPortraits()
    {
        // 이미지 초기화(비활성 + 스프라이트 제거)
        ClearImage(firstImage);
        ClearImage(secondImage);
        ClearImage(thirdImage);

        if (charNameText) charNameText.text = "";
        if (dialogueText) dialogueText.text = "";
    }
    #endregion

    #region Portrait / Speaker Rules
    private void ApplyPortrait(LineData line)
    {
        // 0) 스프라이트 로드
        Sprite s = LoadIllustrateSprite(line.character, line.illustrate);
        // s가 null이면 해당 슬롯은 비활성 처리(흰 배경 없이 공백)
        bool hasSprite = s != null;

        // 1) 화자 변경 여부 판단
        bool speakerChanged = (_currentSpeaker == null) || !string.Equals(_currentSpeaker, line.character, StringComparison.OrdinalIgnoreCase);

        if (speakerChanged)
        {
            // 직전 화자를 왼쪽으로 보냄 + 톤다운
            _previousSpeaker = _currentSpeaker;
            _previousSprite = _currentSprite;

            if (!string.IsNullOrEmpty(_previousSpeaker))
            {
                if (_previousSprite) // 스프라이트 있는 경우만 표시
                {
                    ShowImage(firstImage, _previousSprite, dimColor, sendToBack: true);
                }
                else
                {
                    ClearImage(firstImage);
                }
            }
            else
            {
                ClearImage(firstImage);
            }

            // 새 화자를 가운데로 + 원색 + 맨 위
            _currentSpeaker = line.character;
            _currentSprite = s;

            if (hasSprite)
                ShowImage(secondImage, s, speakerColor, bringToFront: true);
            else
                ClearImage(secondImage);

            // 우측 슬롯은 현 규칙에선 사용하지 않음(비활성)
            ClearImage(thirdImage);
        }
        else
        {
            // 같은 화자가 계속 말함 → 가운데 이미지만 갱신
            _currentSprite = s;

            if (hasSprite)
                ShowImage(secondImage, s, speakerColor, bringToFront: true);
            else
                ClearImage(secondImage);

            // 왼쪽(직전 화자) 유지, 우측은 비활성
            // 단, 직전 화자 스프라이트가 없었다면 빈 상태 유지
            if (_previousSprite)
                ShowImage(firstImage, _previousSprite, dimColor, sendToBack: true);
            else
                ClearImage(firstImage);

            ClearImage(thirdImage);
        }
    }

    private void ShowImage(Image img, Sprite sprite, Color tint, bool bringToFront = false, bool sendToBack = false)
    {
        if (!img) return;
        img.enabled = true;
        img.sprite = sprite;

        img.material = null;                 // 기본 UI 머티리얼
        img.canvasRenderer.SetAlpha(1f);     // 완전 불투명
        img.color = tint;                  // 화자는 Color.white, 직전 화자는 dimColor

        if (bringToFront) img.transform.SetAsLastSibling();
        else if (sendToBack) img.transform.SetAsFirstSibling();
    }


    private void ClearImage(Image img)
    {
        if (!img) return;
        img.enabled = false;
        img.sprite = null;
        // color는 보존해도 되지만, 에디터 미리보기 혼동 방지를 위해 원색으로
        img.color = Color.white;
    }
    #endregion

    #region Resource Loading & Helpers
    private void BuildAliasMap()
    {
        _aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (folderAliases == null) return;

        foreach (var a in folderAliases)
        {
            if (string.IsNullOrWhiteSpace(a.characterNameKey) || string.IsNullOrWhiteSpace(a.folderName))
                continue;
            _aliasMap[NormalizeKey(a.characterNameKey)] = a.folderName.Trim();
        }
    }

    private static string NormalizeKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var b = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch > 127) // 한글 포함
                b.Append(char.ToLowerInvariant(ch));
        }
        return b.ToString();
    }

    private Sprite LoadIllustrateSprite(string characterName, string illustrate)
    {
        if (string.IsNullOrWhiteSpace(illustrate))
            return null;

        // alias 매핑 우선
        string key = NormalizeKey(characterName);
        string folderName = null;
        if (_aliasMap != null && _aliasMap.TryGetValue(key, out var mapped))
            folderName = mapped;

        var basePath = baseResourcesPath.TrimEnd('/');
        var candidates = new List<string>();

        if (!string.IsNullOrEmpty(folderName))
            candidates.Add($"{basePath}/{folderName}/{illustrate}");

        candidates.Add($"{basePath}/{characterName} (1P)/{illustrate}");
        candidates.Add($"{basePath}/{characterName} (2P)/{illustrate}");
        candidates.Add($"{basePath}/{characterName} (3P)/{illustrate}");
        candidates.Add($"{basePath}/{characterName}/{illustrate}");
        candidates.Add($"{basePath}/{illustrate}");

        foreach (var path in candidates)
        {
            var sp = Resources.Load<Sprite>(path);
            if (sp) return sp;
        }

        // 공백/괄호 제거 버전도 시도
        string simple = StripSpecial(illustrate);
        if (simple != illustrate)
        {
            var sp = Resources.Load<Sprite>($"{basePath}/{simple}");
            if (sp) return sp;
        }

        // 못 찾으면 null 반환 → 호출부에서 Image 비활성 처리
        return null;
    }

    private static string StripSpecial(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var b = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch > 127) b.Append(ch);
        }
        return b.ToString();
    }

    private static int SafeInt(string s) => int.TryParse(s, out var v) ? v : 0;

    private static List<string> ParseCsv(string line)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        bool inQ = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\"')
            {
                if (inQ && i + 1 < line.Length && line[i + 1] == '\"') { sb.Append('\"'); i++; }
                else inQ = !inQ;
            }
            else if (c == ',' && !inQ)
            {
                list.Add(sb.ToString()); sb.Clear();
            }
            else sb.Append(c);
        }
        list.Add(sb.ToString());
        return list;
    }
    #endregion

    #region CSV Loading
    private void LoadCSV()
    {
        _all.Clear();
        if (!csv)
        {
            Debug.LogWarning("[CutSceneDialogue] CSV 미지정");
            return;
        }

        Encoding enc = Encoding.UTF8;
        if (!string.IsNullOrEmpty(csvEncodingOverride))
        {
            try { enc = Encoding.GetEncoding(csvEncodingOverride); }
            catch { Debug.LogWarning($"[CutSceneDialogue] 인코딩 '{csvEncodingOverride}' 실패 → UTF-8 사용"); }
        }

        using var ms = new MemoryStream(csv.bytes);
        using var sr = new StreamReader(ms, enc, detectEncodingFromByteOrderMarks: true);

        string line;
        bool skippedHeader = !hasHeader;
        int row = 0;

        while ((line = sr.ReadLine()) != null)
        {
            if (!skippedHeader) { skippedHeader = true; continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = ParseCsv(line);
            if (cells.Count <= Math.Max(colLine, Math.Max(colCategory, colOrder)))
                continue;

            _all.Add(new LineData
            {
                category = cells[colCategory].Trim(),
                order = SafeInt(cells[colOrder]),
                character = cells[colCharacter].Trim(),
                slot = cells[colSlot].Trim(),
                illustrate = cells[colIllustrate].Trim(),
                line = cells[colLine],
                rowIndex = row++
            });
        }

        // playByCsvOrder=true면 CSV 원래 순서 유지, false면 Category→Order로 정렬
        if (!playByCsvOrder)
        {
            _all.Sort((a, b) =>
            {
                int c = string.Compare(a.category, b.category, StringComparison.OrdinalIgnoreCase);
                return (c != 0) ? c : a.order.CompareTo(b.order);
            });
        }
    }
    #endregion

}
