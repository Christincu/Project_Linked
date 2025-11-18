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
    #region Events / State
    public event Action OnClosed;      // 대사 종료(UI 비활성) 시 알림
    public bool IsPlaying => _isPlaying;
    #endregion

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
    [Tooltip("CSV(TextAsset): Category, order, CharacterName, IllustrateSlot, IllustrateName, Line")]
    public TextAsset csv;

    [Tooltip("CSV에 헤더가 있는가? (반드시 사용 권장)")]
    public bool hasHeader = true;

    [Tooltip("인코딩 강제(예: \"cp949\"). 비우면 UTF-8/BOM 자동 감지")]
    public string csvEncodingOverride = "";

    [Header("CSV Column Index (헤더 없을 때만 사용)")]
    public int colCategory = 0;   // Category
    public int colOrder = 1;      // order
    public int colCharacter = 2;  // CharacterName
    public int colSlot = 3;       // IllustrateSlot
    public int colIllustrate = 4; // IllustrateName
    public int colLine = 5;       // Line

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
    [Tooltip("컷신 패널 루트(필수)")]
    public GameObject cutsceneUIRoot;           // 반드시 연결!

    [Header("Gameplay UI (Optional)")]
    [Tooltip("컷신 켤 때 숨길 게임 UI 루트들 (선택)")]
    public List<GameObject> gameplayUIRoots = new List<GameObject>();

    [Header("TMP Texts")]
    public TMP_Text charNameText;               // 캐릭터명
    public TMP_Text dialogueText;               // 대사

    [Header("Portrait Slots (선택)")]
    public Image firstImage;   // 왼쪽(직전 화자)
    public Image secondImage;  // 가운데(현 화자)
    public Image thirdImage;   // 우측(미사용)
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
        public string characterNameKey; // CSV 캐릭터명
        public string folderName;       // 실제 폴더명
    }

    [Header("Character Folder Aliases (선택)")]
    public CharacterFolderAlias[] folderAliases;
    private Dictionary<string, string> _aliasMap;
    #endregion

    #region Speaker State
    private string _currentSpeaker = null;
    private Sprite _currentSprite = null;
    private string _previousSpeaker = null;
    private Sprite _previousSprite = null;
    #endregion

    #region Unity
    private void Awake()
    {
        BuildAliasMap();
        LoadCSV();

        // 컷신 시작 전 상태: 컷신 패널 Off, 게임 UI On
        SetUI(false);
        ToggleGameplayUI(true);
        ResetPortraits();

        // 컷신 UI가 Gameplay 목록에 들어가 있으면 제거(실수 방지)
        if (cutsceneUIRoot != null && gameplayUIRoots != null)
            gameplayUIRoots.RemoveAll(go => go == cutsceneUIRoot);

        if (autoStartOnPlay)
            StartCutscene(defaultCategory);
    }

    private void Update()
    {
        if (!_isPlaying) return;

        if (Time.unscaledTime < _blockUntilTime) return;

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
    public void StartCutscene(string category)
    {
        if (_isPlaying) return;

        // 재생 목록 구성
        if (playByCsvOrder)
            _cur = _all.OrderBy(l => l.rowIndex).ToList();
        else
            _cur = _all.Where(l => l.category.Equals(category, StringComparison.OrdinalIgnoreCase))
                       .OrderBy(l => l.order).ToList();

        if (_cur.Count == 0)
        {
            Debug.LogWarning($"[CutSceneDialogue] 데이터 없음 (playByCsvOrder={playByCsvOrder}, category='{category}')");
            return;
        }

        _isPlaying = true;

        ToggleGameplayUI(false);  // 게임 UI 숨김
        SetUI(true);              // 컷신 UI 켜기
        ResetPortraits();

        _currentSpeaker = _previousSpeaker = null;
        _currentSprite = _previousSprite = null;

        _blockUntilTime = Time.unscaledTime + clickBlockDurationOnStart;
        _armedForClick = false;

        _idx = -1;
        Advance(); // 첫 줄 표시(빈줄 자동 스킵 포함)
    }

    public void EndCutscene()
    {
        if (!_isPlaying) return;

        _isPlaying = false;

        SetUI(false);             // 컷신 UI 끄기
        ToggleGameplayUI(true);   // 게임 UI 복구

        _currentSpeaker = _previousSpeaker = null;
        _currentSprite = _previousSprite = null;

        OnClosed?.Invoke();
    }
    #endregion

    #region Draw / Advance
    private void Advance()
    {
        if (!_isPlaying) return;

        _idx++;

        // 캐릭터/일러/대사 모두 빈 줄은 자동 스킵
        while (_idx < _cur.Count)
        {
            var l = _cur[_idx];
            bool emptyRow = string.IsNullOrWhiteSpace(l.character)
                         && string.IsNullOrWhiteSpace(l.illustrate)
                         && string.IsNullOrWhiteSpace(l.line);
            if (!emptyRow) break;
            _idx++;
        }

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

        var line = _cur[_idx];

        if (charNameText) charNameText.text = line.character;
        if (dialogueText) dialogueText.text = line.line;

        ApplyPortrait(line);
    }

    private void SetUI(bool on)
    {
        if (cutsceneUIRoot) cutsceneUIRoot.SetActive(on);
    }

    private void ToggleGameplayUI(bool on)
    {
        if (gameplayUIRoots == null) return;
        foreach (var go in gameplayUIRoots)
            if (go) go.SetActive(on);
    }

    private void ResetPortraits()
    {
        ClearImage(firstImage);
        ClearImage(secondImage);
        ClearImage(thirdImage);

        if (charNameText) charNameText.text = "";
        if (dialogueText) dialogueText.text = "";
    }
    #endregion

    #region Portrait
    private void ApplyPortrait(LineData line)
    {
        Sprite s = LoadIllustrateSprite(line.character, line.illustrate);
        bool hasSprite = s != null;

        bool speakerChanged = (_currentSpeaker == null)
                           || !string.Equals(_currentSpeaker, line.character, StringComparison.OrdinalIgnoreCase);

        if (speakerChanged)
        {
            _previousSpeaker = _currentSpeaker;
            _previousSprite = _currentSprite;

            if (!string.IsNullOrEmpty(_previousSpeaker))
            {
                if (_previousSprite) ShowImage(firstImage, _previousSprite, dimColor, sendToBack: true);
                else ClearImage(firstImage);
            }
            else ClearImage(firstImage);

            _currentSpeaker = line.character;
            _currentSprite = s;

            if (hasSprite) ShowImage(secondImage, s, speakerColor, bringToFront: true);
            else ClearImage(secondImage);

            ClearImage(thirdImage);
        }
        else
        {
            _currentSprite = s;

            if (hasSprite) ShowImage(secondImage, s, speakerColor, bringToFront: true);
            else ClearImage(secondImage);

            if (_previousSprite) ShowImage(firstImage, _previousSprite, dimColor, sendToBack: true);
            else ClearImage(firstImage);

            ClearImage(thirdImage);
        }
    }

    private void ShowImage(Image img, Sprite sprite, Color tint, bool bringToFront = false, bool sendToBack = false)
    {
        if (!img) return;
        img.enabled = true;
        img.sprite = sprite;
        img.material = null;
        img.canvasRenderer.SetAlpha(1f);
        img.color = tint;

        if (bringToFront) img.transform.SetAsLastSibling();
        else if (sendToBack) img.transform.SetAsFirstSibling();
    }

    private void ClearImage(Image img)
    {
        if (!img) return;
        img.enabled = false;
        img.sprite = null;
        img.color = Color.white;
    }
    #endregion

    #region Helpers
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
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch > 127)
                b.Append(char.ToLowerInvariant(ch));
        }
        return b.ToString();
    }

    private Sprite LoadIllustrateSprite(string characterName, string illustrate)
    {
        if (string.IsNullOrWhiteSpace(illustrate))
            return null;

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

        string simple = StripSpecial(illustrate);
        if (simple != illustrate)
        {
            var sp = Resources.Load<Sprite>($"{basePath}/{simple}");
            if (sp) return sp;
        }

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

    /// <summary>따옴표/콤마 대응 CSV 1줄 파서</summary>
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

            // 최소 칼럼 체크(헤더 파일 기준)
            if (cells.Count <= Math.Max(colLine, Math.Max(colCategory, colOrder)))
                continue;

            // Line 이후는 모두 합쳐 콤마 내성 확보
            string mergedLine = string.Join(",", cells.Skip(colLine)).Trim();

            // 완전 빈 레코드 스킵(캐릭터/일러/대사 모두 공백)
            bool allEmpty = string.IsNullOrWhiteSpace(cells[colCharacter])
                         && string.IsNullOrWhiteSpace(cells[colIllustrate])
                         && string.IsNullOrWhiteSpace(mergedLine);
            if (allEmpty) continue;

            _all.Add(new LineData
            {
                category = cells[colCategory].Trim(),
                order = SafeInt(cells[colOrder]),
                character = cells[colCharacter].Trim(),
                slot = (cells.Count > colSlot ? cells[colSlot] : "").Trim(),
                illustrate = cells[colIllustrate].Trim(),
                line = mergedLine,
                rowIndex = row++
            });
        }

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
