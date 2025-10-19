using UnityEngine;
using Fusion;

public enum HoldMode { None, Left, Right, Combined }
public enum SkillButton { Left, Right }

[RequireComponent(typeof(Rigidbody2D), typeof(PlayerBehaviour))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float acceleration = 20f;
    public float maxSpeed = 5f;
    public float deceleration = 15f;
    [Range(0.1f, 1.0f)] public float holdMoveSpeedMultiplier = 0.5f;

    [Header("Skill")]
    public SkillSO skillAsset;

    [Header("Rendering")]
    public Animator animator;
    public SpriteRenderer spriteRenderer;

    [Header("Facing")]
    public bool spriteDefaultFacingLeft = false;
    public bool flipColliderWithScale = true;

    [Header("Hold Visual")]
    public GameObject holdVfxPrefab;
    public Transform holdVfxAnchor;

    [HideInInspector] public Rigidbody2D rb;
    [HideInInspector] public PolygonCollider2D col;

    private PlayerBehaviour _behaviour;
    private string _currentState = "Idle";
    private int _currentAnimHash = 0;
    private Vector3 _baseScale;
    private GameObject _holdVfxInstance;

    // Hold/Fire state
    public bool IsHolding { get; private set; }
    public HoldMode CurrentHoldMode { get; private set; } = HoldMode.None;
    private SkillButton _primaryBtn = SkillButton.Left;
    private SkillButton _secondaryBtn = SkillButton.Right;

    // Input cache
    private Vector2 _moveInput;
    private bool _lmbDown, _rmbDown, _lmbUp, _rmbUp;
    private bool _interactPressed;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.drag = 0f;
        rb.angularDrag = 0f;
        
        col = GetComponent<PolygonCollider2D>();
        _behaviour = GetComponent<PlayerBehaviour>();
        if (!spriteRenderer) spriteRenderer = GetComponent<SpriteRenderer>();
        if (!animator) animator = GetComponent<Animator>();
        _baseScale = transform.localScale;
    }

    void OnEnable() => FusionManager.OnPlayerChangeCharacterEvent += OnAnyPlayerChangeCharacter;
    void OnDisable() => FusionManager.OnPlayerChangeCharacterEvent -= OnAnyPlayerChangeCharacter;

    void Start()
    {
        ApplyCharacterFromNetwork();
    }

    void Update()
    {
        CacheInput();
        UpdateState();
    }

    void FixedUpdate()
    {
        FixedUpdateState();
    }

    void CacheInput()
    {
        // 이동 입력: Unity Input Manager의 Horizontal/Vertical 축 사용
        // (기본: WASD, 화살표 키)
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        _moveInput = new Vector2(h, v);
        if (_moveInput.sqrMagnitude > 1f) _moveInput.Normalize();

        // 스킬 입력: 마우스 좌/우 버튼
        _lmbDown = Input.GetMouseButtonDown(0);
        _rmbDown = Input.GetMouseButtonDown(1);
        _lmbUp = Input.GetMouseButtonUp(0);
        _rmbUp = Input.GetMouseButtonUp(1);
        
        // 상호작용 입력: F 또는 E 키
        _interactPressed = Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.E);
    }

    void UpdateState()
    {
        switch (_currentState)
        {
            case "Idle": UpdateIdle(); break;
            case "Move": UpdateMove(); break;
            case "Hold": UpdateHold(); break;
            case "Faint": break; // No update in faint
        }
    }

    void FixedUpdateState()
    {
        switch (_currentState)
        {
            case "Move": 
                ApplyMovement(maxSpeed);
                break;
            case "Hold": 
                ApplyMovement(maxSpeed * holdMoveSpeedMultiplier);
                break;
            case "Idle":
                ApplyDeceleration();
                break;
            case "Faint":
                ApplyDeceleration();
                break;
        }
    }

    // ===== STATES =====
    void UpdateIdle()
    {
        if (_moveInput.sqrMagnitude > 0f) { GotoState("Move"); return; }
        if (_lmbDown) { OnButtonDown(SkillButton.Left); GotoState("Hold"); return; }
        if (_rmbDown) { OnButtonDown(SkillButton.Right); GotoState("Hold"); return; }
        if (_interactPressed) TryInteract();
    }

    void UpdateMove()
    {
        if (_moveInput == Vector2.zero && rb.velocity.sqrMagnitude < 0.1f) 
        { 
            GotoState("Idle"); 
            return; 
        }
        
        UpdateMoveAnimation();
        if (_lmbDown) { OnButtonDown(SkillButton.Left); GotoState("Hold"); return; }
        if (_rmbDown) { OnButtonDown(SkillButton.Right); GotoState("Hold"); return; }
    }

    void UpdateHold()
    {
        if (_moveInput == Vector2.zero)
            PlayAnim("1P_Idle");
        else
            UpdateMoveAnimation();

        if (_lmbDown) OnButtonDown(SkillButton.Left);
        if (_rmbDown) OnButtonDown(SkillButton.Right);
        if (_lmbUp) { OnButtonUp(SkillButton.Left); return; }
        if (_rmbUp) { OnButtonUp(SkillButton.Right); return; }
    }

    void ApplyMovement(float targetMaxSpeed)
    {
        if (_moveInput != Vector2.zero)
        {
            // 가속
            Vector2 targetVelocity = _moveInput * targetMaxSpeed;
            Vector2 velocityChange = targetVelocity - rb.velocity;
            velocityChange = Vector2.ClampMagnitude(velocityChange, acceleration * Time.fixedDeltaTime);
            rb.velocity += velocityChange;
        }
        else
        {
            ApplyDeceleration();
        }
    }

    void ApplyDeceleration()
    {
        if (rb.velocity.sqrMagnitude > 0.01f)
        {
            // 감속
            float decelerationAmount = deceleration * Time.fixedDeltaTime;
            Vector2 decelerationForce = -rb.velocity.normalized * decelerationAmount;
            
            if (decelerationForce.sqrMagnitude >= rb.velocity.sqrMagnitude)
                rb.velocity = Vector2.zero;
            else
                rb.velocity += decelerationForce;
        }
        else
        {
            rb.velocity = Vector2.zero;
        }
    }

    void UpdateMoveAnimation()
    {
        // 입력 방향 기준으로 애니메이션 (부드러운 가속 중에도 즉각 반응)
        Vector2 direction = _moveInput.sqrMagnitude > 0.01f ? _moveInput : rb.velocity;
        
        if (direction.sqrMagnitude < 0.01f)
        {
            PlayAnim("1P_Idle");
            return;
        }

        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
        {
            PlayAnim("1P_leftrightMove");
            SetFacingByX(direction.x);
        }
        else
        {
            if (direction.y >= 0f) PlayAnim("1P_upMove");
            else PlayAnim("1P_downMove");
            if (Mathf.Abs(direction.x) > 0.01f) SetFacingByX(direction.x);
        }
    }

    void TryInteract()
    {
        var hit = Physics2D.OverlapCircle(transform.position, 0.6f, LayerMask.GetMask("Interactable"));
        if (!hit) return;
        var it = hit.GetComponent<IInteractable>();
        it?.Interact(gameObject);
        if (it != null)
        {
            switch (it.CallState)
            {
                case InteractState.Click: PlayAnim("Click"); break;
                case InteractState.Grib: PlayAnim("Grib"); break;
                case InteractState.Push: PlayAnim("Push"); break;
            }
            Invoke(nameof(ReturnToIdle), 0.3f);
        }
    }

    void ReturnToIdle() => GotoState("Idle");

    // ===== STATE MACHINE =====
    public void GotoState(string state)
    {
        // Exit
        switch (_currentState)
        {
            case "Hold":
                HideHoldVfx();
                IsHolding = false;
                CurrentHoldMode = HoldMode.None;
                break;
            case "Faint":
                break;
        }

        _currentState = state;

        // Enter
        switch (_currentState)
        {
            case "Idle": PlayAnim("1P_Idle"); break;
            case "Move": break;
            case "Hold": PlayAnim("1P_Idle"); break;
            case "Fire":
                PlayAnim("Fire");
                Invoke(nameof(ReturnToIdle), 0.3f);
                break;
            case "Faint":
                rb.velocity = Vector2.zero;
                PlayAnim("Faint");
                HideHoldVfx();
                break;
        }
    }

    // ===== ANIMATION =====
    public void PlayAnim(string stateName, bool restart = false)
    {
        if (!animator || animator.runtimeAnimatorController == null) return;

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

    // ===== SKILL SYSTEM =====
    public void OnButtonDown(SkillButton btn)
    {
        if (skillAsset == null) return;

        if (CurrentHoldMode == HoldMode.None)
        {
            ShowHoldVfx();
            skillAsset.OnHoldBegin(gameObject, btn);
            IsHolding = true;
            CurrentHoldMode = (btn == SkillButton.Left) ? HoldMode.Left : HoldMode.Right;
            _primaryBtn = btn;
        }
        else if (CurrentHoldMode == HoldMode.Left && btn == SkillButton.Right)
        {
            skillAsset.OnHoldBegin(gameObject, SkillButton.Right);
            CurrentHoldMode = HoldMode.Combined;
            _secondaryBtn = SkillButton.Right;
        }
        else if (CurrentHoldMode == HoldMode.Right && btn == SkillButton.Left)
        {
            skillAsset.OnHoldBegin(gameObject, SkillButton.Left);
            CurrentHoldMode = HoldMode.Combined;
            _secondaryBtn = SkillButton.Left;
        }
        else if (CurrentHoldMode == HoldMode.Combined)
        {
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
        skillAsset?.OnFire(gameObject);
        GameEvents.SkillFired?.Invoke();

        HideHoldVfx();
        IsHolding = false;
        CurrentHoldMode = HoldMode.None;

        Invoke(nameof(ReturnToIdle), 0.3f);
    }

    void ShowHoldVfx()
    {
        if (!holdVfxPrefab || _holdVfxInstance) return;
        var parent = holdVfxAnchor ? holdVfxAnchor : transform;
        _holdVfxInstance = Instantiate(holdVfxPrefab, parent);
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

    // ===== FACING =====
    public void SetFacingByX(float xDir)
    {
        bool wantLeft = xDir < 0f;
        float defaultSign = spriteDefaultFacingLeft ? -1.0f : 1.0f;

        if (flipColliderWithScale)
        {
            if (spriteRenderer) spriteRenderer.flipX = false;
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
            if (!spriteRenderer) return;
            bool faceLeft = wantLeft;
            spriteRenderer.flipX = spriteDefaultFacingLeft ? !faceLeft : faceLeft;
        }
    }

    // ===== NETWORK CHARACTER SYNC =====
    void ApplyCharacterFromNetwork()
    {
        var runner = FusionManager.LocalRunner;
        if (runner == null || GameManager.Instance == null || GameDataManager.Instance == null) return;

        var pData = GameManager.Instance.GetPlayerData(runner.LocalPlayer, runner);
        if (pData == null) return;

        var cData = GameDataManager.Instance.CharacterService?.GetCharacter(pData.CharacterIndex);
        if (cData != null)
        {
            if (spriteRenderer && cData.characterSprite)
                spriteRenderer.sprite = cData.characterSprite;

            if (cData.stats)
            {
                maxSpeed = cData.stats.moveSpeed;
                _behaviour?.SetMaxHP(cData.stats.maxHP, true);
                _behaviour?.SetInvulnDuration(cData.stats.invulnOnHitSeconds);
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

    public Vector2 GetMagicSpawnPoint()
    {
        var b = col.bounds;
        return new Vector2(b.max.x, b.max.y);
    }
}

