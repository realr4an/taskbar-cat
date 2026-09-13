using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TaskbarCat;

internal sealed class StoredChatMessage
{
    public string Id { get; set; } = "";
    public string ContactId { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string Text { get; set; } = "";
    public long SentAt { get; set; }
    public bool Outgoing { get; set; }
}

internal sealed class EncryptedChatStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TaskbarCat-chat-history-v1");
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarCat", "chat-history.dat");
    private readonly List<StoredChatMessage> messages;

    public EncryptedChatStore() => messages = Load();

    public IReadOnlyList<StoredChatMessage> Conversation(string contactId) => messages
        .Where(x => x.ContactId == contactId)
        .OrderBy(x => x.SentAt)
        .ToArray();

    public void Add(StoredChatMessage message)
    {
        if (messages.Any(x => x.Id == message.Id)) return;
        messages.Add(message);
        if (messages.Count > 5000) messages.RemoveRange(0, messages.Count - 5000);
        Save();
    }

    private List<StoredChatMessage> Load()
    {
        try
        {
            if (!File.Exists(path)) return new();
            var protectedBytes = File.ReadAllBytes(path);
            var json = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<List<StoredChatMessage>>(json) ?? new();
        }
        catch { return new(); }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(messages);
        var protectedBytes = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
        var temporary = path + ".new";
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, path, true);
    }
}
