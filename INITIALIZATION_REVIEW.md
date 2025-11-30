# 초기화 로직 재검토 및 개선사항 보고서

## 📋 현재 구조 개요

### 초기화 흐름
1. `GameManager.Awake()` → 핵심 매니저들 초기화 시작
2. `GameManager.Start()` → Canvas 찾기 + 코루틴으로 씬 매니저 초기화 대기
3. `GameManager.OnSceneLoaded()` → 씬 로드 시 동일한 흐름 반복
4. `GameManager.FindSceneManager()` → `ISceneManager.Initialize()` 호출
5. `TitleGameManager.Initialize()` / `MainGameManager.Initialize()` → 각자의 Canvas 초기화

---

## ⚠️ 발견된 문제점

### 1. **중복 초기화 문제** (심각도: 높음)

**문제:**
- `TitleGameManager.Initialize()`에서 `TitleCanvas.Initialize()` 호출
- `TitleCanvas.Initialize()`에서 `_titleGameManager?.OnInitialize(this)` 호출
- `OnInitialize()`에서 다시 상태 초기화 및 `SetupNetworkEvents()` 호출
- 결과: `SetupNetworkEvents()`가 두 번 호출될 수 있음

**위치:**
```csharp
// TitleGameManager.cs
public void Initialize(...) {
    SetupNetworkEvents();  // 1번째 호출
    InitializeTitleCanvas(...);
}

// TitleCanvas.cs
public void Initialize(...) {
    _titleGameManager?.OnInitialize(this);  // OnInitialize 호출
}

// TitleGameManager.cs
public void OnInitialize(TitleCanvas titleCanvas) {
    SetupNetworkEvents();  // 2번째 호출 (중복!)
}
```

**영향:**
- 이벤트 핸들러가 중복 등록될 수 있음
- 메모리 누수 가능성
- 예상치 못한 동작 발생 가능

---

### 2. **비효율적인 GameObject.Find() 사용** (심각도: 중간)

**문제:**
- `GameObject.Find()`를 여러 곳에서 반복 호출
- 성능 저하 가능성 (씬이 복잡할수록 느림)

**위치:**
- `GameManager.FindCanvas()` → `GameObject.Find("Canvas")`
- `GameManager.FindSceneManager()` → `GameObject.Find("SceneManager")`
- `TitleGameManager.InitializeTitleCanvas()` → `GameObject.Find("Canvas")`
- `TitleCanvas.Initialize()` → `GameObject.Find("SceneManager")`
- `MainGameManager.InitializeMainCanvas()` → `GameObject.Find("Canvas")`

**영향:**
- 초기화 시간 증가
- 씬이 복잡할수록 성능 저하

---

### 3. **불명확한 책임 분리** (심각도: 중간)

**문제:**
- Canvas 초기화 책임이 혼재되어 있음
- `GameManager.FindCanvas()`는 참조만 설정하고 초기화는 하지 않음
- 씬 매니저가 Canvas를 찾아서 초기화함
- 하지만 `TitleCanvas`는 다시 `TitleGameManager`를 찾아서 `OnInitialize()` 호출

**영향:**
- 코드 가독성 저하
- 유지보수 어려움
- 초기화 순서 파악 어려움

---

### 4. **중복 코드** (심각도: 낮음)

**문제:**
- `TitleGameManager.Initialize()`와 `OnInitialize()`에 중복된 초기화 로직
- 상태 초기화, 닉네임 로드 등이 중복됨

**위치:**
```csharp
// Initialize()와 OnInitialize() 모두에서
_isConnecting = false;
SetupNetworkEvents();
_playerNickname = GameManager.MyLocalNickname;
```

---

### 5. **초기화 순서 의존성** (심각도: 낮음)

**문제:**
- `GameManager.Start()`에서 `FindCanvas()`를 호출하지만, Canvas는 아직 초기화되지 않음
- 씬 매니저가 나중에 초기화하므로 실제로는 문제 없지만, 코드 흐름이 혼란스러움

---

## ✅ 개선 제안

### 개선안 1: 중복 초기화 제거

