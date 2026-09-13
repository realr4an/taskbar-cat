const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), {
  status,
  headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store", "x-content-type-options": "nosniff" }
});
const b64url = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes)).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
const sha256 = async (text: string) => b64url(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text))));
const validId = (s: string) => /^[0-9a-f-]{36}$/i.test(s);
const normalizeUsername = (value: string) => value.trim().replace(/\s+/g, " ");
const validUsername = (value: string) => /^[\p{L}\p{N}][\p{L}\p{N} ._-]{2,23}$/u.test(value);
const ADMIN_PUBLIC_KEY = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEv7R5poB3XHMt/PrUMzzJirpLc9F6m/Bw+OEvV3kbDqfjMtoNAb51iKF0wRjSLFYqQvgCbf5fAbuylDNIuElkHg==";
const utf8 = (value: string) => new TextEncoder().encode(value);
const fromB64 = (value: string) => Uint8Array.from(atob(value), c => c.charCodeAt(0));
const htmlEscape = (value: string) => value.replace(/[&<>"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]!));
const securityHeaders = { "content-type": "text/html; charset=utf-8", "content-security-policy": "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'", "x-frame-options": "DENY", "x-content-type-options": "nosniff", "referrer-policy": "no-referrer", "cache-control": "no-store" };

async function hmac(keyText: string, value: string) {
  const key = await crypto.subtle.importKey("raw", utf8(keyText), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return b64url(new Uint8Array(await crypto.subtle.sign("HMAC", key, utf8(value))));
}
async function verifyHmac(keyText: string, value: string, signature: string) {
  const key = await crypto.subtle.importKey("raw", utf8(keyText), { name: "HMAC", hash: "SHA-256" }, false, ["verify"]);
  return crypto.subtle.verify("HMAC", key, FromBase64Url(signature), utf8(value));
}
async function passwordMatches(expected: string, supplied: string) {
  const proof = await hmac(supplied, "taskbar-cat-admin-login-v1");
  const key = await crypto.subtle.importKey("raw", utf8(expected), { name: "HMAC", hash: "SHA-256" }, false, ["verify"]);
  return crypto.subtle.verify("HMAC", key, FromBase64Url(proof), utf8("taskbar-cat-admin-login-v1"));
}
function FromBase64Url(value: string) { return fromB64(value.replaceAll("-", "+").replaceAll("_", "/") + "=".repeat((4 - value.length % 4) % 4)); }
async function createSession(env: Env) {
  const payload = b64url(utf8(JSON.stringify({ exp: Date.now() + 8 * 3600000, csrf: b64url(crypto.getRandomValues(new Uint8Array(24))) })));
  return payload + "." + await hmac(env.ADMIN_SESSION_KEY, payload);
}
async function readSession(request: Request, env: Env) {
  const cookie = request.headers.get("cookie")?.match(/(?:^|;\s*)tc_admin=([^;]+)/)?.[1];
  if (!cookie) return null;
  const [payload, signature] = cookie.split(".");
  if (!payload || !signature || !await verifyHmac(env.ADMIN_SESSION_KEY, payload, signature)) return null;
  const data = JSON.parse(new TextDecoder().decode(FromBase64Url(payload))) as { exp: number, csrf: string };
  return data.exp > Date.now() ? data : null;
}
function page(content: string, status = 200, extra: HeadersInit = {}) {
  return new Response(`<!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Taskbar Cat Admin</title><style>body{margin:0;background:#f5f1e8;color:#203326;font:16px system-ui}.wrap{max-width:760px;margin:48px auto;padding:24px}.card{background:#fffdf8;border:1px solid #cad8c9;border-radius:18px;padding:26px;box-shadow:0 12px 35px #20332618}h1{margin-top:0}label{display:block;margin:18px 0 6px;font-weight:650}input,select,textarea{box-sizing:border-box;width:100%;padding:12px;border:1px solid #9cad9c;border-radius:9px;font:inherit}textarea{min-height:150px;resize:vertical}button{margin-top:18px;padding:12px 18px;border:1px solid #6e906f;border-radius:9px;background:#dcebdc;color:#19371f;font-weight:700;cursor:pointer}.note{color:#58705c;font-size:14px}.ok{padding:10px;background:#e1f4df;border-radius:8px}</style></head><body><main class="wrap"><div class="card">${content}</div></main></body></html>`, { status, headers: { ...securityHeaders, ...extra } });
}
function presenceLabel(lastSeen: number, now = Date.now()) {
  const seconds = Math.max(0, Math.floor((now - lastSeen) / 1000));
  if (seconds <= 90) return "● online";
  if (seconds < 3600) return `zuletzt online vor ${Math.max(2, Math.floor(seconds / 60))} Minuten`;
  if (seconds < 86400) { const hours = Math.floor(seconds / 3600); return `zuletzt online vor ${hours} ${hours === 1 ? "Stunde" : "Stunden"}`; }
  const days = Math.floor(seconds / 86400); return `zuletzt online vor ${days} ${days === 1 ? "Tag" : "Tagen"}`;
}
async function adminDashboard(env: Env, csrf: string, sent: boolean) {
  const [rows, profile] = await Promise.all([
    env.DB.prepare("SELECT id,username,last_seen FROM devices WHERE id <> 'admin' ORDER BY username COLLATE NOCASE LIMIT 500").all<{id:string,username:string,last_seen:number}>(),
    env.DB.prepare("SELECT sender_name FROM admin_settings WHERE id=1").first<{sender_name:string}>()
  ]);
  const options = rows.results.map(d => {
    const isTemporary = /^cat-[0-9a-f]{8}$/i.test(d.username);
    const name = isTemporary ? `@${d.username} (noch kein eigener Name)` : `@${d.username}`;
    const label = `${name} — ${presenceLabel(d.last_seen)}`;
    return `<option value="${htmlEscape(d.id)}">${htmlEscape(label)}</option>`;
  }).join("");
  return page(`<h1>🐾 Taskbar Cat Admin</h1>${sent ? '<p class="ok">Nachricht wurde sicher bereitgestellt.</p>' : ''}<form method="post" action="/admin/send"><input type="hidden" name="csrf" value="${htmlEscape(csrf)}"><label>Dein Absendername</label><input name="senderName" minlength="1" maxlength="40" value="${htmlEscape(profile?.sender_name ?? "Taskbar Cat Admin")}" required><label>Katze auswählen (Username)</label><select name="recipientId" size="${Math.min(Math.max(rows.results.length, 2), 8)}" required>${options}</select><p class="note">Ältere Katzen erhalten ihren gewählten Username, sobald die aktuelle EXE einmal gestartet wurde.</p><label>Nachricht</label><textarea name="message" maxlength="500" required></textarea><button type="submit">Nachricht senden</button></form><p class="note">Die Nachricht erscheint mit deinem Absendernamen in der Gedankenblase der ausgewählten Katze.</p>`);
}

async function createAdminEnvelope(env: Env, recipientId: string, recipientPublicKey: string, senderName: string, text: string) {
  const privateBytes = fromB64(env.ADMIN_EC_PRIVATE_KEY);
  const privateEcdh = await crypto.subtle.importKey("pkcs8", privateBytes, { name: "ECDH", namedCurve: "P-256" }, false, ["deriveBits"]);
  const recipientKey = await crypto.subtle.importKey("spki", fromB64(recipientPublicKey), { name: "ECDH", namedCurve: "P-256" }, false, []);
  const ecdhParams: SubtleCryptoDeriveKeyAlgorithm & { public: CryptoKey } = { name: "ECDH", $public: recipientKey, public: recipientKey };
  const shared = new Uint8Array(await crypto.subtle.deriveBits(ecdhParams, privateEcdh, 256));
  const keyMaterial = new Uint8Array(utf8("TaskbarCat-v1").length + shared.length + utf8(recipientId).length);
  keyMaterial.set(utf8("TaskbarCat-v1")); keyMaterial.set(shared, utf8("TaskbarCat-v1").length); keyMaterial.set(utf8(recipientId), utf8("TaskbarCat-v1").length + shared.length);
  const aesKey = await crypto.subtle.importKey("raw", await crypto.subtle.digest("SHA-256", keyMaterial), "AES-GCM", false, ["encrypt"]);
  const nonce = crypto.getRandomValues(new Uint8Array(12));
  const plaintext = utf8(JSON.stringify({ messageId: crypto.randomUUID(), senderId: "admin", senderName, text, sentAt: Date.now() }));
  const sealed = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv: nonce, additionalData: utf8(recipientId), tagLength: 128 }, aesKey, plaintext));
  const ciphertext = sealed.slice(0, -16), tag = sealed.slice(-16);
  const canonical = `${b64url(nonce)}.${b64url(ciphertext)}.${b64url(tag)}.${recipientId}`;
  const signingKey = await crypto.subtle.importKey("pkcs8", privateBytes, { name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);
  const signature = new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, signingKey, utf8(canonical)));
  return JSON.stringify({ v: 2, type: "admin", nonce: b64url(nonce), ciphertext: b64url(ciphertext), tag: b64url(tag), signature: b64url(signature) });
}

