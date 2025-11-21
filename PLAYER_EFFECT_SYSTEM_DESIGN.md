# 플레이어 효과 시스템 설계 방안

## 요구사항
- 이동속도 증가/감소 (퍼센테이지)
- 확장 가능한 효과 시스템
- 네트워크 동기화 (Fusion)
- 지속 시간 관리
- 효과 스택 관리

## 옵션 1: 간단한 효과 변수 방식 (비권장)

### 구조
```csharp
// PlayerController에 직접 변수 추가
[Networked] public float MoveSpeedMultiplier { get; set; } // 1.0 = 기본값, 1.5 = 50% 증가
```

### 장점
- ✅ 구현 간단
- ✅ 네트워크 동기화 쉬움

### 단점
- ❌ 효과 타입 추가 시 코드 수정 필요
- ❌ 여러 효과 동시 적용 어려움
- ❌ 효과 스택 관리 불가
- ❌ 확장성 낮음

---

## 옵션 2: 효과 매니저 컴포넌트 방식 (권장) ⭐

### 구조
```
Assets/Scripts/Player/
└── State/
    └── PlayerEffectManager.cs  (새로 생성)
```

### 효과 타입 정의
```csharp
public enum EffectType
{
    MoveSpeed,      // 이동속도
    AttackDamage,   // 공격력
    Defense,        // 방어력
    // ... 확장 가능
}
```

### 효과 데이터 구조
```csharp
[System.Serializable]
public struct PlayerEffect
{
    public EffectType type;
    public float value;           // 퍼센테이지 (1.0 = 100%, 1.5 = 150%)
    public float duration;        // 지속 시간 (초)
    public int stackCount;        // 스택 수
    public int effectId;          // 고유 ID (중복 방지용)
}
```

### 구현 방식
1. **PlayerEffectManager** 컴포넌트 생성
2. 효과 리스트 관리 (네트워크 동기화)
3. PlayerRigidBodyMovement에서 효과 적용
4. FixedUpdateNetwork에서 지속 시간 관리

### 장점
- ✅ 확장성 좋음 (새 효과 타입 추가 쉬움)
- ✅ 여러 효과 동시 적용 가능
- ✅ 효과 스택 관리 가능
- ✅ 효과별 지속 시간 관리
- ✅ 효과 제거/갱신 용이

### 단점
- ⚠️ 구현 복잡도 증가
- ⚠️ 네트워크 동기화 고려 필요

---

## 옵션 3: 효과 데이터 구조체 + 딕셔너리 방식

### 구조
```csharp
// PlayerController에
[Networked] public NetworkDictionary<int, PlayerEffectData> ActiveEffects { get; set; }
```

### 장점
- ✅ 가장 유연함
- ✅ 효과 ID 기반 관리

### 단점
- ❌ Fusion NetworkDictionary 복잡도
- ❌ 성능 오버헤드 가능

---

## 추천: 옵션 2 (효과 매니저 컴포넌트)

### 구현 계획

#### 1. 효과 타입 및 데이터 정의
```csharp
public enum EffectType
{
    MoveSpeed,      // 이동속도
    AttackDamage,   // 공격력
    Defense,        // 방어력
}

[System.Serializable]
public struct PlayerEffectData
{
    public EffectType type;
    public float value;        // 퍼센테이지 (0.5 = 50% 감소, 1.5 = 50% 증가)
    public float duration;    // 지속 시간 (초)
    public int stackCount;    // 스택 수 (같은 효과 중복 적용)
    public int effectId;      // 고유 ID
}
```

#### 2. PlayerEffectManager 컴포넌트
- 효과 추가/제거
- 효과 지속 시간 관리
- 효과 스택 관리
- 효과 값 계산 (여러 효과 합산)

#### 3. PlayerRigidBodyMovement 수정
```csharp
// GetMoveSpeed() 메서드 수정
public float GetMoveSpeed()
{
    float baseSpeed = _moveSpeed;
    float multiplier = _controller.EffectManager?.GetMoveSpeedMultiplier() ?? 1.0f;
    return baseSpeed * multiplier;
}
```

#### 4. 네트워크 동기화
- 효과 리스트는 서버에서만 관리
- 효과 값은 계산된 결과만 동기화 (성능 최적화)
- 또는 효과 리스트 자체를 네트워크 동기화

---

## 효과 적용 예시

### 이동속도 증가 50% (5초)
```csharp
playerController.EffectManager.AddEffect(
    EffectType.MoveSpeed, 
    value: 1.5f,      // 150% = 50% 증가
    duration: 5f
);
```

### 이동속도 감소 30% (3초)
```csharp
playerController.EffectManager.AddEffect(
    EffectType.MoveSpeed, 
    value: 0.7f,      // 70% = 30% 감소
    duration: 3f
);
```

### 효과 스택 (같은 효과 중복)
```csharp
// 첫 번째: +50%
playerController.EffectManager.AddEffect(EffectType.MoveSpeed, 1.5f, 5f);

// 두 번째: +30% (스택)
playerController.EffectManager.AddEffect(EffectType.MoveSpeed, 1.3f, 3f);

// 결과: 1.5 * 1.3 = 1.95 (95% 증가)
```

---

## 네트워크 동기화 전략

### 전략 1: 효과 리스트 동기화 (권장)
- `NetworkArray<PlayerEffectData>` 사용
- 모든 클라이언트에서 효과 리스트 동기화
- 클라이언트에서도 효과 계산 가능

### 전략 2: 계산 결과만 동기화
- 서버에서만 효과 계산
- `MoveSpeedMultiplier` 같은 계산 결과만 네트워크 변수로 동기화
- 성능 최적화, but 클라이언트 예측 어려움

---

## 구현 단계

1. ✅ 효과 타입 및 데이터 구조 정의
2. ✅ PlayerEffectManager 컴포넌트 생성
3. ✅ PlayerController에 EffectManager 추가
4. ✅ PlayerRigidBodyMovement에 효과 적용
5. ✅ 네트워크 동기화 구현
6. ✅ 효과 테스트

---

## 추가 고려사항

### 효과 우선순위
- 같은 타입의 효과가 여러 개일 때 처리 방식
  - 곱셈: 1.5 * 1.3 = 1.95
  - 덧셈: 1.5 + 0.3 = 1.8
  - 최대값: max(1.5, 1.3) = 1.5

### 효과 아이콘/UI
- 효과 상태 표시용 UI
- 효과 남은 시간 표시

### 효과 시각화
- 이동속도 증가 시 파티클 효과
- 이동속도 감소 시 느려짐 효과

