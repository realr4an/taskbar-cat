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
    public override string ToString() => Name;
}

internal sealed class IncomingCatMessage
{
    public string SenderName { get; init; } = "Freund";
    public string Text { get; init; } = "";
}

internal sealed class MessagingService : IDisposable
{
    // Replaced with the production workers.dev URL during deployment.
    internal const string ApiBase = "https://taskbar-cat-messaging.taskbar-cat-messaging.workers.dev";
    private readonly CatSettings settings;
    private readonly Action save;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 20000 };
    private bool busy;
    public event Action<IncomingCatMessage>? MessageReceived;

    public MessagingService(CatSettings settings, Action save)
    {
        this.settings = settings;
        this.save = save;
        poll.Tick += async (_, _) => await PollAsync();
    }

    public async Task StartAsync()
    {
        try { await EnsureIdentityAsync(); poll.Start(); await PollAsync(); } catch { }
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
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { messageId = Guid.NewGuid().ToString(), senderId = settings.DeviceId, senderName = settings.Name, text, sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }));
        var cipher = new byte[plain.Length]; var tag = new byte[16];
        using (var aes = new AesGcm(secret, 16)) aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(recipient.Id));
        var envelope = JsonSerializer.Serialize(new { v = 1, nonce = Convert.ToBase64String(nonce), ciphertext = Convert.ToBase64String(cipher), tag = Convert.ToBase64String(tag) });
        using var req = Authorized(HttpMethod.Post, "/v1/messages", JsonSerializer.Serialize(new { recipientId = recipient.Id, envelope }));
        using var response = await http.SendAsync(req);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Die Nachricht konnte gerade nicht gesendet werden.");
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
        using var response = await http.PostAsync(ApiBase + "/v1/devices", new StringContent(JsonSerializer.Serialize(new { publicKey = settings.PublicKey }), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var registration = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        settings.DeviceId = registration.GetProperty("id").GetString()!;
        settings.DeviceTokenProtected = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(registration.GetProperty("token").GetString()!), null, DataProtectionScope.CurrentUser));
        save();
    }

    private async Task PollAsync()
    {
        if (busy || string.IsNullOrEmpty(settings.DeviceId)) return;
        busy = true;
        try
        {
            using var req = Authorized(HttpMethod.Get, "/v1/messages", null);
            using var response = await http.SendAsync(req);
            if (!response.IsSuccessStatusCode) return;
            var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var item in doc.RootElement.GetProperty("messages").EnumerateArray())
            {
                try {
                    var msg = Decrypt(item.GetProperty("envelope").GetString()!, item.GetProperty("senderId").GetString()!);
                    MessageReceived?.Invoke(msg);
                }
                catch { }
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
        var messageId = data.GetProperty("messageId").GetString() ?? throw new CryptographicException();
        if (!Guid.TryParse(messageId, out _) || settings.SeenMessageIds.Contains(messageId)) throw new CryptographicException("Wiederholte Nachricht");
        var sentAt = data.GetProperty("sentAt").GetInt64();
        if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - sentAt) > 8L * 86400000) throw new CryptographicException("Abgelaufene Nachricht");
        settings.SeenMessageIds.Add(messageId);
        if (settings.SeenMessageIds.Count > 1000) settings.SeenMessageIds.RemoveRange(0, settings.SeenMessageIds.Count - 1000);
        save();
        return new IncomingCatMessage { SenderName = contact.Name, Text = data.GetProperty("text").GetString() ?? "" };
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
