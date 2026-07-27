// Slimey 릴레이 — Room Durable Object.
//
// 방 하나 = DO 인스턴스 하나(전 세계 단일). DO는 단일 스레드로 이벤트를 하나씩
// 처리하므로 락 없이 소유권 경합이 원천적으로 안전하다("공은 정확히 한 대").
//
// 책임:
//   · 방 참여 인증(방 코드는 라우팅, 시크릿은 여기서 검증 — 최초 참여자가 시크릿 등록)
//   · 프레즌스(온라인 노드 목록) — 활성 WebSocket 에서 파생(Hibernation 안전)
//   · 공 소유권(owner) 권위 보관 + 핸드오프 중재(ACK/타임아웃 롤백)
//   · 엣지 매핑(links) 보관·배포
//   · 메시지 라우팅(대상 지정 전달 / 브로드캐스트)
//
// 상태 지속: owner/secretHash/links/pending 은 storage 에 저장(Hibernation 후에도 유지).
// 프레즌스는 ctx.getWebSockets() 로 매번 파생(재우고 깨워도 살아있는 소켓 목록).

import { DurableObject } from "cloudflare:workers";
import type { Env } from "./index";
import {
  PROTOCOL_VERSION,
  ErrorCodes,
  MAX_NODES_PER_ROOM,
  HANDOFF_TIMEOUT_MS,
  EMPTY_ROOM_TTL_MS,
  type Envelope,
  type EdgeLink,
  type HelloData,
  type HandoffData,
  type AckData,
  type NodePresence,
} from "./protocol";

interface PendingHandoff {
  handoffId: string;
  from: string;      // 넘기는(현 owner) 노드
  to: string;        // 받을 노드
  expiresAt: number; // epoch ms
}

// WebSocket 에 붙는 노드 신원(Hibernation 안전하게 serializeAttachment 로 저장).
interface SocketMeta {
  nodeId: string;
  version: string;
}

