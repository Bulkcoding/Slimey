// Slimey 릴레이 프로토콜 — 클라이언트/서버 공용 메시지 정의.
// 설계문서 `Slimey_멀티PC_인터넷_릴레이_설계.md` 9절과 일치.
//
// 모든 메시지는 봉투(Envelope)로 감싸 서버가 라우팅한다.
// 좌표는 절대값을 보내지 않고 엣지 파라미터 t + 엣지 기준 속도만 담는다(해상도/DPI 무관).

export const PROTOCOL_VERSION = 1;

export type MessageType =
  | "HELLO"          // client → server : 방 참여(코드+시크릿) 인증
  | "WELCOME"        // server → client : 인증 성공(내 nodeId·현재 owner·링크)
  | "PRESENCE"       // server → 방 전체 : 온라인 노드 목록 + owner
  | "ROOM_CONFIG"    // client → server(설정) / server → 방 전체(배포) : 엣지 매핑
  | "HANDOFF"        // owner → server → target : 공 넘김
  | "ACK"            // target → server : 공 수락
  | "HANDOFF_RESULT" // server → origin : 최종 결과(accepted → 해제 / 거부 → 반사)
  | "HEARTBEAT"      // client → server : 생존 확인
  | "ERROR";         // server → client : 오류 고지

export interface Envelope<T = unknown> {
  v: number;               // 프로토콜 버전
  type: MessageType;
  roomId?: string;
  from?: string;           // 보낸 노드 id
  to?: string;             // 서버 라우팅 대상 노드 id (없으면 브로드캐스트)
  seq?: number;            // 순서/중복 방지
  sig?: string;            // (예약) 메시지 서명 — Phase 7-G 강화 항목
  data?: T;
}

// ── 타입별 페이로드 ────────────────────────────────────────────

export interface HelloData {
  secret: string;          // 방 시크릿(최초 참여 시 방 생성·시크릿 등록)
  version: string;         // 클라이언트 버전(HELLO 신원 교환)
}

export interface WelcomeData {
  nodeId: string;          // 서버가 확정한 내 노드 id
  owner: string | null;    // 현재 공 소유 노드
  links: EdgeLink[];       // 엣지 매핑
  nodes: NodePresence[];   // 현재 온라인 노드
}

export interface NodePresence {
  nodeId: string;
  online: boolean;
  hasBall: boolean;
}

export interface PresenceData {
  nodes: NodePresence[];
  owner: string | null;
}

export type Edge = "Left" | "Right" | "Top" | "Bottom";

export interface EdgeLink {
  from: string;            // nodeId
  fromEdge: Edge;
  to: string;              // nodeId
  toEdge: Edge;
  flip: boolean;           // true면 진입 t → 1-t, 접선 부호 반전(거울)
}

export interface RoomConfigData {
  links: EdgeLink[];
}

// 설계문서 9절 HANDOFF.data — LAN 설계 6절 필드 그대로 재사용.
export interface HandoffData {
  handoffId: string;       // ACK 매칭용
  viaLink: string;         // "A.Right->B.Left" 링크 식별
  edgeParam: number;       // 진입 엣지 t (0~1)
  normalSpeed: number;     // 엣지 법선 성분(px/s, 항상 양수=안쪽)
  tangentSpeed: number;    // 접선 성분(부호=방향)
  angularVelocity: number; // deg/s
  surfaceSpin: number;     // px/s (끌어치기/밀어치기)
  surfaceSpinAxisDeg: number; // SpinShotDir 각도(엣지 기준)
  spinAngle: number;       // 시각 회전 연속성
}

export interface AckData {
  handoffId: string;
  accepted: boolean;
}

export interface HandoffResultData {
  handoffId: string;
  accepted: boolean;       // true=상대가 받음(공 해제) / false=실패(반사로 회수)
  reason?: string;
}

export interface ErrorData {
  code: string;
  message: string;
}

// ── 오류 코드 ─────────────────────────────────────────────────
export const ErrorCodes = {
  BAD_MESSAGE: "BAD_MESSAGE",
  NOT_AUTHENTICATED: "NOT_AUTHENTICATED",
  BAD_SECRET: "BAD_SECRET",
  ROOM_FULL: "ROOM_FULL",
  NOT_OWNER: "NOT_OWNER",
  TARGET_OFFLINE: "TARGET_OFFLINE",
  VERSION_MISMATCH: "VERSION_MISMATCH",
} as const;

// 방 한도(남용 방지). 개인/소규모용 기본값.
export const MAX_NODES_PER_ROOM = 16;

// 핸드오프 ACK 타임아웃(ms). 인터넷 지연 고려. 초과 시 owner 롤백 → origin이 반사.
export const HANDOFF_TIMEOUT_MS = 1500;

// 하트비트 없이 이 시간(ms) 지나면 죽은 연결로 간주(참고용).
export const HEARTBEAT_TIMEOUT_MS = 45_000;
