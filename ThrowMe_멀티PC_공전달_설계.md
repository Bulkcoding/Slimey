# ThrowMe — 멀티 PC 공 전달 (Cross-PC Ball Handoff) 설계

> 사용자가 지정한 여러 PC를 **순서(엣지 매핑)**로 연결해, 슬라임(공)이 한 PC 화면 밖으로 나가면
> 다음 PC 화면 안으로 이어져 날아가게 한다. 멀티 모니터의 "경계 통과"를 **PC 경계까지 확장**한 것.

- **확정 범위**: 같은 **LAN**, **임의 엣지 매핑**(상/하/좌/우 자유 연결), 이번 문서는 **설계만**
- **핵심 원칙**: 공은 항상 **한 PC만 소유**(공 = 토큰). 소유 PC만 물리를 돌린다.
- **기존 구조 재사용**: 충돌은 이미 `IWalkableArea.IsRectValid()`에 위임 → "엣지가 네트워크로 연결됐는지" 아는 구현체로 교체하면 됨.

---

## 1. 개념 모델

| 용어 | 의미 |
|------|------|
| **Node** | 참여 PC 하나. 고유 `NodeId`(문자열)와 LAN IP를 가짐. |
| **Edge** | 한 PC의 **가상 데스크톱 바깥 경계** 방향: `Left/Right/Top/Bottom` (필요 시 세분 가능). |
| **EdgeLink** | `(fromNode, fromEdge) → (toNode, toEdge, flip)` 단방향 연결. 양방향은 링크 2개. |
| **Token(공 소유권)** | 동시에 공을 가진 노드는 정확히 하나. 핸드오프 = 토큰 이전. |
| **Chain** | EdgeLink 집합. 좌우 일렬뿐 아니라 고리형·격자형도 표현 가능. |

예시(임의 매핑):
```
PC-A.Right  → PC-B.Left
PC-B.Top    → PC-C.Bottom
PC-C.Left   → PC-A.Right(반대)   // 고리 구성도 가능
```

---

## 2. 아키텍처 — 신규 컴포넌트

```
App
 └─ SlimeWindow (렌더 루프: 소유 중일 때만 물리 tick)
      ├─ SlimePhysicsEngine        (기존, 상태 그대로 직렬화)
      ├─ NetworkedWalkableArea      ← IWalkableArea 새 구현 (로컬 모니터 + 엣지 링크 인지)
      └─ BallHandoffCoordinator     (핸드오프 판정·상태 패킹/언패킹·소유권)
 └─ NetworkService                  (TCP 연결·메시지 송수신·하트비트)
 └─ ClusterConfig                   (노드 목록·EdgeLink·좌표 매핑, JSON 저장)
```

- **NetworkService**: `System.Net.Sockets`만 사용(NuGet 없음). 각 노드에 리스너 1개 + 상대별 연결. JSON 라인 프로토콜.
- **BallHandoffCoordinator**: 물리 결과를 보고 "연결된 엣지를 넘었는가?" 판정 → 넘었으면 상태 패킹 후 `NetworkService`로 전송, ACK 후 로컬 공 제거.
- **NetworkedWalkableArea**: `IsRectValid`에서 **연결된 엣지 방향은 "밖으로 나가도 유효"처럼 취급하지 않고**, 대신 Coordinator가 "엣지 교차"를 감지하도록 경계 정보를 제공. (아래 5절)
- **ClusterConfig**: 사용자가 지정하는 PC/IP/엣지 매핑. 설정창(디자인 트랙)에서 편집, 파일 저장.

> 네트워크 코드는 순수 로컬 로직과 성격이 다르므로 **명확히 분리**한다. `SlimePhysicsEngine`은 네트워크를 전혀 모른다(현재 순수성 유지).

---

## 3. 기존 코드와의 접점 (변경 최소화)

| 기존 요소 | 접점/변경 |
|-----------|-----------|
| `IWalkableArea` | 그대로 사용. `MonitorLayoutService`는 로컬 판정 유지. |
| `SlimePhysicsEngine` | **변경 없음**. 상태(`Position/Velocity/AngularVelocity/SurfaceSpin/SpinShotDir/SpinAngle`)를 Coordinator가 읽고/쓴다. |
| `SlimeWindow.OnRendering` | tick 후 **Coordinator.CheckHandoff() 1줄 추가**. 소유하지 않을 때는 물리·렌더 중지(공이 다른 PC에 있음). |
| `AppSettings` | 네트워크 설정은 **별도 `ClusterConfig`**로 분리(엔진 튜닝값과 성격 다름). |

