# 플레이어 스크립트 정리 방안

## ✅ 완료된 작업

### 1. 폴더 구조 생성 및 파일 이동
- ✅ Core/ 폴더: PlayerController.cs, PlayerInputData.cs
- ✅ State/ 폴더: PlayerState.cs, PlayerBehavior.cs
- ✅ Movement/ 폴더: PlayerRigidBodyMovement.cs
- ✅ Magic/ 폴더: PlayerMagicController.cs, MagicAnchorCollision.cs
- ✅ Visual/ 폴더: PlayerViewManager.cs, PlayerAnimationController.cs, PlayerBarrierVisual.cs
- ✅ Detection/ 폴더: PlayerDetectionManager.cs, PlayerDetectionTrigger.cs
- ✅ Camera/ 폴더: MainCameraController.cs

### 2. 안 쓰는 로직 정리
- ✅ PlayerBehavior.cs에서 사용되지 않는 이벤트 제거:
  - `OnAttackPerformed` (구독자 없음)
  - `OnSkillUsed` (구독자 없음)
  - `OnInteracted` (구독자 없음)
- ✅ PlayerBehavior.cs에서 사용되지 않는 메서드 제거:
  - `PerformAttack()` (호출자 없음)
  - `UseSkill()` (호출자 없음)
  - `Interact()` (호출자 없음)
- ✅ 불필요한 using 문 정리
- ✅ Debug.Log 제거 (초기화 로그)

## 현재 상황
- 총 13개의 플레이어 관련 스크립트가 `Assets/Scripts/Player/` 폴더에 평면적으로 존재
- 네임스페이스 없이 모든 클래스가 전역 네임스페이스에 존재
- 기능별로 명확한 구분이 어려움

## 옵션 1: 기능별 하위 폴더 구조 (권장) ⭐

### 구조
```
Assets/Scripts/Player/
├── Core/
│   ├── PlayerController.cs          (메인 컨트롤러)
│   └── PlayerInputData.cs           (입력 데이터 구조)
│
├── State/
│   ├── PlayerState.cs                  (상태 관리: HP, 사망, 리스폰)
│   └── PlayerBehavior.cs              (게임플레이 로직: 공격, 스킬, 상호작용)
│
├── Movement/
│   └── PlayerRigidBodyMovement.cs   (이동 처리)
│
├── Magic/
│   ├── PlayerMagicController.cs      (마법 시스템)
│   └── MagicAnchorCollision.cs       (마법 앵커 충돌)
│
├── Visual/
│   ├── PlayerViewManager.cs          (뷰 오브젝트 관리)
│   ├── PlayerAnimationController.cs  (애니메이션)
│   └── PlayerBarrierVisual.cs        (보호막 시각 효과)
│
├── Detection/
│   ├── PlayerDetectionManager.cs     (적 감지 관리)
│   └── PlayerDetectionTrigger.cs     (적 감지 트리거)
│
└── Camera/
    └── MainCameraController.cs       (카메라 제어)
```

### 장점
- ✅ 기능별로 명확하게 분리되어 찾기 쉬움
- ✅ 유지보수성 향상 (관련 스크립트가 한 곳에)
- ✅ 확장성 좋음 (새 기능 추가 시 폴더만 추가)
- ✅ Unity 프로젝트 구조와 일치
- ✅ 기존 코드 변경 최소화 (폴더 이동만)

### 단점
- ⚠️ 폴더 구조가 복잡해질 수 있음
- ⚠️ 일부 스크립트는 여러 카테고리에 속할 수 있음

---

## 옵션 2: 네임스페이스 기반 구조

### 구조
모든 스크립트는 `Assets/Scripts/Player/`에 유지하되 네임스페이스로 구분:

```csharp
namespace Player.Core { ... }
namespace Player.State { ... }
namespace Player.Movement { ... }
namespace Player.Magic { ... }
namespace Player.Visual { ... }
namespace Player.Detection { ... }
namespace Player.Camera { ... }
```

### 장점
- ✅ 폴더 구조 단순 유지
- ✅ 네임스페이스로 논리적 그룹화
- ✅ 타입 충돌 방지

### 단점
- ⚠️ Unity에서는 폴더 구조가 더 직관적
- ⚠️ 모든 파일에 네임스페이스 추가 필요
- ⚠️ using 문 추가 필요

---

## 옵션 3: 통합 구조 (비권장)

### 구조
관련 기능을 하나의 스크립트로 통합:
- `PlayerCore.cs` (Controller + Input)
- `PlayerStateSystem.cs` (State + Behavior)
- `PlayerMagicSystem.cs` (MagicController + MagicAnchorCollision)
- `PlayerVisualSystem.cs` (ViewManager + Animation + BarrierVisual)
- `PlayerDetectionSystem.cs` (DetectionManager + DetectionTrigger)
- `PlayerMovement.cs` (기존 유지)
- `MainCameraController.cs` (기존 유지)

### 장점
- ✅ 스크립트 수 감소

### 단점
- ❌ 단일 책임 원칙 위반
- ❌ 코드 가독성 저하
- ❌ 테스트 어려움
- ❌ 재사용성 저하
- ❌ 대규모 리팩토링 필요

---

## 옵션 4: 하이브리드 구조 (폴더 + 네임스페이스)

### 구조
옵션 1의 폴더 구조 + 옵션 2의 네임스페이스

### 장점
- ✅ 폴더 구조의 직관성
- ✅ 네임스페이스의 논리적 그룹화
- ✅ 최고의 확장성

### 단점
- ⚠️ 약간의 복잡도 증가
- ⚠️ 모든 파일 수정 필요

---

## 추천: 옵션 1 (기능별 하위 폴더 구조)

### 이유
1. **Unity 프로젝트 관례**: 폴더 구조가 가장 직관적
2. **최소 변경**: 파일 이동만으로 가능
3. **확장성**: 새 기능 추가 시 새 폴더만 생성
4. **유지보수성**: 관련 스크립트를 쉽게 찾을 수 있음

### 마이그레이션 계획
1. 폴더 생성 (Core, State, Movement, Magic, Visual, Detection, Camera)
2. 파일 이동 (Unity가 자동으로 .meta 파일 업데이트)
3. 참조 확인 (다른 스크립트에서 참조하는 부분 확인)
4. 테스트 (프리팹 및 씬에서 참조 확인)

---

## 추가 개선 사항

### 1. 공통 인터페이스 도입
```csharp
// IPlayerComponent.cs
public interface IPlayerComponent
{
    void Initialize(PlayerController controller);
}
```

### 2. 이벤트 시스템 통합
현재 각 컴포넌트가 개별 이벤트를 가지고 있음 → 중앙화된 이벤트 버스 고려

### 3. 의존성 주입 패턴
현재는 GetComponent로 의존성 해결 → 초기화 시 주입으로 변경 고려
