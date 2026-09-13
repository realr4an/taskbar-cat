using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TaskbarCat;

internal sealed class CatContact
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Freund";
    public string PublicKey { get; set; } = "";
    public long LastSeen { get; set; }
    public bool IsOnline { get; set; }
    public string Presence => PresenceText(IsOnline, LastSeen);
    public override string ToString() => $"{Name}  ·  {Presence}";

    internal static string PresenceText(bool online, long lastSeen)
    {
        if (online) return "● online";
        if (lastSeen <= 0) return "noch nie online";
        var elapsed = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(lastSeen);
        if (elapsed.TotalMinutes < 60) return $"zuletzt vor {Math.Max(2, (int)elapsed.TotalMinutes)} Min.";
        if (elapsed.TotalHours < 24) return $"zuletzt vor {(int)elapsed.TotalHours} Std.";
        return $"zuletzt vor {(int)elapsed.TotalDays} Tg.";
    }
}

internal sealed class CatSearchResult
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long LastSeen { get; set; }
    public bool IsOnline { get; set; }
    public override string ToString() => $"{Name}  ·  {CatContact.PresenceText(IsOnline, LastSeen)}";
}

internal sealed class IncomingCatMessage
{
    public string SenderId { get; init; } = "";
    public string SenderName { get; init; } = "Freund";
    public string Text { get; init; } = "";
    public string MessageId { get; init; } = "";
    public long SentAt { get; init; }
}

