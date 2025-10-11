using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody2D), typeof(PolygonCollider2D))]
public class PlayerCtx : MonoBehaviour, IInvulnerable
{
    [Header("Move")]
    public float MoveSpeed = 1.0f;
    [Range(0.1f, 1.0f)] public float HoldMoveSpeedMultiplier = 1.0f; // 들고 있을 때 이동 배율

    [Header("Bind")]
    public SkillSO SkillAsset;

    [Header("Rendering")]
    public Animator animator;
    public SpriteRenderer CharacterRenderer;

    [Header("Facing")]
    public bool spriteDefaultFacingLeft = false;
    public bool flipColliderWithScale = true;

    [Header("Hold Visual")]
    public GameObject HoldVfxPrefab;
    public Transform HoldVfxAnchor;

    [HideInInspector] public Rigidbody2D RB;
    [HideInInspector] public PolygonCollider2D Col;
    [HideInInspector] public IInputSource Input;

    private readonly Dictionary<string, PlayerState> _states = new();
    private PlayerState _cur;

    public bool IsInvincible { get; private set; }

    public bool IsHolding { get; private set; }
    private GameObject _holdVfxInstance;

    private Vector3 _baseScale;

    void Awake()
    {
        RB = GetComponent<Rigidbody2D>();
        Col = GetComponent<PolygonCollider2D>();
        if (!CharacterRenderer) CharacterRenderer = GetComponent<SpriteRenderer>();
        if (!animator) animator = GetComponent<Animator>();
        Input = new UnityInputSource();

        _baseScale = transform.localScale;

        Register("Idle", new IdleState());
        Register("Move", new MoveState());
        Register("Hold", new HoldState());
        Register("Fire", new FireState());
        Register("Faint", new FaintState());
    }

    void OnEnable() { FusionManager.OnPlayerChangeCharacterEvent += OnAnyPlayerChangeCharacter; }
    void OnDisable() { FusionManager.OnPlayerChangeCharacterEvent -= OnAnyPlayerChangeCharacter; }

    void Start() { Goto("Idle"); ApplyCharacterFromNetwork(); }
    void Update() { _cur?.Tick(); }
    void FixedUpdate() { _cur?.FixedTick(); }

    public void Register(string key, PlayerState s) { s.Bind(this); _states[key] = s; }
    public void Goto(string key)
    {
        if (!_states.ContainsKey(key)) { Debug.LogWarning($"State not found: {key}"); return; }
        _cur?.Exit(); _cur = _states[key]; _cur.Enter();
    }

    public void BeginHold()
    {
        if (IsHolding) return;
        if (SkillAsset == null) return;

        ShowHoldVfx();
        SkillAsset.OnHoldBegin(gameObject, SkillButton.Left);
        IsHolding = true;
    }

    public void EndHold()
    {
        if (!IsHolding) return;
        HideHoldVfx();
        IsHolding = false;
    }

    void ShowHoldVfx()
    {
        if (!HoldVfxPrefab || _holdVfxInstance) return;
        var parent = HoldVfxAnchor ? HoldVfxAnchor : transform;
        _holdVfxInstance = Instantiate(HoldVfxPrefab, parent);
        _holdVfxInstance.transform.localPosition = Vector3.zero;
        _holdVfxInstance.transform.localRotation = Quaternion.identity;
        _holdVfxInstance.transform.localScale = Vector3.one;
    }

    void HideHoldVfx()
    {
        if (_holdVfxInstance)
        {
            Destroy(_holdVfxInstance);
            _holdVfxInstance = null;
        }
    }

    public void GrantInvuln(float seconds) { if (gameObject.activeInHierarchy) StartCoroutine(CoInvuln(seconds)); }
    IEnumerator CoInvuln(float s)
    {
        IsInvincible = true;
        var sr = CharacterRenderer ? CharacterRenderer : GetComponent<SpriteRenderer>();
        float t = 0.0f; bool vis = true;
        while (t < s)
        {
            t += 0.1f;
            if (sr) { vis = !vis; sr.enabled = vis; }
            yield return new WaitForSeconds(0.1f);
        }
        if (sr) sr.enabled = true;
        IsInvincible = false;
    }

    public void RunAfter(float seconds, System.Action action) { StartCoroutine(CoRunAfter(seconds, action)); }
    IEnumerator CoRunAfter(float s, System.Action a) { yield return new WaitForSeconds(s); a?.Invoke(); }

    public Vector2 GetMagicSpawnPoint()
    {
        var b = Col.bounds; return new Vector2(b.max.x, b.max.y);
    }

    void ApplyCharacterFromNetwork()
    {
        var runner = FusionManager.LocalRunner;
        if (runner == null || GameManager.Instance == null || GameDataManager.Instance == null) return;

        var pData = GameManager.Instance.GetPlayerData(runner.LocalPlayer, runner);
        if (pData == null) return;

        var cData = GameDataManager.Instance.CharacterService?.GetCharacter(pData.CharacterIndex);
        if (cData != null)
        {
            if (CharacterRenderer && cData.characterSprite) CharacterRenderer.sprite = cData.characterSprite;

            if (cData.stats)
            {
                MoveSpeed = cData.stats.moveSpeed;

                var hp = GetComponent<HealthComponent>();
                if (hp) hp.SetMaxHP(cData.stats.maxHP, true);

                if (GetComponent<HealthComponent>()?.InvulnOnHit != null)
                    GetComponent<HealthComponent>().InvulnOnHit.duration = cData.stats.invulnOnHitSeconds;

                spriteDefaultFacingLeft = cData.stats.spriteDefaultFacingLeft;
                flipColliderWithScale = cData.stats.flipColliderWithScale;
            }
        }
    }

    void OnAnyPlayerChangeCharacter(PlayerRef who, NetworkRunner runner, int idx)
    {
        if (runner != FusionManager.LocalRunner) return;
        if (who == runner.LocalPlayer) ApplyCharacterFromNetwork();
    }

    public void PlayAnim(string stateName) { if (animator) animator.Play(stateName); }

    public void SetFacingByX(float xDir)
    {
        bool wantLeft = xDir < 0.0f;
        float defaultSign = spriteDefaultFacingLeft ? -1.0f : 1.0f;

        if (flipColliderWithScale)
        {
            if (CharacterRenderer) CharacterRenderer.flipX = false;
            float dirSign = wantLeft ? -1.0f : 1.0f;
            float finalSign = dirSign * defaultSign;

            var s = transform.localScale;
            s.x = Mathf.Abs(_baseScale.x) * finalSign; // 콜라이더 포함 반전
            s.y = _baseScale.y;
            s.z = _baseScale.z;
            transform.localScale = s;
        }
        else
        {
            if (!CharacterRenderer) return;
            bool faceLeft = wantLeft;
            CharacterRenderer.flipX = spriteDefaultFacingLeft ? !faceLeft : faceLeft;
        }
    }

    public void FaceLeft(bool left) => SetFacingByX(left ? -1.0f : 1.0f);
}
