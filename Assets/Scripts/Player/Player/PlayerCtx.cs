using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public enum HoldMode { None, Left, Right, Combined }

[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody2D), typeof(PolygonCollider2D))]
public class PlayerCtx : MonoBehaviour, IInvulnerable
{
    [Header("Move")]
    public float MoveSpeed = 1.0f;
    [Range(0.1f, 1.0f)] public float HoldMoveSpeedMultiplier = 1.0f;

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

    private int _currentAnimHash = 0;

    public bool IsInvincible { get; private set; }
    public bool IsHolding { get; private set; }
    private GameObject _holdVfxInstance;

    private Vector3 _baseScale;

    public HoldMode CurrentHoldMode { get; private set; } = HoldMode.None;
    private SkillButton _primaryBtn = SkillButton.Left;
    private SkillButton _secondaryBtn = SkillButton.Right;

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

    void OnEnable() => FusionManager.OnPlayerChangeCharacterEvent += OnAnyPlayerChangeCharacter;
    void OnDisable() => FusionManager.OnPlayerChangeCharacterEvent -= OnAnyPlayerChangeCharacter;

    void Start()
    {
        Goto("Idle");
        ApplyCharacterFromNetwork();
    }

    void Update() => _cur?.Tick();
    void FixedUpdate() => _cur?.FixedTick();

    public void Register(string key, PlayerState s) { s.Bind(this); _states[key] = s; }

    public void Goto(string key)
    {
        if (!_states.ContainsKey(key))
        {
            Debug.LogWarning($"State not found: {key}");
            return;
        }
        _cur?.Exit();
        _cur = _states[key];
        _cur.Enter();
    }

    // 애니메이션 재생
    public void PlayAnim(string stateName, bool restart = false)
    {
        if (!animator) return;
        if (animator.runtimeAnimatorController == null) return;

        int hash = Animator.StringToHash(stateName);
        var info = animator.GetCurrentAnimatorStateInfo(0);

        if (!restart && (info.shortNameHash == hash || _currentAnimHash == hash))
            return;

        if (restart)
            animator.Play(hash, 0, 0.0f);     
        else
            animator.CrossFade(hash, 0.05f, 0, 0.0f); 

        _currentAnimHash = hash;
        if (animator.speed <= 0f) animator.speed = 1.0f;
    }

    // Hold / Fire 로직
    public void OnButtonDown(SkillButton btn)
    {
        if (SkillAsset == null) return;

        if (CurrentHoldMode == HoldMode.None)
        {
            ShowHoldVfx();
            SkillAsset.OnHoldBegin(gameObject, btn);
            IsHolding = true;
            CurrentHoldMode = (btn == SkillButton.Left) ? HoldMode.Left : HoldMode.Right;
            _primaryBtn = btn;
        }
        else if (CurrentHoldMode == HoldMode.Left && btn == SkillButton.Right)
        {
            SkillAsset.OnHoldBegin(gameObject, SkillButton.Right);
            CurrentHoldMode = HoldMode.Combined;
            _secondaryBtn = SkillButton.Right;
        }
        else if (CurrentHoldMode == HoldMode.Right && btn == SkillButton.Left)
        {
            SkillAsset.OnHoldBegin(gameObject, SkillButton.Left);
            CurrentHoldMode = HoldMode.Combined;
            _secondaryBtn = SkillButton.Left;
        }
        else if (CurrentHoldMode == HoldMode.Combined)
        {
            // 이미 합쳐진 상태에서 또 누르면 즉시 발사
            FireAndReturn();
        }
    }

    public void OnButtonUp(SkillButton btn)
    {
        if (!IsHolding) return;

        if (CurrentHoldMode == HoldMode.Combined)
        {
            FireAndReturn();
            return;
        }

        if ((CurrentHoldMode == HoldMode.Left && btn == SkillButton.Left) ||
            (CurrentHoldMode == HoldMode.Right && btn == SkillButton.Right))
        {
            FireAndReturn();
        }
    }

    private void FireAndReturn()
    {
        SkillAsset?.OnFire(gameObject);
        GameEvents.SkillFired?.Invoke();

        HideHoldVfx();
        IsHolding = false;
        CurrentHoldMode = HoldMode.None;

        RunAfter(0.3f, () => Goto("Idle"));
    }

    // 반짝반짝
    void ShowHoldVfx()
    {
        if (!HoldVfxPrefab || _holdVfxInstance) return;
        var parent = HoldVfxAnchor ? HoldVfxAnchor : transform;
        _holdVfxInstance = Instantiate(HoldVfxPrefab, parent);
        _holdVfxInstance.transform.localPosition = Vector3.zero;
        _holdVfxInstance.transform.localRotation = Quaternion.identity;
        _holdVfxInstance.transform.localScale = Vector3.one;
    }

    public void HideHoldVfx()
    {
        if (_holdVfxInstance)
        {
            Destroy(_holdVfxInstance);
            _holdVfxInstance = null;
        }
    }

    // 유틸리티
    public void GrantInvuln(float seconds)
    {
        if (gameObject.activeInHierarchy)
            StartCoroutine(CoInvuln(seconds));
    }

    IEnumerator CoInvuln(float s)
    {
        IsInvincible = true;
        var sr = CharacterRenderer ? CharacterRenderer : GetComponent<SpriteRenderer>();
        float t = 0f;
        bool vis = true;
        while (t < s)
        {
            t += 0.1f;
            if (sr)
            {
                vis = !vis;
                sr.enabled = vis;
            }
            yield return new WaitForSeconds(0.1f);
        }
        if (sr) sr.enabled = true;
        IsInvincible = false;
    }

    public void RunAfter(float seconds, System.Action action) => StartCoroutine(CoRunAfter(seconds, action));

    IEnumerator CoRunAfter(float s, System.Action a)
    {
        yield return new WaitForSeconds(s);
        a?.Invoke();
    }

    public Vector2 GetMagicSpawnPoint()
    {
        var b = Col.bounds;
        return new Vector2(b.max.x, b.max.y);
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
            if (CharacterRenderer && cData.characterSprite)
                CharacterRenderer.sprite = cData.characterSprite;

            if (cData.stats)
            {
                MoveSpeed = cData.stats.moveSpeed;

                var hp = GetComponent<HealthComponent>();
                if (hp)
                    hp.SetMaxHP(cData.stats.maxHP, true);

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

    // 방향 / 콜리전 반전
    public void SetFacingByX(float xDir)
    {
        bool wantLeft = xDir < 0f;
        float defaultSign = spriteDefaultFacingLeft ? -1.0f : 1.0f;

        if (flipColliderWithScale)
        {
            if (CharacterRenderer) CharacterRenderer.flipX = false;
            float dirSign = wantLeft ? -1.0f : 1.0f;
            float finalSign = dirSign * defaultSign;

            var s = transform.localScale;
            s.x = Mathf.Abs(_baseScale.x) * finalSign;
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
