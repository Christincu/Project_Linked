using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// [DEPRECATED] 보호막 마법 오브젝트 (Air + Soil 조합)
/// 
/// 이 클래스는 더 이상 사용되지 않습니다.
/// 타겟팅 로직은 BarrierMagicHandler에서 로컬로 처리하고,
/// 보호막 적용은 PlayerController의 Networked 속성(HasBarrier, BarrierTimer)을 통해 처리합니다.
/// 
/// 네트워크 최적화를 위해 NetworkObject 스폰 없이 RPC만 사용하는 방식으로 변경되었습니다.
/// 
/// 제거 예정: 이 파일은 향후 제거될 수 있습니다.
/// </summary>
public class BarrierMagic : NetworkBehaviour
{
    #region Serialized Fields
    [Header("Visual Settings")]
    [SerializeField] private Color highlightColor = new Color(0.2f, 1f, 0.2f, 1f); // 연두색
    [SerializeField] private float highlightThickness = 0.1f;
    [SerializeField] private float detectionRadius = 2f;
    #endregion

    #region Networked Properties
    [Networked] public PlayerController Owner { get; set; }
    [Networked] public PlayerController SelectedPlayer { get; set; }
    [Networked] public int MagicCode { get; set; }
    [Networked] public Vector3 Position { get; set; }
    #endregion

    #region Private Fields
    private GameDataManager _gameDataManager;
    private Dictionary<PlayerController, GameObject> _highlightObjects = new Dictionary<PlayerController, GameObject>();
    private PlayerController _previousSelectedPlayer;
    private NetworkButtons _previousMouseButtons; // 이전 프레임의 마우스 버튼 상태
    #endregion

