// ThrowMe 릴레이 서버 — Worker 진입점.
// 역할: WSS 업그레이드 요청의 방 코드를 파싱해 해당 Room Durable Object로 라우팅.
//
//   wss://<host>/room/<ROOM_CODE>   → RoomDurableObject(idFromName(ROOM_CODE))
//
// 인증(시크릿 검증)·프레즌스·소유권 중재는 모두 DO 안에서 처리한다.

import { RoomDurableObject } from "./room";

export interface Env {
  ROOMS: DurableObjectNamespace;
}

export { RoomDurableObject };

// 방 코드 정규화: 대문자/숫자/하이픈만, 최대 64자. DO 이름으로 사용.
function normalizeRoomCode(raw: string): string | null {
  const code = decodeURIComponent(raw).trim().toUpperCase();
  if (!/^[A-Z0-9-]{3,64}$/.test(code)) return null;
  return code;
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const parts = url.pathname.split("/").filter(Boolean); // ["room", "<CODE>"]

    if (parts.length === 1 && parts[0] === "healthz") {
      return new Response("ok", { status: 200 });
    }

    if (parts.length !== 2 || parts[0] !== "room") {
      return new Response("ThrowMe relay. Connect to /room/<CODE> via WebSocket.", {
        status: 404,
      });
    }

    const code = normalizeRoomCode(parts[1]);
    if (!code) {
      return new Response("invalid room code", { status: 400 });
    }

    if (request.headers.get("Upgrade")?.toLowerCase() !== "websocket") {
      return new Response("expected websocket upgrade", { status: 426 });
    }

    // 같은 방 코드 → 항상 같은 DO 인스턴스. 방 상태·소유권이 그 안에 모인다.
    const id = env.ROOMS.idFromName(code);
    const stub = env.ROOMS.get(id);
    return stub.fetch(request);
  },
};