핸드오프 판정을 물리 안에 넣지 않고 **밖(Coordinator)**에서 하므로, 엔진은 계속 "로컬에서 벽에 튕기는" 순수 동작만 안다. 연결된 엣지는 Coordinator가 가로챈다.

---

## 4. 공 소유권(토큰) 모델

- **한 시점에 공은 한 노드**. 소유 노드만: 물리 tick, 슬라임 표시, 렌더 루프 가동.
- 비소유 노드: 슬라임 숨김, 렌더 루프 유휴(CPU 0), 리스너만 대기.
- 핸드오프 순서(안전한 토큰 이전):
  1. A: 공이 연결된 엣지를 넘음 감지 → 물리 정지, 슬라임 **숨김(잠금)**, `BALL_HANDOFF` 전송
  2. B: 수신 → 진입 엣지에서 공 생성 → 물리 시작 → `HANDOFF_ACK` 회신
  3. A: ACK 수신 → 로컬 공 완전 제거(소유권 해제)
  - ACK 타임아웃(예: 500ms) → **핸드오프 실패로 간주, A가 공을 되돌려 반사**(유실 방지)

이렇게 하면 공이 **복제되거나 사라지지 않는다**(정확히 하나 보장).

---

## 5. 엣지 교차 감지 & 좌표 정규화 (임의 매핑 핵심)

### 5.1 나가는 판정
- 매 tick 후 슬라임 사각형이 **연결된 엣지(EdgeLink 존재)** 바깥으로 나갔는지 확인.
- 연결이 **없는** 엣지는 기존대로 **반사**(로컬 `IWalkableArea`가 처리).
- 연결이 **있는** 엣지는 Coordinator가 가로채 핸드오프.

### 5.2 엣지 로컬 좌표 정규화 (해상도·DPI 무관하게)
각 엣지를 **파라미터 t∈[0,1]**로 표현:
- Left/Right 엣지: `t = (y - edgeTop) / edgeLength`
- Top/Bottom 엣지: `t = (x - edgeLeft) / edgeLength`

핸드오프 메시지에는 **절대 좌표 대신 t와 엣지 기준 속도**만 담는다 → 받는 PC가 자기 해상도로 역변환.

### 5.3 속도/스핀 변환 (엣지 방향 회전)
각 엣지는 **바깥 법선 n**과 **접선 τ**를 가진다. 나가는 속도를 (법선성분 vₙ, 접선성분 vτ)로 분해:
- 진입 엣지에서 새 속도 = `(-inNormal)*|vₙ|` + `inTangent*vτ`
  (법선은 항상 **안쪽**으로, 접선은 보존)
- `flip=true`면 `t → 1-t`, 접선 성분 부호 반전(거울 매핑).
- **각속도(AngularVelocity)**: 좌우/상하 거울이 홀수 번이면 부호 반전(스핀 휨 방향 유지). `SpinShotDir`도 동일 회전 적용.

이 변환으로 **A.Right → B.Top** 같은 90° 매핑에서도 공이 자연스럽게 꺾여 들어간다.

### 5.4 진입 위치 보정(지연 흡수)
네트워크 지연만큼 공백이 생기므로, 받는 PC는 진입 엣지에서 **살짝 안쪽(예: 슬라임 크기 × 0.5)** 지점에 생성해 "쑥 들어오는" 느낌으로 자연 흡수.

---

## 6. 핸드오프 메시지 스키마 (JSON, TCP 라인 단위)

