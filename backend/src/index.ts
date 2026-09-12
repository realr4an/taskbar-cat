const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), {
  status,
  headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", "x-content-type-options": "nosniff" }
});
const b64url = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes)).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
const sha256 = async (text: string) => b64url(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text))));
const validId = (s: string) => /^[0-9a-f-]{36}$/i.test(s);

async function authenticate(request: Request, env: Env) {
  const value = request.headers.get("authorization") ?? "";
  if (!value.startsWith("Bearer ") || value.length > 400) return null;
  const tokenHash = await sha256(value.slice(7));
  return env.DB.prepare("SELECT id FROM devices WHERE token_hash = ?").bind(tokenHash).first<{ id: string }>();
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    try {
      const url = new URL(request.url);
      if (request.method === "GET" && url.pathname === "/health") return json({ ok: true, service: "taskbar-cat-messaging" });
      if ((request.headers.get("content-length") ? Number(request.headers.get("content-length")) : 0) > 16384) return json({ error: "too_large" }, 413);

      if (request.method === "POST" && url.pathname === "/v1/devices") {
        const body = await request.json<{ publicKey?: string }>();
        if (!body.publicKey || body.publicKey.length < 80 || body.publicKey.length > 500) return json({ error: "invalid_public_key" }, 400);
        const ip = request.headers.get("cf-connecting-ip") ?? "unknown";
        const registrationKey = "register:" + await sha256(ip + ":" + new Date().toISOString().slice(0, 10));
        const registrations = await env.DB.prepare("SELECT COUNT(*) n FROM rate_events WHERE event_key=? AND created_at>?").bind(registrationKey, Date.now() - 86400000).first<{n:number}>();
        if ((registrations?.n ?? 0) >= 20) return json({ error: "rate_limited" }, 429);
        const id = crypto.randomUUID();
        const token = b64url(crypto.getRandomValues(new Uint8Array(32)));
        const now = Date.now();
        await env.DB.prepare("INSERT INTO devices(id, token_hash, public_key, created_at, last_seen) VALUES(?,?,?,?,?)")
          .bind(id, await sha256(token), body.publicKey, now, now).run();
        await env.DB.prepare("INSERT INTO rate_events(event_key,created_at) VALUES(?,?)").bind(registrationKey, now).run();
        return json({ id, token }, 201);
      }

      const auth = await authenticate(request, env);
      if (!auth) return json({ error: "unauthorized" }, 401);

      if (request.method === "POST" && url.pathname === "/v1/messages") {
        const body = await request.json<{ recipientId?: string, envelope?: string }>();
        if (!body.recipientId || !validId(body.recipientId) || !body.envelope || body.envelope.length > 12000) return json({ error: "invalid_message" }, 400);
        if (body.recipientId === auth.id) return json({ error: "self_message" }, 400);
        const recipient = await env.DB.prepare("SELECT id FROM devices WHERE id=?").bind(body.recipientId).first();
        if (!recipient) return json({ error: "recipient_not_found" }, 404);
        const sendKey = "send:" + auth.id;
        const recent = await env.DB.prepare("SELECT COUNT(*) n FROM rate_events WHERE event_key=? AND created_at>?").bind(sendKey, Date.now() - 3600000).first<{n:number}>();
        if ((recent?.n ?? 0) >= 60) return json({ error: "rate_limited" }, 429);
        const id = crypto.randomUUID(), now = Date.now();
        await env.DB.prepare("INSERT INTO messages(id,sender_id,recipient_id,envelope,created_at,expires_at) VALUES(?,?,?,?,?,?)")
          .bind(id, auth.id, body.recipientId, body.envelope, now, now + 7 * 86400000).run();
        await env.DB.prepare("INSERT INTO rate_events(event_key,created_at) VALUES(?,?)").bind(sendKey, now).run();
        return json({ id }, 201);
      }

      if (request.method === "GET" && url.pathname === "/v1/messages") {
        await env.DB.prepare("DELETE FROM messages WHERE expires_at<?").bind(Date.now()).run();
        await env.DB.prepare("DELETE FROM rate_events WHERE created_at<?").bind(Date.now() - 8 * 86400000).run();
        const rows = await env.DB.prepare("SELECT id,sender_id senderId,envelope,created_at createdAt FROM messages WHERE recipient_id=? ORDER BY created_at LIMIT 20")
          .bind(auth.id).all();
        return json({ messages: rows.results });
      }

      const ack = url.pathname.match(/^\/v1\/messages\/([0-9a-f-]{36})$/i);
      if (request.method === "DELETE" && ack) {
        await env.DB.prepare("DELETE FROM messages WHERE id=? AND recipient_id=?").bind(ack[1], auth.id).run();
        return new Response(null, { status: 204 });
      }
      return json({ error: "not_found" }, 404);
    } catch {
      return json({ error: "bad_request" }, 400);
    }
  }
} satisfies ExportedHandler<Env>;