internal sealed class MessagingService : IDisposable
{
    // Replaced with the production workers.dev URL during deployment.
    internal const string ApiBase = "https://taskbar-cat-messaging.taskbar-cat-messaging.workers.dev";
    private const string AdminPublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEv7R5poB3XHMt/PrUMzzJirpLc9F6m/Bw+OEvV3kbDqfjMtoNAb51iKF0wRjSLFYqQvgCbf5fAbuylDNIuElkHg==";
    private readonly CatSettings settings;
    private readonly Action save;
    private readonly EncryptedChatStore chatStore = new();
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 20000 };
    private bool busy;
    public event Action<IncomingCatMessage>? MessageReceived;
    public event Action<string>? ConversationChanged;
    public event Action<IReadOnlyList<CatContact>>? FriendsChanged;

    public MessagingService(CatSettings settings, Action save)
    {
        this.settings = settings;
        this.save = save;
        poll.Tick += async (_, _) => await PollAsync();
    }

    public async Task StartAsync()
    {
        try
        {
            await EnsureIdentityAsync();
            try { await SyncNameAsync(); } catch { }
            try { await RefreshFriendsAsync(); } catch { }
            poll.Start();
            await PollAsync();
        }
        catch { }
    }

    public Task EnsureReadyAsync() => EnsureIdentityAsync();
    public IReadOnlyList<StoredChatMessage> Conversation(string contactId) => chatStore.Conversation(contactId);

    public async Task SyncNameAsync()
    {
        if (string.IsNullOrEmpty(settings.DeviceTokenProtected)) return;
        using var req = Authorized(HttpMethod.Put, "/v1/devices/me", JsonSerializer.Serialize(new { name = settings.Name }));
        using var response = await http.SendAsync(req);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict) throw new InvalidOperationException("Dieser Katzenname ist bereits vergeben.");
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest) throw new InvalidOperationException("Der Katzenname muss 3–24 Zeichen lang sein und darf Buchstaben, Zahlen, Leerzeichen, Punkt, Minus und Unterstrich enthalten.");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Der Katzenname konnte nicht gespeichert werden.");
    }

    public string InviteCode
    {
        get
        {
            if (string.IsNullOrEmpty(settings.DeviceId) || string.IsNullOrEmpty(settings.PublicKey)) return "Wird vorbereitet …";
            var payload = JsonSerializer.Serialize(new { v = 1, id = settings.DeviceId, n = settings.Name, k = settings.PublicKey });
            return "TC1." + Base64Url(Encoding.UTF8.GetBytes(payload));
        }
    }

    public CatContact AddContact(string code)
    {
        code = code.Trim();
        if (!code.StartsWith("TC1.", StringComparison.Ordinal)) throw new InvalidOperationException("Das ist kein gültiger Taskbar-Cat-Freundescode.");
        var doc = JsonDocument.Parse(Encoding.UTF8.GetString(FromBase64Url(code[4..])));
        var root = doc.RootElement;
        if (root.GetProperty("v").GetInt32() != 1) throw new InvalidOperationException("Diese Code-Version wird nicht unterstützt.");
        var contact = new CatContact { Id = root.GetProperty("id").GetString()!, Name = root.GetProperty("n").GetString()!, PublicKey = root.GetProperty("k").GetString()! };
        if (!Guid.TryParse(contact.Id, out _) || contact.PublicKey.Length is < 80 or > 500 || contact.Name.Length > 40) throw new InvalidOperationException("Der Freundescode ist beschädigt.");
        if (contact.Id == settings.DeviceId) throw new InvalidOperationException("Das ist dein eigener Freundescode.");
        using var check = ECDiffieHellman.Create(); check.ImportSubjectPublicKeyInfo(Convert.FromBase64String(contact.PublicKey), out _);
        settings.Contacts.RemoveAll(x => x.Id == contact.Id);
        settings.Contacts.Add(contact);
        save();
        return contact;
    }

    public async Task<List<CatSearchResult>> SearchUsersAsync(string query)
    {
        query = query.Trim();
        if (query.Length < 2) return new();
        await EnsureIdentityAsync();
        using var req = Authorized(HttpMethod.Get, "/v1/users/search?q=" + Uri.EscapeDataString(query), null);
        using var response = await http.SendAsync(req);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Die Suche ist gerade nicht erreichbar.");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("users").EnumerateArray().Select(x => new CatSearchResult
        {
            Id = x.GetProperty("id").GetString()!,
            Name = x.GetProperty("username").GetString()!,
            LastSeen = x.GetProperty("lastSeen").GetInt64(),
            IsOnline = x.GetProperty("online").GetBoolean()
        }).ToList();
    }

    public async Task<CatContact> AddFriendAsync(CatSearchResult result)
    {
        await EnsureIdentityAsync();
        using var req = Authorized(HttpMethod.Post, "/v1/friends", JsonSerializer.Serialize(new { deviceId = result.Id }));
        using var response = await http.SendAsync(req);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Der Freund konnte nicht hinzugefügt werden.");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = doc.RootElement.GetProperty("friend");
        var contact = new CatContact { Id = item.GetProperty("id").GetString()!, Name = item.GetProperty("username").GetString()!, PublicKey = item.GetProperty("publicKey").GetString()!, LastSeen = item.GetProperty("lastSeen").GetInt64(), IsOnline = item.GetProperty("online").GetBoolean() };
        UpsertContact(contact);
        return contact;
    }

    public async Task<List<CatContact>> RefreshFriendsAsync()
    {
        await EnsureIdentityAsync();
        using var req = Authorized(HttpMethod.Get, "/v1/friends", null);
        using var response = await http.SendAsync(req);
        if (!response.IsSuccessStatusCode) return settings.Contacts;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var contacts = doc.RootElement.GetProperty("friends").EnumerateArray().Select(x => new CatContact
        {
            Id = x.GetProperty("id").GetString()!, Name = x.GetProperty("username").GetString()!, PublicKey = x.GetProperty("publicKey").GetString()!, LastSeen = x.GetProperty("lastSeen").GetInt64(), IsOnline = x.GetProperty("online").GetBoolean()
        }).ToList();
        settings.Contacts = contacts;
        save();
        try { FriendsChanged?.Invoke(contacts); } catch { }
        return contacts;
    }

    private void UpsertContact(CatContact contact)
    {
        settings.Contacts.RemoveAll(x => x.Id == contact.Id);
        settings.Contacts.Add(contact);
        save();
    }

    public async Task SendAsync(CatContact recipient, string text)
    {
        text = text.Trim();
        if (text.Length is < 1 or > 500) throw new InvalidOperationException("Eine Nachricht darf 1 bis 500 Zeichen lang sein.");
        await EnsureIdentityAsync();
        using var recipientKey = ECDiffieHellman.Create();
        recipientKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(recipient.PublicKey), out _);
        using var own = ECDiffieHellman.Create();
        own.ImportPkcs8PrivateKey(ProtectedData.Unprotect(Convert.FromBase64String(settings.PrivateKeyProtected), null, DataProtectionScope.CurrentUser), out _);
        var secret = own.DeriveKeyFromHash(recipientKey.PublicKey, HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes("TaskbarCat-v1"), Encoding.UTF8.GetBytes(recipient.Id));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var messageId = Guid.NewGuid().ToString();
        var sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { messageId, senderId = settings.DeviceId, senderName = settings.Name, text, sentAt }));
        var cipher = new byte[plain.Length]; var tag = new byte[16];
        using (var aes = new AesGcm(secret, 16)) aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(recipient.Id));
        var envelope = JsonSerializer.Serialize(new { v = 1, nonce = Convert.ToBase64String(nonce), ciphertext = Convert.ToBase64String(cipher), tag = Convert.ToBase64String(tag) });
        using var req = Authorized(HttpMethod.Post, "/v1/messages", JsonSerializer.Serialize(new { recipientId = recipient.Id, envelope }));
        using var response = await http.SendAsync(req);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Die Nachricht konnte gerade nicht gesendet werden.");
        chatStore.Add(new StoredChatMessage { Id = messageId, ContactId = recipient.Id, SenderName = settings.Name, Text = text, SentAt = sentAt, Outgoing = true });
        ConversationChanged?.Invoke(recipient.Id);
    }

    private async Task EnsureIdentityAsync()
    {
        if (string.IsNullOrEmpty(settings.PrivateKeyProtected))
        {
            using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            settings.PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
            settings.PrivateKeyProtected = Convert.ToBase64String(ProtectedData.Protect(key.ExportPkcs8PrivateKey(), null, DataProtectionScope.CurrentUser));
            save();
        }
        if (!string.IsNullOrEmpty(settings.DeviceId) && !string.IsNullOrEmpty(settings.DeviceTokenProtected)) return;
        using var response = await http.PostAsync(ApiBase + "/v1/devices", new StringContent(JsonSerializer.Serialize(new { publicKey = settings.PublicKey, name = settings.Name }), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var registration = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        settings.DeviceId = registration.GetProperty("id").GetString()!;
        if (registration.TryGetProperty("username", out var username)) settings.Name = username.GetString() ?? settings.Name;
        settings.DeviceTokenProtected = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(registration.GetProperty("token").GetString()!), null, DataProtectionScope.CurrentUser));
        save();
    }

    private async Task PollAsync()
    {
        if (busy || string.IsNullOrEmpty(settings.DeviceId)) return;
        busy = true;
        try
        {
            // A newly added friendship is mutual. Refresh keys before reading
            // messages so the other side can decrypt immediately while running.
            try { await RefreshFriendsAsync(); } catch { }
            using var req = Authorized(HttpMethod.Get, "/v1/messages", null);
            using var response = await http.SendAsync(req);
            if (!response.IsSuccessStatusCode) return;
            var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var item in doc.RootElement.GetProperty("messages").EnumerateArray())
            {
                var accepted = false;
                try {
                    var msg = Decrypt(item.GetProperty("envelope").GetString()!, item.GetProperty("senderId").GetString()!);
                    chatStore.Add(new StoredChatMessage { Id = msg.MessageId, ContactId = msg.SenderId, SenderName = msg.SenderName, Text = msg.Text, SentAt = msg.SentAt, Outgoing = false });
                    MarkSeen(msg.MessageId);
                    accepted = true;
                    try { ConversationChanged?.Invoke(msg.SenderId); } catch { }
                    try { MessageReceived?.Invoke(msg); } catch { }
                }
                catch { }
                if (!accepted) continue;
                using var ack = Authorized(HttpMethod.Delete, "/v1/messages/" + item.GetProperty("id").GetString(), null);
                using var ignored = await http.SendAsync(ack);
            }
        }
        catch { }
        finally { busy = false; }
    }

    private IncomingCatMessage Decrypt(string envelopeJson, string serverSenderId)
    {
        var env = JsonDocument.Parse(envelopeJson).RootElement;
        if (env.TryGetProperty("type", out var type) && type.GetString() == "admin" && serverSenderId == "admin") return DecryptAdmin(env);
        var contact = settings.Contacts.FirstOrDefault(x => x.Id == serverSenderId) ?? throw new CryptographicException("Unbekannter Absender");
        using var own = ECDiffieHellman.Create();
        own.ImportPkcs8PrivateKey(ProtectedData.Unprotect(Convert.FromBase64String(settings.PrivateKeyProtected), null, DataProtectionScope.CurrentUser), out _);
        using var senderKey = ECDiffieHellman.Create();
        senderKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(contact.PublicKey), out _);
        var secret = own.DeriveKeyFromHash(senderKey.PublicKey, HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes("TaskbarCat-v1"), Encoding.UTF8.GetBytes(settings.DeviceId));
        var cipher = Convert.FromBase64String(env.GetProperty("ciphertext").GetString()!); var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(secret, 16)) aes.Decrypt(Convert.FromBase64String(env.GetProperty("nonce").GetString()!), cipher, Convert.FromBase64String(env.GetProperty("tag").GetString()!), plain, Encoding.UTF8.GetBytes(settings.DeviceId));
        var data = JsonDocument.Parse(plain).RootElement;
        if (data.GetProperty("senderId").GetString() != serverSenderId) throw new CryptographicException();
        return AcceptPayload(data, serverSenderId, contact.Name);
    }

    private IncomingCatMessage DecryptAdmin(JsonElement env)
    {
        var nonceText = env.GetProperty("nonce").GetString()!; var cipherText = env.GetProperty("ciphertext").GetString()!; var tagText = env.GetProperty("tag").GetString()!;
        var canonical = $"{nonceText}.{cipherText}.{tagText}.{settings.DeviceId}";
        using var adminVerify = ECDsa.Create(); adminVerify.ImportSubjectPublicKeyInfo(Convert.FromBase64String(AdminPublicKey), out _);
        if (!adminVerify.VerifyData(Encoding.UTF8.GetBytes(canonical), FromBase64Url(env.GetProperty("signature").GetString()!), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw new CryptographicException("Ungültige Admin-Signatur");
        using var own = ECDiffieHellman.Create(); own.ImportPkcs8PrivateKey(ProtectedData.Unprotect(Convert.FromBase64String(settings.PrivateKeyProtected), null, DataProtectionScope.CurrentUser), out _);
        using var adminKey = ECDiffieHellman.Create(); adminKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(AdminPublicKey), out _);
        var secret = own.DeriveKeyFromHash(adminKey.PublicKey, HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes("TaskbarCat-v1"), Encoding.UTF8.GetBytes(settings.DeviceId));
        var cipher = FromBase64Url(cipherText); var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(secret, 16)) aes.Decrypt(FromBase64Url(nonceText), cipher, FromBase64Url(tagText), plain, Encoding.UTF8.GetBytes(settings.DeviceId));
        var data = JsonDocument.Parse(plain).RootElement;
        if (data.GetProperty("senderId").GetString() != "admin") throw new CryptographicException();
        var senderName = data.TryGetProperty("senderName", out var name) ? name.GetString() ?? "Taskbar Cat Admin" : "Taskbar Cat Admin";
        return AcceptPayload(data, "admin", senderName[..Math.Min(senderName.Length, 40)]);
    }

    private IncomingCatMessage AcceptPayload(JsonElement data, string senderId, string senderName)
    {
        var messageId = data.GetProperty("messageId").GetString() ?? throw new CryptographicException();
        if (!Guid.TryParse(messageId, out _) || settings.SeenMessageIds.Contains(messageId)) throw new CryptographicException("Wiederholte Nachricht");
        var sentAt = data.GetProperty("sentAt").GetInt64();
        if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - sentAt) > 8L * 86400000) throw new CryptographicException("Abgelaufene Nachricht");
        return new IncomingCatMessage { SenderId = senderId, SenderName = senderName, Text = data.GetProperty("text").GetString() ?? "", MessageId = messageId, SentAt = sentAt };
    }

    private void MarkSeen(string messageId)
    {
        settings.SeenMessageIds.Add(messageId);
        if (settings.SeenMessageIds.Count > 1000) settings.SeenMessageIds.RemoveRange(0, settings.SeenMessageIds.Count - 1000);
        save();
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path, string? json)
    {
        var req = new HttpRequestMessage(method, ApiBase + path);
        var token = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(settings.DeviceTokenProtected), null, DataProtectionScope.CurrentUser));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return req;
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromBase64Url(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    public void Dispose() { poll.Dispose(); http.Dispose(); }
}
