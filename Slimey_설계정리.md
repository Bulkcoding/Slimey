# Slimey — 데스크톱 슬라임 토이 (설계·구현 정리)

> 업무 중 스트레스를 짧게 해소하는 **데스크톱 위 말랑한 슬라임 장난감 앱**.
> 바탕화면 위에 떠 있는 슬라임을 잡아당기고, 던지고, 두드린다. 던지면 멀티 모니터 전체를 이동하며 외곽에서 당구공처럼 튕긴다.

- **플랫폼**: Windows 전용 / C# / WPF / .NET 8
- **원칙**: 외부 패키지 최소화, 네이티브 WPF로 MVP 우선, 책임 분리(물리·입력·모니터·애니메이션·설정), 과도한 추상화 지양
- **성능**: 평상시 낮은 CPU, 이동 중 ~60FPS, 프레임 시간 기반 계산(PC 성능 무관)
- **현재 목표**: **Phase 1 + Phase 2까지** 구현 (단일 모니터 드래그·던지기·반사·감속)

---

## 1. 기술적으로 주의해야 할 점

| 영역 | 주의 사항 |
|------|-----------|
| 투명 창 | `AllowsTransparency=True` + `WindowStyle=None` 조합. 투명 영역 클릭 통과 여부, `Topmost` 토글 가능 구조 |
| 입력 | 창 밖으로 마우스가 나가도 드래그 유지 → `CaptureMouse()` 필요 |
| 던지기 속도 | 한 프레임 이동량이 아닌 **최근 여러 샘플(슬라이딩 윈도우)** 평균/회귀로 안정적 속도 계산. 최대 속도 클램프 |
| 물리 루프 | 고정 간격 `DispatcherTimer` 대신 `CompositionTarget.Rendering` 검토. **deltaTime 기반** 갱신 |
| 터널링 | 고속 이동 시 한 프레임에 벽 통과 → **이동 거리 분할(substep)** 또는 충돌 보정 |
| 멀티 모니터 | 전체를 하나의 큰 사각형으로 처리 금지. 음수 좌표, 이종 해상도/배율/오프셋, 모니터 사이 빈 공간 대응 |
| 경계 판정 | 다음 위치가 **인접 모니터로 연결되면 통과**, **어떤 모니터에도 없으면 벽**. X/Y 축 분리 판정 |
| DPI | WPF DIP 좌표 ↔ Windows 픽셀 좌표 불일치. Per-Monitor DPI Awareness V2. 배율 경계에서 순간이동 방지 |
| 좌표계 | 물리 계산은 **물리 픽셀(스크린) 좌표**로 통일, WPF 창 배치 시 변환. `WorkingArea`(작업표시줄 제외) 기본 |
| 런타임 변경 | `SystemEvents.DisplaySettingsChanged`로 모니터 구성 재계산 |
| 리소스 해제 | 종료 시 `CompositionTarget.Rendering` 핸들러, 타이머, 트레이 아이콘, 시스템 이벤트 구독 해제 |
| 정지 처리 | 저속에서 진동 방지 → 임계 속도 이하이면 완전 정지 + 렌더 루프 유휴화(CPU 절감) |

### 좌표계 결정 (핵심)
- **물리 엔진**: 물리 스크린 픽셀 좌표(멀티모니터 가상 데스크톱 좌표) 사용.
- **창 배치**: `Left/Top`은 DIP. WPF에서 실제로는 `HwndSource`/PInvoke `SetWindowPos`로 픽셀 배치가 가장 정확하지만, MVP는 **Per-Monitor V2 + WorkingArea + 단일 배율 가정**으로 단순화하고 Phase 3에서 픽셀 배치로 승격.
- 이유: 배율이 다른 모니터를 넘나들 때 DIP 기반 `Left/Top`만 쓰면 경계에서 위치가 튀는데, 물리를 픽셀로 계산하면 충돌 판정이 일관됨.

---

## 2. 추천 아키텍처와 책임 분리

```
App (수명주기)
 └─ SlimeWindow (투명 표시 + 입력 수집 + 렌더 루프 tick)
      ├─ ThrowInputTracker   : 마우스 샘플 수집 → 투척 속도
      ├─ SlimePhysicsEngine  : 위치/속도/마찰/충돌/반사 (순수 로직)
      │    └─ MonitorLayoutService : 모니터 영역/작업영역/DPI, 경계 판정 제공
      ├─ SlimeAnimationController : Squash/Stretch/Punch/표정 (Transform 제어)
      ├─ AudioService        : 효과음 (Phase 4)
      └─ TrayIconService     : 트레이 메뉴 (Phase 5)
 └─ SettingsWindow (MVVM, AppSettings 바인딩) (Phase 5)
 └─ AppSettings (반발/마찰/최대속도/모드 등)
```