```jsonc
// BALL_HANDOFF : A → B (공 넘김)
{
  "type": "BALL_HANDOFF",
  "handoffId": "guid",          // ACK 매칭용
  "fromNode": "PC-A",
  "toNode": "PC-B",
  "viaLink": "A.Right->B.Left",  // 엣지 링크 식별
  "edgeParam": 0.37,             // 진입 엣지 t (0~1)
  "normalSpeed": 1820.5,         // 엣지 법선 성분 (px/s, 항상 양수=안쪽)
  "tangentSpeed": -240.0,        // 엣지 접선 성분 (부호=방향)
  "angularVelocity": 320.0,      // deg/s (필요 시 부호 변환 후)
  "surfaceSpin": -150.0,         // px/s
  "surfaceSpinAxisDeg": 12.0,    // SpinShotDir 각도(엣지 기준)
  "spinAngle": 47.0,             // 시각 회전 연속성
  "sentAtMs": 173... ,           // 송신 시각(지연 추정용, 선택)
  "seq": 128                     // 순서/중복 방지
}

// HANDOFF_ACK : B → A
{ "type": "HANDOFF_ACK", "handoffId": "guid", "accepted": true }

// HEARTBEAT : 상호 생존 확인(1~2s 주기)
{ "type": "HEARTBEAT", "node": "PC-B", "hasBall": true }

// HELLO : 연결 수립 시 신원 교환
{ "type": "HELLO", "node": "PC-B", "version": "1.0.1" }
```

- **좌표를 절대값으로 보내지 않음**(해상도/DPI 차이 무관).
- `seq`/`handoffId`로 중복·유실 방지.

---

## 7. 네트워크 방식 (LAN 확정)

- **전송**: 노드 간 **TCP** 상시 연결(작은 메시지, 순서·신뢰성 보장). 핸드오프 빈도 낮아 성능 무관.
- **주소 지정**: 사용자가 **IP로 직접 지정**(요청하신 "내가 지정하는 PC"). 포트 1개 고정(예: 45123).
- **연결 토폴로지**: 각 노드가 리스너 + EdgeLink 상대에게 아웃바운드 연결(양방향 연결 재사용).
- **직렬화**: `System.Text.Json`(설정 저장과 동일 스택). 개행 구분 라인 프로토콜.
- **방화벽**: 인바운드 예외 1개(포트) 필요 — 설치/최초 실행 시 안내.
- (선택) **자동 발견**: UDP 브로드캐스트로 같은 LAN 노드 자동 검색 → MVP는 수동 IP, 이후 확장.

---

## 8. 폴백 / 에러 처리 (필수 안전장치)

| 상황 | 처리 |
|------|------|
| 대상 노드 연결 없음/꺼짐 | 그 엣지는 **일반 벽으로 폴백 → 반사**. 공 유실 없음. |
| `HANDOFF_ACK` 타임아웃 | A가 공을 **되돌려 반사**(넘기기 취소). |
| 하트비트 끊김 | 링크 비활성화 표시 → 반사 폴백. 재연결 시 자동 복구. |
| 중복 수신(seq 재도착) | 무시(idempotent). |
| 버전 불일치(HELLO) | 경고 로그 + 연결 유지 시도 or 거부(정책 선택). |

모든 네트워크 예외는 기존 `Logger`로 기록하고 앱은 죽지 않는다(로컬 토이로 계속 동작).

---

## 9. 설정 스키마 (ClusterConfig, 별도 JSON)

`%APPDATA%/ThrowMe/cluster.json`
```jsonc
{
  "selfNodeId": "PC-A",
  "listenPort": 45123,
  "nodes": [
    { "id": "PC-A", "host": "192.168.0.11" },
    { "id": "PC-B", "host": "192.168.0.12" },
    { "id": "PC-C", "host": "192.168.0.13" }
  ],
  "links": [
    { "from": "PC-A", "fromEdge": "Right", "to": "PC-B", "toEdge": "Left",  "flip": false },
    { "from": "PC-B", "fromEdge": "Left",  "to": "PC-A", "toEdge": "Right", "flip": false },
    { "from": "PC-B", "fromEdge": "Top",   "to": "PC-C", "toEdge": "Bottom","flip": false },
    { "from": "PC-C", "fromEdge": "Bottom","to": "PC-B", "toEdge": "Top",   "flip": false }
  ],
  "enabled": true
}
```
- `selfNodeId`만 PC마다 다르게 두면 **같은 파일을 복사**해 쓸 수 있음(편의).
- 엔진 튜닝값(`AppSettings`)과 **분리** → 네트워크 설정 변경이 물리 저장과 섞이지 않음.

---