async function authenticate(request: Request, env: Env) {
  const value = request.headers.get("authorization") ?? "";
  if (!value.startsWith("Bearer ") || value.length > 400) return null;
  const tokenHash = await sha256(value.slice(7));
  const device = await env.DB.prepare("SELECT id FROM devices WHERE token_hash = ?").bind(tokenHash).first<{ id: string }>();
  if (device) {
    const now = Date.now();
    await env.DB.prepare("UPDATE devices SET last_seen=? WHERE id=? AND last_seen<?").bind(now, device.id, now - 60000).run();
  }
  return device;
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    try {
      const url = new URL(request.url);
      if (request.method === "GET" && url.pathname === "/health") return json({ ok: true, service: "taskbar-cat-messaging" });
      if ((request.headers.get("content-length") ? Number(request.headers.get("content-length")) : 0) > 16384) return json({ error: "too_large" }, 413);

      if (url.pathname === "/admin" && request.method === "GET") {
        const session = await readSession(request, env);
        if (!session) return page('<h1>🐾 Taskbar Cat Admin</h1><form method="post" action="/admin/login"><label>Admin-Passwort</label><input type="password" name="password" autocomplete="current-password" required><button type="submit">Anmelden</button></form>');
        return adminDashboard(env, session.csrf, url.searchParams.get("sent") === "1");
      }
      if (url.pathname === "/admin/login" && request.method === "POST") {
        const ipKey = "admin-login:" + await sha256(request.headers.get("cf-connecting-ip") ?? "unknown");
        const attempts = await env.DB.prepare("SELECT COUNT(*) n FROM rate_events WHERE event_key=? AND created_at>?").bind(ipKey, Date.now() - 3600000).first<{n:number}>();
        if ((attempts?.n ?? 0) >= 10) return page("<h1>Zu viele Versuche</h1><p>Bitte später erneut versuchen.</p>", 429);
        const form = await request.formData();
        const suppliedPassword = String(form.get("password") ?? "").trim();
        if (!await passwordMatches(env.ADMIN_PASSWORD, suppliedPassword)) {
          await env.DB.prepare("INSERT INTO rate_events(event_key,created_at) VALUES(?,?)").bind(ipKey, Date.now()).run();
          return page('<h1>Anmeldung fehlgeschlagen</h1><p>Das Passwort stimmt nicht. Bitte erneut eingeben.</p><p><a href="/admin">Zurück zur Anmeldung</a></p>', 401);
        }
        await env.DB.prepare("DELETE FROM rate_events WHERE event_key=?").bind(ipKey).run();
        const session = await createSession(env);
        return new Response(null, { status: 303, headers: { location: "/admin", "set-cookie": `tc_admin=${session}; Path=/admin; Secure; HttpOnly; SameSite=Strict; Max-Age=28800`, "cache-control": "no-store" } });
      }
      if (url.pathname === "/admin/send" && request.method === "POST") {
        const session = await readSession(request, env); if (!session) return new Response(null, { status: 303, headers: { location: "/admin" } });
        const form = await request.formData();
        if (String(form.get("csrf")) !== session.csrf) return page("<h1>Ungültige Anfrage</h1>", 403);
        const recipientId = String(form.get("recipientId") ?? ""), message = String(form.get("message") ?? "").trim(), senderName = String(form.get("senderName") ?? "").trim();
        if (!validId(recipientId) || message.length < 1 || message.length > 500 || senderName.length < 1 || senderName.length > 40) return page("<h1>Ungültige Nachricht</h1>", 400);
        const recipient = await env.DB.prepare("SELECT public_key FROM devices WHERE id=?").bind(recipientId).first<{public_key:string}>();
        if (!recipient) return page("<h1>Empfänger nicht gefunden</h1>", 404);
        const envelope = await createAdminEnvelope(env, recipientId, recipient.public_key, senderName, message), now = Date.now();
        await env.DB.batch([
          env.DB.prepare("UPDATE admin_settings SET sender_name=? WHERE id=1").bind(senderName),
          env.DB.prepare("INSERT INTO messages(id,sender_id,recipient_id,envelope,created_at,expires_at) VALUES(?,?,?,?,?,?)").bind(crypto.randomUUID(), "admin", recipientId, envelope, now, now + 7 * 86400000)
        ]);
        return new Response(null, { status: 303, headers: { location: "/admin?sent=1", "cache-control": "no-store" } });
      }

      if (request.method === "POST" && url.pathname === "/v1/devices") {
        const body = await request.json<{ publicKey?: string, name?: string }>();
        if (!body.publicKey || body.publicKey.length < 80 || body.publicKey.length > 500) return json({ error: "invalid_public_key" }, 400);
        const requestedName = normalizeUsername(body.name ?? "Sneaker");
        const ip = request.headers.get("cf-connecting-ip") ?? "unknown";
        const registrationKey = "register:" + await sha256(ip + ":" + new Date().toISOString().slice(0, 10));
        const registrations = await env.DB.prepare("SELECT COUNT(*) n FROM rate_events WHERE event_key=? AND created_at>?").bind(registrationKey, Date.now() - 86400000).first<{n:number}>();
        if ((registrations?.n ?? 0) >= 20) return json({ error: "rate_limited" }, 429);
        const id = crypto.randomUUID();
        const token = b64url(crypto.getRandomValues(new Uint8Array(32)));
        const now = Date.now();
        const nameTaken = validUsername(requestedName) ? await env.DB.prepare("SELECT 1 found FROM devices WHERE username=? COLLATE NOCASE").bind(requestedName).first() : true;
        const username = !nameTaken ? requestedName : `Sneaker-${id.slice(0, 6)}`;
        await env.DB.prepare("INSERT INTO devices(id, token_hash, public_key, created_at, last_seen, display_name, username) VALUES(?,?,?,?,?,?,?)")
          .bind(id, await sha256(token), body.publicKey, now, now, username, username).run();
        await env.DB.prepare("INSERT INTO rate_events(event_key,created_at) VALUES(?,?)").bind(registrationKey, now).run();
        return json({ id, token, username }, 201);
      }

      const auth = await authenticate(request, env);
      if (!auth) return json({ error: "unauthorized" }, 401);

      if (request.method === "PUT" && url.pathname === "/v1/devices/me") {
        const body = await request.json<{ name?: string }>(); const name = normalizeUsername(body.name ?? "");
        if (!validUsername(name)) return json({ error: "invalid_name" }, 400);
        const taken = await env.DB.prepare("SELECT id FROM devices WHERE username=? COLLATE NOCASE AND id<>?").bind(name, auth.id).first();
        if (taken) return json({ error: "username_taken" }, 409);
        await env.DB.prepare("UPDATE devices SET display_name=?,username=?,last_seen=? WHERE id=?").bind(name, name, Date.now(), auth.id).run();
        return new Response(null, { status: 204 });
      }

      if (request.method === "GET" && url.pathname === "/v1/users/search") {
        const query = normalizeUsername(url.searchParams.get("q") ?? "");
        if (query.length < 2 || query.length > 24) return json({ users: [] });
        const escaped = query.replace(/[\\%_]/g, "\\$&");
        const now = Date.now();
        const rows = await env.DB.prepare("SELECT id,username,last_seen lastSeen FROM devices WHERE id<>? AND id<>'admin' AND username LIKE ? ESCAPE '\\' COLLATE NOCASE ORDER BY CASE WHEN username LIKE ? ESCAPE '\\' COLLATE NOCASE THEN 0 ELSE 1 END, length(username), username COLLATE NOCASE LIMIT 10")
          .bind(auth.id, `%${escaped}%`, `${escaped}%`).all<{id:string,username:string,lastSeen:number}>();
        return json({ users: rows.results.map(x => ({ ...x, online: now - x.lastSeen <= 90000 })) });
      }

      if (request.method === "GET" && url.pathname === "/v1/friends") {
        const now = Date.now();
        const rows = await env.DB.prepare("SELECT d.id,d.username,d.public_key publicKey,d.last_seen lastSeen FROM friendships f JOIN devices d ON d.id=f.friend_id WHERE f.device_id=? ORDER BY d.username COLLATE NOCASE")
          .bind(auth.id).all<{id:string,username:string,publicKey:string,lastSeen:number}>();
        return json({ friends: rows.results.map(x => ({ ...x, online: now - x.lastSeen <= 90000 })) });
      }

      if (request.method === "POST" && url.pathname === "/v1/friends") {
        const body = await request.json<{ deviceId?: string }>(); const friendId = body.deviceId ?? "";
        if (!validId(friendId) || friendId === auth.id) return json({ error: "invalid_friend" }, 400);
        const friend = await env.DB.prepare("SELECT id,username,public_key publicKey,last_seen lastSeen FROM devices WHERE id=? AND id<>'admin'").bind(friendId).first<{id:string,username:string,publicKey:string,lastSeen:number}>();
        if (!friend) return json({ error: "not_found" }, 404);
        const now = Date.now();
        await env.DB.batch([
          env.DB.prepare("INSERT OR IGNORE INTO friendships(device_id,friend_id,created_at) VALUES(?,?,?)").bind(auth.id, friendId, now),
          env.DB.prepare("INSERT OR IGNORE INTO friendships(device_id,friend_id,created_at) VALUES(?,?,?)").bind(friendId, auth.id, now)
        ]);
        return json({ friend: { ...friend, online: Date.now() - friend.lastSeen <= 90000 } }, 201);
      }

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
    } catch (error) {
      console.error(JSON.stringify({ event: "request_error", path: new URL(request.url).pathname, error: error instanceof Error ? error.message : "unknown" }));
      return json({ error: "bad_request" }, 400);
    }
  }
} satisfies ExportedHandler<Env>;