- **물리 루프는 MVVM 배제** — 명령형 tick 루프가 자연스러움.
- **MVVM은 SettingsWindow에만** 실용적으로 적용.
- `SlimePhysicsEngine`은 UI 비의존 순수 클래스 → 단위 테스트 용이.
- `MonitorLayoutService`는 경계 판정 책임까지 가져 물리 엔진이 화면 배치를 몰라도 되게 함.

---

## 3. 프로젝트 폴더 구조

```
Slimey/
├─ Slimey.sln
├─ src/
│  └─ Slimey/
│     ├─ Slimey.csproj
│     ├─ app.manifest                 # Per-Monitor DPI Awareness V2
│     ├─ App.xaml / App.xaml.cs
│     ├─ Views/
│     │  ├─ SlimeWindow.xaml / .cs
│     │  └─ SettingsWindow.xaml / .cs        (Phase 5)
│     ├─ Physics/
│     │  ├─ SlimePhysicsEngine.cs
│     │  └─ Vector2.cs                        # 자체 double 벡터
│     ├─ Services/
│     │  ├─ MonitorLayoutService.cs
│     │  ├─ ThrowInputTracker.cs
│     │  ├─ AudioService.cs                   (Phase 4)
│     │  └─ TrayIconService.cs                (Phase 5)
│     ├─ Animation/
│     │  └─ SlimeAnimationController.cs
│     ├─ Models/
│     │  └─ AppSettings.cs
│     └─ Resources/
│        └─ (추후 PNG/스프라이트)
└─ Slimey_설계정리.md
```

---

## 4. 물리 업데이트 방식

- **렌더 루프**: `CompositionTarget.Rendering` 사용. `Stopwatch`로 실제 경과 시간을 재서 `dt`(초) 계산 → 프레임률 독립.
- **유휴화**: 정지 상태 & 드래그 아님 → 렌더 핸들러 detach(또는 no-op)로 CPU 절감. 입력 발생 시 재개.
- **적분**: semi-implicit Euler
  - `velocity *= pow(damping, dt)` 형태(프레임 독립 마찰) 또는 `velocity -= velocity * friction * dt`.
  - `position += velocity * dt`.
- **정지 임계**: `|velocity| < StopThreshold` → `velocity = 0`, 루프 유휴화.
- **최대 속도**: `velocity`를 `MaxSpeed`로 클램프.
- **터널링 방지(substep)**: 한 프레임 이동량이 슬라임 크기보다 크면 이동을 N등분해 각 구간마다 충돌 검사.
- **드래그 중**: 물리 정지, 위치는 마우스를 따라감(약간의 스무딩 가능). `ThrowInputTracker`가 샘플 적재.

### 투척 속도 계산 (ThrowInputTracker)
- 최근 N개(예: 5~8개) `(위치, 타임스탬프)` 샘플을 링버퍼에 저장.
- 놓는 순간: 가장 오래된 샘플과 최신 샘플 사이 `Δpos/Δt`(또는 최근 100ms 윈도우 평균)로 속도 산출.
- `MaxThrowSpeed`로 클램프.

---

## 5. 멀티 모니터 충돌 처리 방식 (Phase 3 상세, Phase 2 단일화)

- `MonitorLayoutService`가 `System.Windows.Forms.Screen.AllScreens`(또는 PInvoke `EnumDisplayMonitors`)로 각 모니터의 `WorkingArea` 사각형 목록 보관.
- **핵심 판정**: `IsInsideAnyMonitor(point)` — 점이 어느 하나의 모니터 사각형에 포함되는가?
- **축 분리 이동 + 충돌**:
  1. X만 이동한 후보 위치 → 슬라임 경계 박스가 어떤 모니터에도 안 걸치면 X 벽 → `vx` 반전, 경계로 클램프.
  2. Y만 이동한 후보 위치 → 동일하게 Y 판정.
  - 이렇게 하면 인접 모니터 경계(맞닿은 면)는 통과, 빈 공간·외곽은 반사.
- **반발 계수**: 반전 시 `v *= Restitution`(<1)로 감쇠.
- **substep**과 결합해 고속 통과 방지.
- 단일 모니터(Phase 2)에서는 모니터 목록이 1개인 특수 케이스로 동일 로직 사용.

> Phase 2에서는 `MonitorLayoutService`가 주 모니터 `WorkingArea` 하나만 반환하도록 구현하고, Phase 3에서 전체 모니터·경계 통과 로직을 채운다. 인터페이스는 처음부터 다중 모니터를 가정해 설계.

---

## 6. DPI 좌표 처리 방식