**제안:**
- `TitleGameManager.OnInitialize()`에서 `SetupNetworkEvents()` 호출 제거
- `Initialize()`에서만 한 번 호출하도록 수정
- 또는 `OnInitialize()`를 단순히 참조 설정만 하도록 변경

**수정 예시:**
```csharp
// TitleGameManager.cs
public void OnInitialize(TitleCanvas titleCanvas)
{
    _titleCanvas = titleCanvas;
    // SetupNetworkEvents() 제거 (이미 Initialize()에서 호출됨)
    // 상태 초기화도 제거 (이미 Initialize()에서 수행됨)
}
```

---

### 개선안 2: Find 최적화

**제안:**
- `GameObject.Find()` 대신 캐시된 참조 사용
- 또는 `FindObjectOfType<T>()` 사용 (더 효율적)
- 초기화 시 한 번만 찾고 참조 저장

**수정 예시:**
```csharp
// TitleGameManager.cs
private void InitializeTitleCanvas(...)
{
    if (_titleCanvas == null)
    {
        // FindObjectOfType 사용 (더 효율적)
        _titleCanvas = FindObjectOfType<TitleCanvas>();
    }
    // ...
}
```

---

### 개선안 3: 책임 분리 명확화

**제안:**
- 씬 매니저가 Canvas를 찾아서 초기화하는 것은 유지
- 하지만 `TitleCanvas.Initialize()`에서 `TitleGameManager`를 찾는 로직 제거
- 씬 매니저가 자신의 참조를 Canvas에 전달하도록 변경

**수정 예시:**
```csharp
// TitleGameManager.cs
private void InitializeTitleCanvas(...)
{
    _titleCanvas = FindObjectOfType<TitleCanvas>();
    if (_titleCanvas != null)
    {
        // TitleGameManager 참조를 직접 전달
        _titleCanvas.SetTitleGameManager(this);
        _titleCanvas.Initialize(gameManager, gameDataManager);
    }
}
```

---

### 개선안 4: 중복 코드 제거

**제안:**
- `OnInitialize()`를 단순화하여 참조 설정만 수행
- 모든 초기화 로직을 `Initialize()`로 통합

---

### 개선안 5: 초기화 순서 명확화

**제안:**
- `GameManager.FindCanvas()`를 제거하거나, 씬 매니저 초기화 후에 호출
- 또는 주석으로 명확히 표시

---

## 📊 우선순위별 개선 계획

### 🔴 높은 우선순위 (즉시 수정 필요)
1. **중복 초기화 문제 해결** - 이벤트 중복 등록 방지
2. **Find 최적화** - 성능 개선

### 🟡 중간 우선순위 (점진적 개선)
3. **책임 분리 명확화** - 코드 가독성 향상
4. **중복 코드 제거** - 유지보수성 향상

### 🟢 낮은 우선순위 (리팩토링 시 고려)
5. **초기화 순서 명확화** - 문서화 및 주석 개선

---

## 🎯 권장 수정 사항

### 즉시 수정 권장:
1. `TitleGameManager.OnInitialize()`에서 중복 초기화 로직 제거
2. `GameObject.Find()` → `FindObjectOfType<T>()` 변경
3. `TitleCanvas.Initialize()`에서 `TitleGameManager` 찾기 로직 제거 (씬 매니저가 직접 참조 전달)

### 점진적 개선:
4. 초기화 로직 통합 및 단순화
5. 주석 및 문서화 개선

---

## 📝 결론

현재 구조는 전반적으로 잘 설계되어 있으나, 몇 가지 개선이 필요합니다:

**강점:**
- 명확한 초기화 순서 (핵심 매니저 → 씬 매니저 → Canvas)
- 인터페이스를 통한 느슨한 결합
- 씬별 매니저가 자신의 Canvas를 관리하는 구조

**개선 필요:**
- 중복 초기화 제거
- Find 최적화
- 책임 분리 명확화

**전체 평가:**
- 구조: ⭐⭐⭐⭐ (4/5)
- 성능: ⭐⭐⭐ (3/5)
- 유지보수성: ⭐⭐⭐ (3/5)
- 안정성: ⭐⭐⭐⭐ (4/5)

**종합 점수: 3.5/5** - 개선 후 4.5/5 예상

