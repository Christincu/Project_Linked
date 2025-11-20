# 플레이어 코드 리팩토링 계획

## 현재 문제점

### 1. PlayerController.cs (907줄) - 너무 많은 책임
- 네트워크 동기화
- 초기화 로직
- 애니메이션 관리
- 보호막 시각 효과
- 적 감시 범위 트리거
- ViewObj 관리
- RPC 메서드들

### 2. PlayerMagicController.cs (781줄) - 마법 시스템 전체
- 마법 UI 관리
- 마법 시전
- 보호막 선택 모드
- 마법 흡수 로직
- 입력 처리

## 리팩토링 제안

### Phase 1: PlayerController 분리 (우선순위 높음)

#### 1.1 PlayerAnimationController.cs (새 파일)
**분리할 내용:**
- `UpdateAnimation()` 메서드
- `PlayAnimation()` 메서드
- `UpdateScale()` 메서드
- `AnimationState`, `ScaleX` 관련 로직
- `_animator`, `_lastAnimationState`, `_previousPosition` 필드

**예상 줄 수:** ~100줄

#### 1.2 PlayerBarrierVisual.cs (새 파일)
**분리할 내용:**
- `UpdateBarrierVisual()` 메서드
- `CreateBarrierVisual()` 메서드
- `DestroyBarrierVisual()` 메서드
- `CreateCircleTexture()` 메서드
- `_barrierVisualObject`, `_previousHasBarrier` 필드
- `HasBarrier` 변경 감지 로직

**예상 줄 수:** ~120줄

#### 1.3 PlayerViewManager.cs (새 파일)
**분리할 내용:**
- `TryCreateView()` 메서드
- `EnsureViewObjParentExists()` 메서드
- `_viewObj` 관리
- ViewObj 생성 및 초기화 로직

**예상 줄 수:** ~80줄

#### 1.4 PlayerDetectionManager.cs (새 파일)
**분리할 내용:**
- `InitializeDetectionTrigger()` 메서드
- `SetDetectionTriggerRange()` 메서드
- `UpdateDetectionTriggerRange()` 메서드
- `IsEnemyNearby()`, `OnEnemyEnter()`, `OnEnemyExit()` 메서드
- `_nearbyEnemies`, `_detectionTriggerObj` 등 필드

**예상 줄 수:** ~100줄

### Phase 2: PlayerMagicController 분리 (우선순위 중간)

#### 2.1 PlayerMagicUI.cs (새 파일)
**분리할 내용:**
- `UpdateMagicUIState()` 메서드
- `SetMagicUIActive()` 메서드
- `UpdateMagicUiSprite()` 메서드
- `UpdateAnchorPosition()` 메서드
- `CalculateAnchorPosition()` 메서드
- Magic UI 관련 필드들 (`_magicViewObj`, `_magicAnchor`, 등)

**예상 줄 수:** ~200줄

#### 2.2 PlayerBarrierSelector.cs (새 파일)
**분리할 내용:**
- `IsInBarrierSelectionMode()` 메서드
- `UpdateBarrierSelectionWithInput()` 메서드
- `FindClosestPlayerForBarrier()` 메서드
- `UpdateBarrierHighlightVisuals()` 메서드
- `AddBarrierHighlight()` 메서드
- `RemoveBarrierHighlight()` 메서드
- `ApplyBarrierToPlayer()` 메서드
- 보호막 관련 필드들

**예상 줄 수:** ~200줄

#### 2.3 PlayerMagicAbsorption.cs (새 파일)
**분리할 내용:**
- `OnPlayerCollisionEnter()` 메서드
- `DetermineAbsorber()` 메서드
- `OnAbsorbed()` 메서드
- 마법 흡수 관련 로직

**예상 줄 수:** ~80줄

### Phase 3: 네트워크 상태 관리 개선 (우선순위 낮음)

#### 3.1 PlayerNetworkStateManager.cs (새 파일)
**분리할 내용:**
- `DetectNetworkChanges()` 메서드
- 네트워크 상태 변경 감지 로직
- 각 컴포넌트에 변경 알림 전달

**예상 줄 수:** ~100줄

## 리팩토링 후 예상 구조

```
PlayerController.cs (~400줄)
├── 네트워크 변수 정의
├── 컴포넌트 참조
├── 초기화 (다른 컴포넌트 초기화 호출)
├── RPC 메서드들
└── FixedUpdateNetwork (다른 컴포넌트 호출)

PlayerAnimationController.cs (~100줄)
├── 애니메이션 상태 관리
└── 스케일 관리

PlayerBarrierVisual.cs (~120줄)
├── 보호막 시각 효과 생성/제거
└── 텍스처 생성

PlayerViewManager.cs (~80줄)
├── ViewObj 생성 및 관리
└── ViewObjParent 관리

PlayerDetectionManager.cs (~100줄)
├── 적 감시 범위 트리거 관리
└── 적 감지 로직

PlayerMagicController.cs (~300줄)
├── 마법 시전 로직
├── 입력 처리
└── 컴포넌트 조율

PlayerMagicUI.cs (~200줄)
├── 마법 UI 표시/숨김
└── 스프라이트 업데이트

PlayerBarrierSelector.cs (~200줄)
├── 보호막 선택 모드
└── 하이라이트 관리

PlayerMagicAbsorption.cs (~80줄)
└── 마법 흡수 로직
```

## 구현 순서

1. **Phase 1.1**: PlayerAnimationController 분리 (가장 간단)
2. **Phase 1.2**: PlayerBarrierVisual 분리
3. **Phase 1.3**: PlayerViewManager 분리
4. **Phase 1.4**: PlayerDetectionManager 분리
5. **Phase 2.1**: PlayerMagicUI 분리
6. **Phase 2.2**: PlayerBarrierSelector 분리
7. **Phase 2.3**: PlayerMagicAbsorption 분리
8. **Phase 3.1**: PlayerNetworkStateManager 분리 (선택사항)

## 주의사항

1. **네트워크 변수는 PlayerController에 유지**: Fusion의 NetworkBehaviour 특성상 네트워크 변수는 PlayerController에 있어야 함
2. **점진적 리팩토링**: 한 번에 하나씩 분리하여 테스트
3. **의존성 관리**: 각 컴포넌트는 PlayerController 참조를 통해 필요한 데이터 접근
4. **초기화 순서**: PlayerController의 `InitializeComponents()`에서 모든 컴포넌트 초기화