## 10. 신규 / 수정 파일 목록

**신규**
- `Network/ClusterConfig.cs` — 노드·링크·자기 식별, 로드/저장
- `Network/EdgeLink.cs` + `Edge.cs` — 엣지 방향/법선/접선, 좌표 정규화·역변환
- `Network/NetworkService.cs` — TCP 리스너/클라이언트, 메시지 송수신, 하트비트
- `Network/HandoffMessages.cs` — 메시지 DTO(직렬화 대상)
- `Network/BallHandoffCoordinator.cs` — 교차 감지·패킹/언패킹·소유권·ACK/타임아웃
- `Physics/NetworkedWalkableArea.cs` — 로컬 모니터 + "연결된 엣지" 인지 (또는 Coordinator가 로컬 area를 감싸는 방식)
- (설정 UI: `Views/ClusterSettings*` — **디자인 트랙**)

**수정(최소)**
- `SlimeWindow.xaml.cs` — 렌더 루프에 `Coordinator.CheckHandoff()` 연결, 비소유 시 유휴
- `App.xaml.cs` — `ClusterConfig` 로드, `NetworkService`/`Coordinator` 생성·해제

**변경 없음**: `SlimePhysicsEngine`(순수 유지), `MonitorLayoutService`, `AudioService`, `ParticleSystem`

---

## 11. 구현 단계 (Phase 6 제안)

1. **6-A 배관**: `ClusterConfig` + `NetworkService`(HELLO/HEARTBEAT)로 2노드 TCP 연결·생존 확인
2. **6-B 최소 핸드오프**: A.Right→B.Left 한 방향, 위치 t + 속도만 전달(스핀 제외) — "실제로 넘어가는가" 검증
3. **6-C 양방향 + 스핀/각속도 변환** + 진입 보정
4. **6-D 임의 엣지 매핑**(90° 회전 포함) + flip
5. **6-E 폴백/타임아웃/재연결** 안정화
6. **6-F 설정 UI**(디자인 트랙) + 자동 발견(선택)

각 단계마다 빌드·실행 유지.

---

## 12. 테스트 체크리스트

- [ ] 2대: A 오른쪽으로 던지면 B 왼쪽에서 **같은 높이 비율**로 진입, 속도 이어짐
- [ ] 되돌아오기: B에서 A로 다시 넘어옴(양방향)
- [ ] 3대 체인: A→B→C 순서 이동
- [ ] 임의 매핑: A.Right→B.Top(90°)에서 자연스럽게 꺾여 진입
- [ ] flip 매핑: 진입 위치가 거울(1-t)로 반전
- [ ] 스핀 유지: 사이드 스핀/끌어치기 상태가 넘어가서도 이어짐
- [ ] 소유권: 어느 순간에도 공은 **정확히 한 대**에만 존재(복제·유실 없음)
- [ ] 폴백: 대상 PC 꺼짐 → 그 엣지에서 **반사**(공 안 잃음)
- [ ] ACK 타임아웃 → 공 되돌림
- [ ] 비소유 PC: 슬라임 숨김 + **CPU ≈ 0**
- [ ] 해상도/DPI 다른 두 PC 간에도 진입 비율·크기 일관

---

## 13. 보안 / 개인정보 메모
- LAN 내부 통신, 전송 데이터는 **공의 물리 상태(좌표 비율·속도·스핀)뿐** — 개인정보 없음.
- IP는 사용자가 직접 지정한 자신의 기기. 외부 노출 없음(인터넷 미사용).
- (강화 옵션) 노드 간 공유 토큰/간단 인증으로 임의 기기의 공 주입 방지 — 필요 시 6-E에 추가.

---

## 14. 결론
- **가능하며, 현재 아키텍처와 잘 맞는다.** 핵심은 "연결된 엣지에서 반사 대신 네트워크 핸드오프".
- 엔진은 그대로 두고 **Coordinator + NetworkService**만 추가하는 저침습 설계.
- 가장 신경 쓸 지점: **좌표 정규화(해상도/DPI 무관)**, **단일 토큰(복제·유실 방지)**, **폴백(끊김 시 반사)**.
- 다음 실행 시 **Phase 6-A(배관) + 6-B(최소 핸드오프)**부터 구현 권장.