    #region Unity Callbacks
    public override void Spawned()
    {
        if (Runner == null) return;
        
        // GameDataManager 찾기
        _gameDataManager = FindObjectOfType<GameDataManager>();
        if (_gameDataManager == null)
        {
            Debug.LogError("[BarrierMagicObject] GameDataManager not found!");
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        if (Owner == null) return;

        // 보호막 오브젝트를 Owner 플레이어 위치로 이동
        if (Owner != null && !Owner.IsDead)
        {
            Position = Owner.transform.position;
            transform.position = Position;
        }

        // 선택된 플레이어 업데이트
        UpdateSelectedPlayer();

        // 클릭 입력 처리 (Owner의 Input Authority에서만)
        if (Owner != null && Owner.Object.HasInputAuthority)
        {
            // Owner의 입력 데이터 가져오기
            if (Owner.GetInput<InputData>(out var inputData))
            {
                ProcessInput(inputData);
            }
        }
    }

    /// <summary>
    /// 입력을 처리합니다.
    /// </summary>
    private void ProcessInput(InputData inputData)
    {
        // 마우스 클릭 감지 (이전 프레임과 비교하여 눌림 감지)
        NetworkButtons pressedButtons = inputData.GetMouseButtonPressed(_previousMouseButtons);
        bool leftClickDown = pressedButtons.IsSet(InputMouseButton.LEFT);
        bool rightClickDown = pressedButtons.IsSet(InputMouseButton.RIGHT);
        
        // 좌클릭 또는 우클릭 모두 보호막 적용 가능
        if (leftClickDown || rightClickDown)
        {
            // 선택된 플레이어가 있으면 (자신 포함) 보호막 적용
            if (SelectedPlayer != null && !SelectedPlayer.IsDead)
            {
                // 선택된 플레이어에게 보호막 적용
                RPC_ApplyBarrier(SelectedPlayer);
            }
        }
        
        _previousMouseButtons = inputData.MouseButtons;
    }

    /// <summary>
    /// 보호막 적용 RPC
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ApplyBarrier(PlayerController targetPlayer)
    {
        ApplyBarrier(targetPlayer);
    }

    public override void Render()
    {
        // 위치 동기화 (모든 클라이언트에서)
        if (Position != transform.position)
        {
            transform.position = Position;
        }
        
        // 하이라이트 렌더링 (모든 클라이언트에서)
        // SelectedPlayer는 Networked이므로 모든 클라이언트에서 동기화됨
        UpdateHighlightVisuals();
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// 보호막 마법 오브젝트 초기화
    /// </summary>
    public void Initialize(PlayerController owner, int magicCode)
    {
        if (Object.HasStateAuthority)
        {
            Owner = owner;
            MagicCode = magicCode;
            
            // 초기 위치 설정
            if (Owner != null)
            {
                Position = Owner.transform.position;
                transform.position = Position;
            }
        }
    }

    /// <summary>
    /// 플레이어 클릭 시 보호막 적용
    /// </summary>
    public void ApplyBarrier(PlayerController targetPlayer)
    {
        if (!Object.HasStateAuthority) return;
        if (targetPlayer == null) return;

        // 보호막 적용
        ApplyBarrierToPlayer(targetPlayer);
        
        // Owner의 마법 비활성화 (State Authority에서 실행 중이므로 내부 메서드 사용)
        if (Owner != null)
        {
            Owner.DeactivateMagicInternal();
        }
        
        // 오브젝트 제거
        Runner.Despawn(Object);
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// 마우스 위치에 있는 플레이어를 선택합니다. (자신 포함)
    /// </summary>
    private void UpdateSelectedPlayer()
    {
        if (Owner == null) return;
        
        // Input Authority 체크는 제거 - State Authority에서도 업데이트 가능하도록
        // 단, 마우스 위치는 Owner의 입력에서만 가져올 수 있으므로 조건부로 처리
        Vector3 mouseWorldPos = Vector3.zero;
        
        if (Owner.Object.HasInputAuthority)
        {
            // Owner의 입력 데이터에서 마우스 위치 가져오기
            mouseWorldPos = GetMouseWorldPosition();
        }
        else
        {
            // Input Authority가 없으면 업데이트하지 않음 (다른 클라이언트에서는 SelectedPlayer가 네트워크로 동기화됨)
            return;
        }
        
        // 가장 가까운 플레이어 찾기 (자신 포함)
        PlayerController closestPlayer = FindClosestPlayer(mouseWorldPos);
        
        if (closestPlayer != null && !closestPlayer.IsDead)
        {
            SelectedPlayer = closestPlayer;
        }
        else
        {
            SelectedPlayer = null;
        }
    }

    /// <summary>
    /// 마우스 월드 위치를 가져옵니다.
    /// </summary>
    private Vector3 GetMouseWorldPosition()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return Vector3.zero;

        // Owner의 입력 데이터에서 마우스 위치 가져오기
        if (Owner != null && Owner.GetInput<InputData>(out var inputData))
        {
            Vector3 mouseScreenPos = inputData.MousePosition;
            Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(mouseScreenPos);
            mouseWorldPos.z = 0;
            return mouseWorldPos;
        }

        // 폴백: Unity Input 사용
        Vector3 mouseScreenPosFallback = Input.mousePosition;
        Vector3 mouseWorldPosFallback = mainCamera.ScreenToWorldPoint(mouseScreenPosFallback);
        mouseWorldPosFallback.z = 0;
        return mouseWorldPosFallback;
    }

    /// <summary>
    /// 마우스 위치에 있는 플레이어를 찾습니다. (레이캐스트 사용, 자신 포함)
    /// </summary>
    private PlayerController FindClosestPlayer(Vector3 mouseWorldPos)
    {
        List<PlayerController> allPlayers = new List<PlayerController>();
        
        if (MainGameManager.Instance != null)
        {
            allPlayers = MainGameManager.Instance.GetAllPlayers();
        }
        
        if (allPlayers == null || allPlayers.Count == 0)
        {
            allPlayers = new List<PlayerController>(FindObjectsOfType<PlayerController>());
        }

        // 2D 게임이므로 OverlapPoint나 거리 기반으로 찾기
        // 마우스 위치에서 가장 가까운 플레이어 찾기 (거리 제한 없음)
        PlayerController closestPlayer = null;
        float closestDistance = float.MaxValue;

        foreach (var player in allPlayers)
        {
            if (player == null || player.IsDead) continue;

            // 플레이어의 ViewObj나 Collider 위치 확인
            Vector3 playerPos = player.transform.position;
            
            // ViewObj가 있으면 ViewObj 위치 사용
            if (player.ViewObj != null)
            {
                playerPos = player.ViewObj.transform.position;
            }
            
            // 마우스 위치와 플레이어 위치의 거리 계산
            float distance = Vector3.Distance(mouseWorldPos, playerPos);
            
            // 가장 가까운 플레이어 선택
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPlayer = player;
            }
        }

        return closestPlayer;
    }

    /// <summary>
    /// 하이라이트 시각 효과를 업데이트합니다.
    /// </summary>
    private void UpdateHighlightVisuals()
    {
        // SelectedPlayer가 null이 아니고 변경되었을 때만 업데이트
        if (SelectedPlayer != _previousSelectedPlayer)
        {
            // 이전 선택된 플레이어 하이라이트 제거
            if (_previousSelectedPlayer != null)
            {
                RemoveHighlight(_previousSelectedPlayer);
            }

            // 현재 선택된 플레이어 하이라이트 추가
            if (SelectedPlayer != null)
            {
                AddHighlight(SelectedPlayer);
            }
        }
        
        // SelectedPlayer가 null이 되었을 때도 하이라이트 제거
        if (SelectedPlayer == null && _previousSelectedPlayer != null)
        {
            RemoveHighlight(_previousSelectedPlayer);
        }

        _previousSelectedPlayer = SelectedPlayer;
    }

    /// <summary>
    /// 플레이어에게 하이라이트를 추가합니다.
    /// </summary>
    private void AddHighlight(PlayerController player)
    {
        if (player == null) return;
        
        // 이미 하이라이트가 있으면 추가하지 않음
        if (_highlightObjects.ContainsKey(player)) return;
        
        // ViewObj 찾기 (ViewObjParent 또는 직접)
        GameObject viewObj = player.ViewObj;
        if (viewObj == null)
        {
            // ViewObjParent에서 찾기
            Transform viewObjParent = player.transform.Find("ViewObjParent");
            if (viewObjParent != null && viewObjParent.childCount > 0)
            {
                viewObj = viewObjParent.GetChild(0).gameObject;
            }
        }
        
        if (viewObj == null)
        {
            Debug.LogWarning($"[BarrierMagic] ViewObj not found for player {player.name}");
            return;
        }

        // 하이라이트 오브젝트 생성
        GameObject highlightObj = new GameObject("BarrierHighlight");
        highlightObj.transform.SetParent(viewObj.transform, false);
        
        // SpriteRenderer 추가
        SpriteRenderer playerRenderer = viewObj.GetComponent<SpriteRenderer>();
        if (playerRenderer != null && playerRenderer.sprite != null)
        {
            SpriteRenderer highlightRenderer = highlightObj.AddComponent<SpriteRenderer>();
            highlightRenderer.sprite = playerRenderer.sprite;
            highlightRenderer.color = highlightColor;
            highlightRenderer.sortingOrder = playerRenderer.sortingOrder + 1;
            
            // 약간 크게 만들기 (테두리 효과)
            highlightObj.transform.localScale = Vector3.one * (1f + highlightThickness);
            
            Debug.Log($"[BarrierMagic] Added highlight to {player.name}");
        }
        else
        {
            Debug.LogWarning($"[BarrierMagic] PlayerRenderer or sprite not found for {player.name}");
        }

        _highlightObjects[player] = highlightObj;
    }

    /// <summary>
    /// 플레이어의 하이라이트를 제거합니다.
    /// </summary>
    private void RemoveHighlight(PlayerController player)
    {
        if (player == null || !_highlightObjects.ContainsKey(player)) return;

        GameObject highlightObj = _highlightObjects[player];
        if (highlightObj != null)
        {
            Destroy(highlightObj);
        }

        _highlightObjects.Remove(player);
    }

    /// <summary>
    /// 플레이어에게 보호막을 적용합니다.
    /// </summary>
    private void ApplyBarrierToPlayer(PlayerController targetPlayer)
    {
        if (targetPlayer == null) return;
        if (!Object.HasStateAuthority) return;
        
        // 베리어 조합 데이터 가져오기
        BarrierMagicCombinationData barrierData = GetBarrierData();
        if (barrierData == null)
        {
            Debug.LogError("[BarrierMagic] BarrierMagicCombinationData not found!");
            return;
        }

        // 모든 플레이어 가져오기
        List<PlayerController> allPlayers = new List<PlayerController>();
        
        if (MainGameManager.Instance != null)
        {
            allPlayers = MainGameManager.Instance.GetAllPlayers();
        }
        
        if (allPlayers == null || allPlayers.Count == 0)
        {
            allPlayers = new List<PlayerController>(FindObjectsOfType<PlayerController>());
        }

        // 보호막을 받은 플레이어와 받지 못한 플레이어의 체력 설정
        // 보호막 오브젝트의 State Authority를 사용하여 모든 플레이어의 상태를 변경
        foreach (var player in allPlayers)
        {
            if (player == null || player.IsDead) continue;

            if (player == targetPlayer)
            {
                // 보호막 받은 플레이어: 체력 및 보호막 타이머 설정
                player.CurrentHealth = barrierData.barrierReceiverHealth;
                if (player.CurrentHealth > player.MaxHealth)
                {
                    player.MaxHealth = player.CurrentHealth;
                }
                // 자폭 베리어 타이머 시작
                player.BarrierTimer = TickTimer.CreateFromSeconds(Runner, barrierData.barrierDuration);
                player.HasBarrier = true;
                // 위협점수와 이동속도 효과는 PlayerController의 FixedUpdateNetwork에서 자동 업데이트됨
                Debug.Log($"[BarrierMagic] {targetPlayer.name} received self-destruct barrier (HP: {barrierData.barrierReceiverHealth}, Duration: {barrierData.barrierDuration}s, ThreatScore: {barrierData.threatScore})");
            }
            else
            {
                // 보호막 받지 못한 플레이어: 체력 설정
                player.CurrentHealth = barrierData.nonReceiverHealth;
                if (player.CurrentHealth > player.MaxHealth)
                {
                    player.MaxHealth = player.CurrentHealth;
                }
                // 기존 보호막 제거
                player.BarrierTimer = TickTimer.None;
                player.HasBarrier = false;
                // 위협점수는 PlayerController의 FixedUpdateNetwork에서 자동 업데이트됨
                Debug.Log($"[BarrierMagic] {player.name} did not receive barrier (HP: {barrierData.nonReceiverHealth}, ThreatScore: {player.ThreatScore})");
            }
        }
    }
    
    /// <summary>
    /// 베리어 조합 데이터를 가져옵니다.
    /// </summary>
    private BarrierMagicCombinationData GetBarrierData()
    {
        if (_gameDataManager == null || _gameDataManager.MagicService == null) return null;
        
        // MagicCode로 조합 데이터 찾기 (결과 마법 코드)
        MagicCombinationData combinationData = _gameDataManager.MagicService.GetCombinationDataByResult(MagicCode);
        
        // BarrierMagicCombinationData로 캐스팅
        if (combinationData is BarrierMagicCombinationData barrierData)
        {
            return barrierData;
        }
        
        return null;
    }
    #endregion

    #region Cleanup
    private void OnDestroy()
    {
        // 모든 하이라이트 제거
        foreach (var kvp in _highlightObjects)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value);
            }
        }
        _highlightObjects.Clear();
    }
    #endregion
}

