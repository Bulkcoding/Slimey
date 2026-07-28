# ThrowMe 릴레이 서버 (Cloudflare Workers + Durable Objects)

ThrowMe 멀티 PC 인터넷 확장(B안)의 **방 코드 기반 릴레이 서버**.
집↔집·집↔사무실처럼 서로 다른 네트워크의 PC들을 **로그인(방 코드+시크릿)만으로** 묶어
공(슬라임)을 인터넷 너머로 주고받게 한다.

설계: 리포지토리 루트의 `ThrowMe_멀티PC_인터넷_릴레이_설계.md`

## 핵심 개념

- **방 하나 = Durable Object 하나**(전 세계 단일 인스턴스). DO는 단일 스레드라 락 없이
  소유권 경합이 안전하다 → **공은 항상 정확히 한 대만 소유**.
- 각 PC는 서버로 **아웃바운드 WSS** 연결 → NAT/방화벽 무관(포트포워딩 불필요).
- **방 코드**는 라우팅 키(`/room/<CODE>`), **시크릿**은 참여 인증(최초 참여자가 등록,
  이후 참여자는 일치해야 함). 서버는 시크릿의 **SHA-256 해시만** 저장.

## 요구 사항

- Node.js 18+ (개발용), Cloudflare 계정(배포용)

## 로컬 실행

```bash
npm install
npm run dev          # ws://127.0.0.1:8787/room/<CODE>
```

## 배포 (무료 티어로 0원 시작)

```bash
npm install
npx wrangler login   # 브라우저로 Cloudflare 로그인
npm run deploy       # 배포 → https://slimey-relay.<계정>.workers.dev 발급
```

- SQLite 백엔드 DO(`wrangler.toml` 의 `new_sqlite_classes`) → **무료 티어 사용 가능**.
  ※ 무료 조건은 시점에 따라 바뀔 수 있으니 배포 직전 Cloudflare Workers/DO 요금 페이지 확인 권장.
- WSS·TLS·도메인(`*.workers.dev`)은 Cloudflare가 자동 제공(별도 설정 0).

## 클라이언트(ThrowMe 앱) 연결값

배포 후 나온 주소를 각 PC의 ThrowMe 설정에 입력한다(설정 UI는 Phase 7-H, 그전엔 `%LOCALAPPDATA%\ThrowMe\relay.json`):

| 항목 | 예시 | 설명 |
|------|------|------|
| ServerBaseUrl | `wss://slimey-relay.내계정.workers.dev` | 배포 주소(http(s):// 넣어도 ws(s)://로 보정) |
| RoomCode | `THROWME-A3F9` | 같이 묶을 PC들에 동일 입력(대문자/숫자/하이픈, 3~64자) |
| Secret | (임의 비밀번호) | 같은 방 PC들에 동일 입력. 최초 참여 PC가 이 값으로 방 생성 |
| NodeId | `집-데스크톱` | 이 PC 이름(방 안에서 고유). 기본=컴퓨터 이름 |

## 프로토콜

`src/protocol.ts` 참조. 봉투(Envelope) + 타입별 페이로드(HELLO/WELCOME/PRESENCE/
ROOM_CONFIG/HANDOFF/ACK/HANDOFF_RESULT/HEARTBEAT/ERROR). C# 클라이언트의
`src/ThrowMe/Network/RelayMessages.cs` 와 1:1 대응.

## 파일

| 파일 | 역할 |
|------|------|
| `src/index.ts` | Worker 진입점. `/room/<CODE>` → 해당 Room DO로 라우팅 |
| `src/room.ts` | Room Durable Object. 인증·프레즌스·소유권 중재·핸드오프 라우팅·타임아웃 알람 |
| `src/protocol.ts` | 메시지 타입/상수(클라이언트 공용 계약) |
| `wrangler.toml` | 배포 설정(DO 바인딩·SQLite 마이그레이션) |

## 안전장치(검증됨)

- 최초 참여자가 시크릿 등록, 이후 불일치 시 `BAD_SECRET` 거부
- 현재 owner 만 핸드오프 시작 가능(`NOT_OWNER`) → 공 주입/복제 방지
- 대상 오프라인 → `TARGET_OFFLINE` 통지 → 클라이언트가 반사(공 유실 없음)
- ACK 타임아웃(기본 1.5s) → owner 롤백 → origin 반사
- 소유자 이탈 → 남은 노드로 소유권 이양(공 유실 없음)