- `app.manifest`에 **Per-Monitor DPI Awareness V2** 선언.
- WPF의 `VisualTreeHelper.GetDpi()` / `Window.DpiChanged` 이벤트로 창의 현재 배율 확인.
- 물리는 **픽셀 좌표**로 계산 → 창 배치 시 해당 모니터 배율로 DIP 변환하거나 PInvoke `SetWindowPos`로 픽셀 직접 배치.
- MVP 단순화: 단일 배율 가정, `Left/Top` = 픽셀 / 배율. Phase 3에서 배율이 다른 모니터 경계 처리 승격.
- 창 **크기도** 배율에 맞춰 재계산해 시각적 크기 일관 유지.

---

## 7. Phase 1 · Phase 2 파일 목록

**Phase 1 (최소 실행: 투명 창 + 도형 슬라임 + 드래그 + 우클릭 종료)**
- `src/Slimey/Slimey.csproj`
- `src/Slimey/app.manifest`
- `src/Slimey/App.xaml` / `App.xaml.cs`
- `src/Slimey/Views/SlimeWindow.xaml` / `SlimeWindow.xaml.cs`
- `src/Slimey/Models/AppSettings.cs`

**Phase 2 (단일 모니터 물리: 투척 속도 + 관성 + 반사 + 마찰/정지 + deltaTime + 클램프 + substep)**
- `src/Slimey/Physics/Vector2.cs`
- `src/Slimey/Physics/SlimePhysicsEngine.cs`
- `src/Slimey/Services/MonitorLayoutService.cs`
- `src/Slimey/Services/ThrowInputTracker.cs`
- `src/Slimey/Animation/SlimeAnimationController.cs` (기본 Squash/Stretch 골격)
- (`SlimeWindow`에 렌더 루프·입력 연결 추가)

---

## 8. 빌드 및 실행 방법 (예정)

```powershell
# 프로젝트 생성 후
cd C:\claudeProject\Slimey
dotnet build src/Slimey/Slimey.csproj -c Debug
dotnet run  --project src/Slimey/Slimey.csproj
```

- **NuGet**: Phase 1~2는 **외부 패키지 불필요**.
  - 모니터 조회에 `System.Windows.Forms.Screen`을 쓰려면 `.csproj`에 `<UseWindowsForms>true</UseWindowsForms>` 추가(패키지 아님, SDK 내장). 대안은 PInvoke `EnumDisplayMonitors`(WinForms 의존 제거). MVP는 WinForms 참조가 간단.
  - 트레이 아이콘(Phase 5)은 `System.Windows.Forms.NotifyIcon` 또는 `Hardcodet.NotifyIcon.Wpf` 검토.

---

## 9. 테스트 체크리스트

**Phase 1**
- [ ] 슬라임 창이 투명 배경으로 뜨고 도형 슬라임만 보인다
- [ ] 작업표시줄에 안 뜬다 (`ShowInTaskbar=False`)
- [ ] 좌클릭 드래그로 슬라임이 마우스를 따라온다 (창 밖으로 나가도 유지)
- [ ] 우클릭 메뉴에서 종료가 된다
- [ ] 앱 종료 시 프로세스가 남지 않는다

**Phase 2 (단일 모니터)**
- [ ] 빠르게 던지면 빠르게, 살살 놓으면 느리게 날아간다
- [ ] 비정상적으로 빠른 속도가 안 나온다 (MaxThrowSpeed 클램프)
- [ ] 화면 4변에서 반사된다
- [ ] 충돌마다 반발 계수로 속도가 준다
- [ ] 시간이 지나면 마찰로 감속 후 완전히 멈춘다 (저속 진동 없음)
- [ ] 아주 빠르게 던져도 벽을 통과하지 않는다 (substep)
- [ ] 정지·무입력 시 CPU 사용량이 거의 0에 수렴한다
- [ ] 작업표시줄 영역으로는 내려가지 않는다 (WorkingArea)

**Phase 3+ (구성별, 이후)**
1. 단일 1920×1080  2. 좌우 듀얼  3. 음수 X 보조모니터  4. 이종 해상도 듀얼
5. 상하 어긋난 배치  6. 트리플  7. 모니터 사이 빈 공간  8. 배율 다른 구성  9. 실행 중 구성 변경

---

## 10. 핵심 설정값 (AppSettings — 하드코딩 금지 항목)

| 키 | 기본값(초안) | 설명 |
|----|-------------|------|
| `Friction` / `Damping` | 0.98/s 계열 | 공기저항·마찰 |
| `Restitution` | 0.7 | 반발 계수 |
| `MaxSpeed` | 4000 px/s | 관성 최대 속도 |
| `MaxThrowSpeed` | 3500 px/s | 투척 초기 속도 상한 |
| `StopThreshold` | 20 px/s | 이하이면 완전 정지 |
| `SlimeSize` | 96 px | 슬라임 지름 |
| `Softness` | 0.5 | Squash/Stretch 강도 |
| `AlwaysOnTop` | true | Topmost |
| `SoundEnabled` | true | 효과음 |
| `ThrowMode`/`PunchMode` | on/on | 상호작용 모드 |
| `SampleWindowMs` | 100 | 투척 속도 계산 윈도우 |
| `SubstepMaxPx` | SlimeSize/2 | substep 분할 기준 |