export class RoomDurableObject extends DurableObject<Env> {
  private owner: string | null = null;
  private secretHash: string | null = null;
  private links: EdgeLink[] = [];
  private pending: PendingHandoff | null = null;
  private seq = 0;
  /** 방장 = 방을 처음 만든(최초 참여) 노드. 배치(순서) 결정 권한을 가진다. */
  private host: string | null = null;
  /** 파티 순서(좌 → 우). 방장이 정한다. 참여 순서대로 뒤에 붙는다. */
  private order: string[] = [];
  /** 빈 방 폐기 예정 시각(epoch ms). null = 폐기 예약 없음(누군가 접속 중). */
  private disposeAt: number | null = null;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    // 최초 이벤트 처리 전에 지속 상태를 메모리로 로드.
    ctx.blockConcurrencyWhile(async () => {
      this.owner = (await ctx.storage.get<string | null>("owner")) ?? null;
      this.secretHash = (await ctx.storage.get<string | null>("secretHash")) ?? null;
      this.links = (await ctx.storage.get<EdgeLink[]>("links")) ?? [];
      this.pending = (await ctx.storage.get<PendingHandoff | null>("pending")) ?? null;
      this.seq = (await ctx.storage.get<number>("seq")) ?? 0;
      this.host = (await ctx.storage.get<string | null>("host")) ?? null;
      this.order = (await ctx.storage.get<string[]>("order")) ?? [];
      this.disposeAt = (await ctx.storage.get<number | null>("disposeAt")) ?? null;
    });
  }

  // ── WebSocket 업그레이드 ─────────────────────────────────────
  async fetch(_request: Request): Promise<Response> {
    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];
    // Hibernation 대응: 표준 addEventListener 대신 acceptWebSocket 사용.
    this.ctx.acceptWebSocket(server);
    return new Response(null, { status: 101, webSocket: client });
  }

  // ── 메시지 수신 ──────────────────────────────────────────────
  async webSocketMessage(ws: WebSocket, message: string | ArrayBuffer): Promise<void> {
    let env: Envelope;
    try {
      env = JSON.parse(typeof message === "string" ? message : new TextDecoder().decode(message));
    } catch {
      return this.sendError(ws, ErrorCodes.BAD_MESSAGE, "invalid JSON");
    }

    const meta = this.metaOf(ws);

    // 인증 전에는 HELLO 만 허용.
    if (!meta) {
      if (env.type !== "HELLO") {
        return this.sendError(ws, ErrorCodes.NOT_AUTHENTICATED, "send HELLO first");
      }
      return this.handleHello(ws, env as Envelope<HelloData>);
    }

    switch (env.type) {
      case "HEARTBEAT":
        return this.send(ws, { v: PROTOCOL_VERSION, type: "HEARTBEAT" });
      case "HANDOFF":
        return this.handleHandoff(meta, env as Envelope<HandoffData>);
      case "ACK":
        return this.handleAck(meta, env as Envelope<AckData>);
      case "ROOM_CONFIG":
        return this.handleRoomConfig(meta, env as Envelope<{ links: EdgeLink[] }>);
      case "SET_ORDER":
        return this.handleSetOrder(meta, env as Envelope<{ order: string[] }>);
      default:
        return this.sendError(ws, ErrorCodes.BAD_MESSAGE, `unknown type ${env.type}`);
    }
  }

  async webSocketClose(ws: WebSocket): Promise<void> {
    await this.onDisconnect(ws);
  }

  async webSocketError(ws: WebSocket): Promise<void> {
    await this.onDisconnect(ws);
  }

  // ── HELLO: 인증 & 방 참여 ────────────────────────────────────
  private async handleHello(ws: WebSocket, env: Envelope<HelloData>): Promise<void> {
    const nodeId = (env.from ?? "").trim();
    const secret = env.data?.secret ?? "";
    const version = env.data?.version ?? "?";

    if (!nodeId || !secret) {
      return this.closeWith(ws, ErrorCodes.BAD_MESSAGE, "nodeId(from)/secret required");
    }

    const incomingHash = await sha256(secret);
    if (this.secretHash === null) {
      // 최초 참여자 = 방 생성. 시크릿 해시 등록.
      this.secretHash = incomingHash;
      await this.ctx.storage.put("secretHash", incomingHash);
    } else if (this.secretHash !== incomingHash) {
      return this.closeWith(ws, ErrorCodes.BAD_SECRET, "room secret mismatch");
    }

    // 정원 초과 방지.
    const active = this.authenticatedSockets();
    if (active.length >= MAX_NODES_PER_ROOM && !active.some((s) => this.metaOf(s)?.nodeId === nodeId)) {
      return this.closeWith(ws, ErrorCodes.ROOM_FULL, `room full (max ${MAX_NODES_PER_ROOM})`);
    }

    // 같은 nodeId 재접속 → 이전 소켓 정리(중복 방지).
    for (const s of active) {
      if (this.metaOf(s)?.nodeId === nodeId && s !== ws) {
        try { s.close(1000, "replaced by new connection"); } catch { /* ignore */ }
      }
    }

    ws.serializeAttachment({ nodeId, version } satisfies SocketMeta);

    // 누군가 들어왔으니 빈 방 폐기 예약을 취소한다.
    if (this.disposeAt !== null) {
      this.disposeAt = null;
      await this.ctx.storage.delete("disposeAt");
      await this.rescheduleAlarm();
    }

    // 소유자가 아직 없으면 최초 인증 노드가 공을 가진다.
    if (this.owner === null) {
      this.owner = nodeId;
      await this.ctx.storage.put("owner", this.owner);
    }

    // 방장이 없으면 최초 참여자(= 방을 만든 사람)가 방장.
    if (this.host === null) {
      this.host = nodeId;
      await this.ctx.storage.put("host", this.host);
    }

    // 파티 순서에 없으면 맨 뒤(가장 오른쪽)에 추가.
    if (!this.order.includes(nodeId)) {
      this.order = [...this.order, nodeId];
      await this.ctx.storage.put("order", this.order);
    }

    // WELCOME(개별) + PRESENCE(전체) 통지.
    this.send(ws, {
      v: PROTOCOL_VERSION,
      type: "WELCOME",
      roomId: env.roomId,
      to: nodeId,
      data: {
        nodeId, owner: this.owner, links: this.links, nodes: this.presenceNodes(),
        host: this.host, order: this.order,
      },
    });
    this.broadcastPresence();
  }

  // ── HANDOFF: 공 넘김(owner만) ────────────────────────────────
  private async handleHandoff(meta: SocketMeta, env: Envelope<HandoffData>): Promise<void> {
    const from = meta.nodeId;
    const to = (env.to ?? "").trim();
    const data = env.data;
    if (!data || !to) return;

    // 권위 검증: 현재 owner 만 넘길 수 있다(공 주입/복제 방지).
    if (this.owner !== from) {
      return this.sendToNode(from, {
        v: PROTOCOL_VERSION, type: "ERROR",
        data: { code: ErrorCodes.NOT_OWNER, message: "not current ball owner" },
      });
    }

    // 대상이 온라인인가.
    const target = this.socketOfNode(to);
    if (!target) {
      // 오프라인 → origin이 반사하도록 실패 통지(공 유실 없음).
      return this.sendToNode(from, {
        v: PROTOCOL_VERSION, type: "HANDOFF_RESULT",
        data: { handoffId: data.handoffId, accepted: false, reason: ErrorCodes.TARGET_OFFLINE },
      });
    }

    // in-transit 기록 + 타임아웃 알람. owner 는 ACK 전까지 from 유지(롤백 대비).
    this.pending = { handoffId: data.handoffId, from, to, expiresAt: Date.now() + HANDOFF_TIMEOUT_MS };
    await this.ctx.storage.put("pending", this.pending);
    await this.rescheduleAlarm();

    // 대상에게 핸드오프 전달.
    this.send(target, {
      v: PROTOCOL_VERSION, type: "HANDOFF",
      roomId: env.roomId, from, to, seq: this.nextSeq(), data,
    });
  }

  // ── ACK: 대상이 공 수락 ──────────────────────────────────────
  private async handleAck(meta: SocketMeta, env: Envelope<AckData>): Promise<void> {
    const data = env.data;
    if (!data || !this.pending) return;
    if (data.handoffId !== this.pending.handoffId) return;      // 오래된/무관 ACK 무시
    if (meta.nodeId !== this.pending.to) return;                // 대상만 유효

    const from = this.pending.from;
    if (data.accepted) {
      // 소유권 커밋(원자적: DO 단일 스레드).
      this.owner = this.pending.to;
      await this.ctx.storage.put("owner", this.owner);
      this.clearPending();
      // origin 에게 "받았음 → 공 해제" 통지 + 전체 프레즌스 갱신.
      this.sendToNode(from, {
        v: PROTOCOL_VERSION, type: "HANDOFF_RESULT",
        data: { handoffId: data.handoffId, accepted: true },
      });
      this.broadcastPresence();
    } else {
      // 대상이 거부 → 롤백(owner 그대로 from), origin 반사.
      this.clearPending();
      this.sendToNode(from, {
        v: PROTOCOL_VERSION, type: "HANDOFF_RESULT",
        data: { handoffId: data.handoffId, accepted: false, reason: "rejected" },
      });
    }
  }

  // ── ROOM_CONFIG: 엣지 매핑 설정·배포(방장만) ────────────────
  private async handleRoomConfig(meta: SocketMeta, env: Envelope<{ links: EdgeLink[] }>): Promise<void> {
    const links = env.data?.links;
    if (!Array.isArray(links)) return;

    // 배치는 방장이 결정한다(다른 사람이 임의로 바꾸지 못하게).
    if (this.host !== null && this.host !== meta.nodeId) {
      return this.sendToNode(meta.nodeId, {
        v: PROTOCOL_VERSION, type: "ERROR",
        data: { code: ErrorCodes.NOT_HOST, message: "only host can change layout" },
      });
    }

    this.links = links;
    await this.ctx.storage.put("links", links);
    this.broadcast({ v: PROTOCOL_VERSION, type: "ROOM_CONFIG", data: { links } });
  }

  // ── SET_ORDER: 파티 순서(좌→우) 변경(방장만) ────────────────
  private async handleSetOrder(meta: SocketMeta, env: Envelope<{ order: string[] }>): Promise<void> {
    const incoming = env.data?.order;
    if (!Array.isArray(incoming)) return;

    if (this.host !== meta.nodeId) {
      return this.sendToNode(meta.nodeId, {
        v: PROTOCOL_VERSION, type: "ERROR",
        data: { code: ErrorCodes.NOT_HOST, message: "only host can change party order" },
      });
    }

    // 알려진 노드만, 중복 없이 수용. 빠진 기존 노드는 뒤에 붙여 유실 방지.
    const known = new Set(this.order);
    for (const n of this.presenceNodes()) known.add(n.nodeId);

    const seen = new Set<string>();
    const next: string[] = [];
    for (const id of incoming) {
      const t = (id ?? "").trim();
      if (t && known.has(t) && !seen.has(t)) { seen.add(t); next.push(t); }
    }
    for (const id of known) if (!seen.has(id)) next.push(id);

    this.order = next;
    await this.ctx.storage.put("order", this.order);
    this.broadcastPresence();
  }

  // ── 알람: 핸드오프 ACK 타임아웃 + 빈 방 폐기 ────────────────
  async alarm(): Promise<void> {
    const now = Date.now();

    // 1) 핸드오프 ACK 타임아웃 → owner 롤백(origin 이 반사).
    if (this.pending && now >= this.pending.expiresAt) {
      const from = this.pending.from;
      const handoffId = this.pending.handoffId;
      this.clearPending();
      this.sendToNode(from, {
        v: PROTOCOL_VERSION, type: "HANDOFF_RESULT",
        data: { handoffId, accepted: false, reason: "timeout" },
      });
    }

    // 2) 빈 방 폐기. 그 사이 누가 들어왔으면 예약을 취소한다.
    if (this.disposeAt !== null && now >= this.disposeAt) {
      if (this.authenticatedSockets().length === 0) {
        await this.disposeRoom();
        return; // 전부 지웠으므로 재예약 불필요
      }
      this.disposeAt = null;
      await this.ctx.storage.delete("disposeAt");
    }

    // 남은 마감시각이 있으면 다시 예약.
    await this.rescheduleAlarm();
  }

  // ── 연결 해제 처리 ───────────────────────────────────────────
  private async onDisconnect(ws: WebSocket): Promise<void> {
    const meta = this.metaOf(ws);
    if (!meta) return;
    const gone = meta.nodeId;

    // 소유자가 나가면 남은 노드 중 하나로 이양(공 유실 방지). 없으면 null.
    if (this.owner === gone) {
      const remaining = this.presenceNodes().filter((n) => n.nodeId !== gone);
      this.owner = remaining.length > 0 ? remaining[0].nodeId : null;
      await this.ctx.storage.put("owner", this.owner);
    }
    // 방장이 나가면 남아 있는 노드 중 파티 순서상 가장 앞선 노드로 승계.
    // (방장이 없으면 아무도 배치를 바꿀 수 없게 되므로 반드시 넘긴다.)
    if (this.host === gone) {
      const online = new Set(this.presenceNodes().map((n) => n.nodeId).filter((id) => id !== gone));
      const next = this.order.find((id) => online.has(id)) ?? [...online][0] ?? null;
      this.host = next;
      await this.ctx.storage.put("host", this.host);
    }

    // 진행 중 핸드오프의 당사자가 사라지면 정리.
    if (this.pending && (this.pending.from === gone || this.pending.to === gone)) {
      this.clearPending();
    }

    // 마지막 한 명까지 나갔으면 일정 시간 뒤 방을 폐기하도록 예약한다.
    // (닫히는 소켓이 아직 목록에 남아 있을 수 있으므로 소켓 동일성으로 제외한다.)
    const others = this.authenticatedSockets().filter((s) => s !== ws);
    if (others.length === 0) {
      this.disposeAt = Date.now() + EMPTY_ROOM_TTL_MS;
      await this.ctx.storage.put("disposeAt", this.disposeAt);
      await this.rescheduleAlarm();
    }

    this.broadcastPresence();
  }

  /** 방 폐기: 저장 상태(시크릿·순서·배치·소유권)를 모두 지우고 초기 상태로 되돌린다. */
  private async disposeRoom(): Promise<void> {
    await this.ctx.storage.deleteAll();
    await this.ctx.storage.deleteAlarm();
    // 인스턴스가 메모리에 남아 있을 수 있으므로 메모리 상태도 초기화.
    this.owner = null;
    this.secretHash = null;
    this.links = [];
    this.pending = null;
    this.host = null;
    this.order = [];
    this.disposeAt = null;
    this.seq = 0;
  }

  /**
   * 알람은 DO 당 하나뿐이라 "핸드오프 타임아웃"과 "빈 방 폐기" 두 마감시각을 함께 관리한다.
   * 더 이른 쪽으로 예약하고, 둘 다 없으면 알람을 지운다.
   */
  private async rescheduleAlarm(): Promise<void> {
    const deadlines: number[] = [];
    if (this.pending) deadlines.push(this.pending.expiresAt);
    if (this.disposeAt !== null) deadlines.push(this.disposeAt);

    if (deadlines.length === 0) {
      await this.ctx.storage.deleteAlarm();
      return;
    }
    await this.ctx.storage.setAlarm(Math.min(...deadlines));
  }

  // ── 유틸 ─────────────────────────────────────────────────────
  private metaOf(ws: WebSocket): SocketMeta | null {
    const a = ws.deserializeAttachment();
    return a && typeof a === "object" && "nodeId" in a ? (a as SocketMeta) : null;
  }

  private authenticatedSockets(): WebSocket[] {
    return this.ctx.getWebSockets().filter((s) => this.metaOf(s) !== null);
  }

  private socketOfNode(nodeId: string): WebSocket | null {
    for (const s of this.authenticatedSockets()) {
      if (this.metaOf(s)?.nodeId === nodeId) return s;
    }
    return null;
  }

  private presenceNodes(): NodePresence[] {
    const seen = new Set<string>();
    const nodes: NodePresence[] = [];
    for (const s of this.authenticatedSockets()) {
      const id = this.metaOf(s)!.nodeId;
      if (seen.has(id)) continue;
      seen.add(id);
      nodes.push({ nodeId: id, online: true, hasBall: this.owner === id });
    }
    return nodes;
  }

  private broadcastPresence(): void {
    this.broadcast({
      v: PROTOCOL_VERSION, type: "PRESENCE",
      data: { nodes: this.presenceNodes(), owner: this.owner, host: this.host, order: this.order },
    });
  }

  private broadcast(obj: Envelope): void {
    const msg = JSON.stringify(obj);
    for (const s of this.authenticatedSockets()) {
      try { s.send(msg); } catch { /* ignore broken socket */ }
    }
  }

  private sendToNode(nodeId: string, obj: Envelope): void {
    const s = this.socketOfNode(nodeId);
    if (s) this.send(s, obj);
  }

  private send(ws: WebSocket, obj: Envelope): void {
    try { ws.send(JSON.stringify(obj)); } catch { /* ignore */ }
  }

  private sendError(ws: WebSocket, code: string, message: string): void {
    this.send(ws, { v: PROTOCOL_VERSION, type: "ERROR", data: { code, message } });
  }

  private closeWith(ws: WebSocket, code: string, message: string): void {
    this.sendError(ws, code, message);
    try { ws.close(1008, message); } catch { /* ignore */ }
  }

  private clearPending(): void {
    this.pending = null;
    void this.ctx.storage.delete("pending");
    // 알람을 그냥 지우면 빈 방 폐기 예약까지 취소되므로 재계산한다.
    void this.rescheduleAlarm();
  }

  private nextSeq(): number {
    this.seq += 1;
    void this.ctx.storage.put("seq", this.seq);
    return this.seq;
  }
}

// SHA-256 hex (Web Crypto — Workers 내장).
async function sha256(text: string): Promise<string> {
  const buf = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(buf)].map((b) => b.toString(16).padStart(2, "0")).join("");
}