---

## 디자인 방향
- 민트+보라 반투명 젤리 슬라임, 귀엽지만 유아적이지 않게
- 밝은 배경·둥근 카드형 설정창, 은은한 그림자, 보라 포인트
- 구현 우선순위: ①투명 창 안정성 ②즉각 입력 반응 ③자연스러운 타격감 ④정확한 멀티모니터 이동 ⑤낮은 CPU

---

## 로직·디자인 분리 전략

**결론: Phase 1·2는 한 세션 통합 진행, Phase 4·5부터 분리.**

### 분리 가능한 경계 (계약만 고정하면 병렬 가능)
| 트랙 | 담당 파일 | 성격 |
|------|-----------|------|
| 로직 | `Physics/`, `Services/`, `ThrowInputTracker`, `SlimePhysicsEngine`, `MonitorLayoutService` | UI 비의존 순수 계산, 단위 테스트 가능 |
| 디자인 | `Views/*.xaml`, 슬라임 비주얼, `SettingsWindow` UI, 색/그림자/카드 스타일 | XAML 비주얼·리소스 |

### 두 트랙의 접점 (여기만 인터페이스로 확정)
1. **`SlimeAnimationController` ↔ XAML Transform 계약**
   - XAML은 `x:Name="SlimeScale"`(ScaleTransform), `x:Name="SlimeRotate"`(RotateTransform)를 반드시 노출.
   - 컨트롤러는 이 두 Transform만 조작 → 비주얼 교체(도형→PNG→스프라이트)해도 이름만 유지하면 로직 불변.
2. **`SlimeWindow` ↔ 렌더 루프**
   - 물리 tick 결과(위치·속도·충돌 세기)를 애니메이션에 전달하는 지점.

### 왜 지금은 통합인가
- Phase 1·2의 "디자인"은 임시 도형뿐 → 분리 시 디자인 트랙 할 일이 거의 없음.
- 우선순위 ①투명창 안정성 ②입력 반응 ③타격감이 전부 로직↔표시 접점에서 결정됨 → 나누면 조율 비용만 증가.

### 분리 시점: Phase 4·5
- 로직이 거의 완성된 상태 → **디자인 트랙(젤리 비주얼·설정창 스킨)을 별도 세션/에이전트로 병렬** 처리 효율 최대.
- 이때 `SlimeAnimationController` 계약과 `AppSettings` 바인딩만 지키면 충돌 없음.

---

## 진행 로드맵
- **Phase 1** 최소 실행(투명 창·도형·드래그·종료)
- **Phase 2** 단일 모니터 물리(투척·관성·반사·마찰·substep)  ← *현재 구현 목표*
- **Phase 3** 멀티 모니터(음수 좌표·경계 통과·빈 공간 반사·이종 DPI)
- **Phase 4** 타격감(Squash&Stretch·Punch·효과음·파티클·BONK/SPLAT/BOING)
- **Phase 5** 설정창·트레이·저장·위치초기화·숨김·일시정지·로그

각 Phase 종료 시 항상 빌드·실행 가능 상태 유지.

---

## 구현 현황 (2026-07-23 기준)

- **Phase 1 완료** — 투명 창·도형 슬라임·드래그·우클릭 메뉴(위치 초기화/항상 위/일시정지/종료)
- **Phase 2 완료** — 투척 속도(시간창 샘플)·관성·반사·마찰·완전 정지·deltaTime·substep 터널링 방지, 유휴 시 렌더 루프 detach(CPU 0%)
- **Phase 3 완료** — `IWalkableArea`(모니터 합집합 커버리지)로 충돌 판정 교체
  - 실 트리플 모니터(음수 X 포함) 배치에서 자동 검증 13/13 PASS
  - 내부 경계 통과, 외곽/작업표시줄/빈 좌표 반사, 축 분리 충돌, 구성 변경 대응

### 알려진 제약 / 다음 개선
- **혼합 DPI(테스트 #8)**: 창 배치가 현재 단일 배율(현재 모니터 스케일) 기준 DIP 변환.
  동일 DPI 구성(#1~7,9)은 음수 좌표·트리플 포함 정확. 서로 다른 배율 모니터 경계에서는
  `OnDpiChanged` 로 재보정되지만 순간 오차 가능 → **Win32 `SetWindowPos` 물리 픽셀 배치로 승격**하면 완전 해소(업그레이드 지점 표시됨).
- 물리·충돌 로직은 `PhysCheck` 콘솔로 회귀 검증 가능(스크래치패드, 커밋 제외).

### 디자인 트랙 (Phase 4·5 UI)
- **테마** `Resources/Theme.xaml` — 민트+보라 팔레트, 카드/토글스위치/슬라이더/버튼 스타일. `App.xaml` 에 병합.
- **설정창** `Views/SettingsWindow.xaml(.cs)` — 둥근 카드 UI(그림자·보라 포인트), 타이틀바 드래그 이동, ✕=숨김.
  - `DataContext = AppSettings` **직접 양방향 바인딩** → 슬라이더/토글이 물리 루프에 **즉시 반영**.
  - Bounce Power(Restitution) / Slime Softness / Throw Power 슬라이더, Throw·Punch·Sound·Particles·Always on Top·표시·일시정지 토글, 위치 초기화·종료.
- **연동 계약**: `AppSettings : INotifyPropertyChanged`(사용자 조절 항목만 알림). `SlimeWindow` 가 PropertyChanged 를 구독해 Topmost·Paused·표시 여부를 즉시 적용. 설정창 → `SlimeWindow.ResetPositionPublic()` 위임.
- 슬라임 우클릭 메뉴: **설정... / 위치 초기화 / 종료** 로 간소화(토글은 설정창이 담당해 상태 동기화 문제 제거).
- 검증: 전체 컴파일 0 경고/0 오류, 앱 실행 정상, `StaticResource` 키 전량 정의·전방참조 없음 확인.
  (Phase 4 로직 — 파티클/오디오/오버레이 — 은 병렬 로직 트랙에서 통합 진행 중.)

### 디자인 트랙 2차 (스킨·상호작용)
- **스킨 시스템** — `Models/SlimeSkinKind`(Jelly/Billiard) + `Views/Skins/*.xaml`(Viewbox 기반 UserControl).
  `SlimeWindow` 의 `SkinHost`(ContentControl)에 선택 스킨을 주입, `AppSettings.Skin` 변경 시 즉시 교체.
  Transform(Squash/Stretch)은 호스트에 그대로 걸려 스킨 무관하게 동작. **새 스킨 = UserControl 추가 + enum 확장**만.
  - **당구공(Billiard)** 스킨 추가 — "당구공처럼 튕긴다" 컨셉 반영(글로시 8-ball).
- **설정창** — 스킨 선택 칩(RadioButton, `EnumMatchToBooleanConverter`), Sound Volume 슬라이더 추가.
- **낚아채기** — 날아가던 슬라임을 클릭하면 그 자리에 잡혀 정지(펀치로 튕겨내지 않음). 정지 상태 클릭은 펀치 유지.
- **오버레이 성능 수정** — 전체 데스크톱 투명창 → **작은 창이 파티클 무리를 따라 이동**(면적↓, 창 조작 스톨 제거). 실측: 충돌 프레임 최대 116ms→54ms.
- 검증: 스킨 전환/설정창 로드/SoundVolume 바인딩 자동 검증 PASS, 빌드 0 경고/0 오류.

### 디자인 트랙 3차 (표정·크기·스킨별 이펙트)
- **표정 시스템** — `Models/SlimeExpression`(Normal/Flying/Dizzy) + `ISkinExpressions` 인터페이스.
  젤리 스킨이 눈 표정 그룹으로 구현(구현 안 한 스킨엔 미전달). `SlimeWindow` 가 속도/충돌로 결정:
  빠르면 Flying(신난 눈), 강한 충돌 직후 Dizzy(× ×), 그 외 Normal. 상태 변화 시에만 반영, Dizzy 중엔 렌더 루프 유지.
- **크기 설정** — `AppSettings.SlimeSize` INPC 화 + 설정창 크기 슬라이더(48~180px). 변경 시 창 리사이즈·경계 재확인.
- **당구공 전용 처리** — `SlimeAnimationController.Rigid`(당구공은 찌그러짐/회전 없음).
  충돌 이펙트를 스킨별로 분기: 젤리=스플랫, **당구공=쿠션 스파크**(충돌 법선의 접선을 따라 밝은 입자 양방향 분사 + 딱딱한 소리).
  이를 위해 `PhysicsStepResult.CollisionNormal`(진행 반대=안쪽 법선) + `ParticleSystem.EmitCushion` + `Particle.Spark` 추가.
- 검증: 표정 토글/리지드 무변형/쿠션 스파크 방향·플래그/크기 리사이즈 자동 검증 9/9 PASS.

### 디자인 트랙 4차 (몬스터볼 + 클릭 열림 이펙트)
- **몬스터볼 스킨** — `SlimeSkinKind.Pokeball` + `Views/Skins/PokeballSkin`(원형 클립: 빨강/흰색/검정 띠/버튼/광택). 단단한 스킨이라 리지드(찌그러짐 X), 벽 충돌은 쿠션 스파크. 설정창 칩 3개(슬라임/당구공/몬스터볼).
- **클릭 열림 이펙트** — `ISkinClickEffect` 인터페이스. 몬스터볼 클릭 시:
  스킨 내부 **빛 플래시**(중앙 RadialGradient Opacity 애니메이션) + `ParticleSystem.EmitOpen`(방사형 밝은 빛 입자) + 소리. 제자리 연출(튕기지 않음).
  클릭 반응은 `DoClickEffect`에서 스킨별 분기(젤리=펀치 스쿼시, 당구공=딱 튕김, 몬스터볼=열림).
- 검증: 스킨 전환/리지드/열림 입자 방사·Spark/클릭 시 제자리 연출 자동 검증 9/9 PASS.

### 디자인 트랙 5차 (볼 3종 + 3D 디테일)
- **3D 디테일링** — 볼 스킨을 구면 음영(가장자리 radial 음영)+상단 광택+선명 스페큘러+좌상단 림 라이트+다층 금속 베젤 버튼으로 재작업. RenderTargetBitmap 미리보기로 확인.
- **볼 통합 `BallSkin`** — 몬스터볼/하이퍼볼(울트라)/마스터볼을 파라미터화된 단일 UserControl 로 처리(공통 3D 구조 + 종류별 위 절반 색/상단 마킹). `SlimeSkinKind` 에 Ultra/Master 추가. (기존 PokeballSkin 은 BallSkin 으로 대체·삭제.)
  - 하이퍼볼: 검정+노란 "H", 마스터볼: 보라+분홍 "M"·양쪽 점. 마킹은 구면 음영 아래에 두어 입체감과 통합.
- **클릭 열림 이펙트 일반화** — `SkinHost.Content is ISkinClickEffect` 로 판정 → 세 볼 모두 클릭 시 열림 연출. 설정창 스킨 칩 5종(UniformGrid).

### 디자인 트랙 6차 (스핀 + 포켓몬볼 열림 애니메이션)
- **스핀** — 드래그 곡선(curl)으로 각속도 충전(진행 방향 변화율 = deg/s). 오른쪽으로 휘어 돌리면 우회전, 왼쪽이면 좌회전. 던지면 스핀이 실려:
  - `SlimePhysicsEngine.AngularVelocity/SpinAngle` + **마그누스 효과**(속도 수직 방향 가속 = MagnusStrength·angVel·speed)로 궤적이 휨
  - 각속도 감쇠(SpinFriction), 회전 중엔 수면 진입 안 함
  - `SlimeAnimationController.Tick(dt, vel, spinDeg)` — 리지드에서도 스핀 회전만은 반영(공이 돎)
  - 설정: MaxAngularVelocity/SpinFriction/SpinStopThreshold/MagnusStrength
- **포켓몬볼 열림 애니메이션**(레퍼런스 참고) — 클릭 시 위/아래 반쪽이 갈라지고(TopShift/BottomShift) 내부 **청록 에너지 글로우**(CoreLight+CoreScale)가 번쩍인 뒤 빛 플래시(OpenFlash) → 다시 닫힘. 방사형 입자(EmitOpen)와 함께 "방출" 연출. 세 볼 공통.
- 검증: 마그누스 궤적 휨/회전각 누적/각속도 감쇠, 리지드·젤리 스핀 회전, 열림 프레임 렌더 자동 검증 7/7 PASS.

### 디자인 트랙 7차 (스핀 개선 + 볼 열림 토글 + 스핀 이펙트)
- **스핀 이펙트(비주얼)** — Pokémon GO 스타일. 슬라임 창을 크기의 2배(양쪽 패딩)로 키워 주변에 **좌우 모션블러 아크(어두운 언더스트로크+흰색) + 노란 반짝이 궤도**를 그림. 각속도로 세기, 스핀각으로 반짝이 회전. (`SlimeWindow` 레이아웃: SpinFx Viewbox + 중앙 SlimeBox)
- **스핀 관성 수정(멈춤 버그)** — 드래그 곡선 스핀을 순간값이 아니라 **누적(관성)+완만한 감쇠**로 변경 → 직선 구간에서도 유지되어 돌리는 중 끊기지 않음.
- **스핀 벽 반응** — 벽 충돌 시 각속도가 **접선 방향 속도로 전달(SpinWallKick)** + 스핀 소모(SpinWallRetain). 검증: 무스핀 vy=0 vs 스핀 vy=275.
- **포켓몬볼 열림 = 토글 유지** — 클릭 시 위/아래로 크게 갈라져 **완전히 열린 채 유지**(청록 유리 내부 글로우), 다시 클릭하면 닫힘. `ISkinClickEffect.PlayClick()`→bool(열림 여부). 검증: 열림 후 TopShift.Y=-21 유지, 재클릭 시 0 복귀.
- 검증: 스핀 벽반응/열림 토글·유지/스핀 FX·열림 상태 렌더 자동 검증 전부 PASS, 빌드 0/0, 유휴 CPU 0%.

### 디자인 트랙 8차 (스핀 FX 개선 + 낚아채기 보완)
- **스핀 이펙트 부드럽게** — 딱딱한 정적 아크 제거. `BuildSpinFx()` 로 원 둘레 접선 방향 짧은 막대(스트릭) 14개를 **BlurEffect+회전(궤도)**으로 그려 부드러운 스핀 블러 링 구현.
- **반짝이 개선** — 불규칙 위치/크기(고정 시드 Random) + 흰-금 방사 그라데이션(밝은 곳도 대비) + **시간 기반 트윈클**(각자 위상). 트윈클은 렌더 루프에서 갱신 → 무한 애니메이션 없이 유휴 CPU 0 유지.
- **낚아채기 보완** — ① 창 전체에 투명 히트영역(Border) → 날아가는 슬라임을 넉넉한 범위(패딩 포함)로 클릭 가능. ② 놓기 판정을 `ClassifyRelease(moved, grabbedSpeed)` 로 분리: 조금이라도(>CatchSpeedThreshold) 움직이던 것을 클릭하면 속도 무관 **낚아채기(제자리 정지)**, 완전 정지만 클릭(펀치/열림). "느려도/빨라도 잡힘" 해결.
- 검증: ClassifyRelease(느림15·빠름2500→CatchHold, 정지→Click) 5/5 + 스핀 FX 렌더 PASS, 빌드 0/0, 유휴 CPU 0%.

### 디자인 트랙 9차 (실제 앱 낚아채기 실측 디버깅)
- **실제 OS 입력 합성으로 실앱을 구동해 원인 규명**(외부 드라이버로 Slimey.exe 던지기·클릭):
  - 클릭은 앱에 도달함(정지 젤리 클릭 → 펀치로 312px 이동). 오버레이는 WS_EX_TRANSPARENT 로 클릭 통과(범인 아님).
  - 정지·느림·중속 클릭은 잡힘(dragging=true, v=0). 확정.
  - "안 잡힘"의 실제 원인 = **빠른 공이 작은 창보다 빨라, 클릭이 전달되는 순간 창이 이미 그 지점을 지나가 빗나감.**
- **해결: 잡기 창을 4배(384px)로 확대** → 빠르게 날아도 클릭 지점에 창이 커서 아래 남아 낚아채짐. (스핀 이펙트는 중앙 고정 2.5배로 창 크기와 분리해 확대에 안 깨지게.)
- (시도했다 되돌림: 전역 저수준 마우스 훅 — 드래그 구동이 불안정해 롤백.)
- 알려진 절충: 4배 창이 슬라임 주변 ~144px 영역의 데스크톱 클릭을 가로챔(잡기 쉬움의 대가). 창 배율은 코드 상수로 조정 가능.

### 디자인 트랙 10차 (스핀 정리 + 설정창 재디자인 + 잡기 단축키)
- **스핀 이펙트**: 좌우 모션블러 제거, **노란 반짝이만** 유지. 반짝이는 스핀 각도를 따라가지 않고 **완만한 고정 속도(22°/s) 자체 회전 + 트윈클**.
- **설정창 재디자인**(Clawd `setting_1/2` 참고): 다크 **2-pane**(좌 네비 사이드바 + 우 내용), 오렌지 포인트, 행 기반(라벨+설명+컨트롤), 섹션 헤더. 네비: 일반/테마/소리/단축키.
  - **일반**: 크기(%)·Bounce·Softness·Throw 슬라이더 + Throw/Punch/AlwaysOnTop/일시정지/표시 토글 + 위치초기화.
  - **테마**: 스킨 미리보기 카드 5종(실제 스킨 렌더), 선택 카드 오렌지 테두리.
  - **소리**: SoundEffects·Volume·Particles. **단축키**: 잡기 키 표시/재설정.
  - `Theme.xaml` 다크 팔레트로 전면 교체. `OnNavChanged` 초기 선택 NRE 가드(설정 열 때 크래시 수정).
- **크기 설정** 추가(일반 탭, 48~180px).
- **잡기 단축키(전역)**: `RegisterHotKey`(WM_HOTKEY, HwndSource 훅) → **슬라임을 마우스 커서로 즉시 회수·정지**. 기본 Ctrl+Shift+G, 설정창에서 재바인딩(키 캡처). 실앱 검증: 커서로 오차 0 회수.
- **컨텍스트 메뉴** 다크 라운드 스타일(menu.png 참고).
- **포켓몬볼 열림 강화**: 분리 폭 확대(위 -46/아래 +30, 코어 1.7배) → 뚜껑이 확 열리고 청록 유리 내부가 크게 드러남.
- 검증: 설정 3패널 렌더 / 스핀 반짝이 렌더 / 포켓몬볼 열림 렌더 / 단축키 실앱 회수(오차 0) PASS, 빌드 0/0, 유휴 CPU 0%.

### 디자인 트랙 11차 (던지기·포켓몬볼·단축키·당구)
- **던지기 = 마우스 속도 × 가중치**: 측정창 60ms(JsonIgnore, 놓는 순간 속도 반영). `ThrowPower`는 순수 가중치(1.0=실제 속도). 설정 라벨 "던지기 가중치". 검증: ×1.0→5000, ×1.5→7000.
- **포켓몬볼 열림 재설계**: 빛/플래시 제거. **오목한 금속 내부** 디자인 추가. **가운데 버튼을 뚜껑(TopGroup)에 부착** → 열리면 버튼도 위로 올라가 가운데에서 사라짐. `ISkinClickEffect`(IsOpen/SetOpen).
- **여닫기 상호작용**: 클릭으로 토글(잡을 때 상태 기준 `_ballWasOpen`), **열린 뒤 다음 액션(재클릭/드래그/던지기) 시 자동으로 닫힘**(CloseBallIfOpen).
- **단축키 강화**: 키보드(RegisterHotKey) + **마우스 버튼 트리거**(WH_MOUSE_LL 훅 + GetAsyncKeyState 수정자 검사, 삼킴). 복합키 지원. 설정창에서 **'변경'으로 캡처(키/클릭) → '저장' 눌러야 적용**(라이브 반영 아님). AppSettings에 CatchHotkeyMouse 추가.
- **당구 컨셉**: `BilliardSkin`을 색상 파라미터화(수구=흰색, 빨강/노랑). 우클릭 메뉴(당구공일 때만) **4구(빨강2·노랑1)/3구(빨강1·노랑1)/치우기**. `ExtraBallWindow`(클릭 통과·자체 물리 바운싱)로 랜덤 위치·속도 스폰. 검증: 4구=3개, 3구=2개, 치우기=0.
- 검증: 던지기/스폰 로직 + 당구 3색·포켓몬볼 열림 렌더 PASS, 빌드 0/0, 유휴 CPU 0%.

### 디자인 트랙 12차 (큐대 모드 클릭 라우팅 수정 + 세로 스핀=끌어치기/밀어치기)
- **큐대 모드 안 되던 원인**: 클릭 감지용 `Border`가 `Background="Transparent"`(알파 0). WPF 투명(레이어드) 창은 **알파 0 픽셀을 OS 단계에서 클릭 통과**시켜, 공 주변(패딩) 클릭이 `OnMouseLeftButtonDown`에 도달 안 함 → 조준 시작 자리가 사실상 없었음.
  - **수정**: 캐치용 Border 배경을 `#01000000`(알파 1, 비가시)로 → OS가 클릭을 창으로 전달. 검증: 공 왼쪽 60/90/120/150px 클릭 후 당김 → 4/4 오른쪽 발사(세기 858~1465, 당긴 만큼 비례).
- **세로 스핀 의미 변경**: 기존엔 세로 점→각속도(마그누스 옆휨)여서 "아래 스핀=되돌아옴" 감각이 없었음. **세로=표면 스핀(끌어치기/밀어치기), 가로=사이드(마그누스)** 로 재매핑.
  - 물리엔진에 `SurfaceSpin`(px/s)·`SpinShotDir` 추가. 매 프레임 `Velocity += SpinShotDir × SurfaceSpin × DrawFollowStrength × dt`, `SurfaceSpin`은 지수 감쇠. 음수(끌어치기)면 진행 반대로 힘→전진하다 반전해 되돌아옴, 양수(밀어치기)면 더 밀고 나감.
  - 매핑: 6시(아래,+y)→`SurfaceSpin=-power`(끌어치기), 12시(위,-y)→`+power`(밀어치기), 가로 점→`AngularVelocity=_spinOffset.X×MaxAngularVelocity`.
  - 튜닝(시뮬레이션): 처음 1.6/0.9는 복귀가 대포처럼 너무 셈("되돌아오는거 너무 쎄"). **현실적으로 `DrawFollowStrength=1.1`, `SurfaceSpinFriction=1.0`** 로 낮춤(JsonIgnore). 잡기/조준 시작 시 SurfaceSpin=0.
- 검증(실앱 물리 진행): 6시 강타→565px 전진 후 **반전해 원점 살짝 뒤(−57px)에서 멈춤**, 12시→3544px(무스핀 1743보다 멀리), 무스핀→굴러 정지, 3시→각속도 1200·반전없음. 빌드 0/0.
