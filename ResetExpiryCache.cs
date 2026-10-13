using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexMeter;

internal sealed class ResetExpiryCache
{
    private string? account;
    private int? count;
    private long? expiry;
    private readonly string? filePath;
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public bool IsCached { get; private set; }

    private sealed record Snapshot(string? Account, int? Count, long? Expiry, DateTimeOffset? ConfirmedAt);

    public ResetExpiryCache(string? filePath)
    {
        this.filePath = filePath;
        if (filePath == null) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(filePath));
            if (saved?.Account is not { Length: 64 } || saved.Count is not > 0 ||
                saved.Expiry is not > 0 || saved.Expiry > DateTimeOffset.MaxValue.ToUnixTimeSeconds() || saved.ConfirmedAt == null) return;
            account = saved.Account; count = saved.Count; expiry = saved.Expiry; ConfirmedAt = saved.ConfirmedAt;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public static string? Identity(JsonElement value)
    {
        var email = value.Field("email").String();
        return string.IsNullOrWhiteSpace(email) ? null : JsonSerializer.Serialize(new
        {
            email = email.Trim().ToLowerInvariant(), type = value.Field("type").String(),
            plan = value.Field("planType").String(), id = value.Field("id").String()
        });
    }

    private void Save()
    {
        if (filePath == null) return;
        try
        {
            if (expiry == null) { File.Delete(filePath); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath + ".tmp", JsonSerializer.Serialize(new Snapshot(account, count, expiry, ConfirmedAt)));
            File.Move(filePath + ".tmp", filePath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void SelectAccount(string? identity)
    {
        var key = identity == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        if (key == null || key != account)
        {
            count = null;
            expiry = null;
            ConfirmedAt = null;
            IsCached = false;
            Save();
        }
        account = key;
    }

    public Usage Apply(string? identity, Usage current, bool detailsMissing, DateTimeOffset now)
    {
        SelectAccount(identity);
        IsCached = false;
        if (current.ResetCount != count ||
            expiry <= now.ToUnixTimeSeconds() || ConfirmedAt <= now.AddHours(-1) || ConfirmedAt > now)
        {
            expiry = null;
            ConfirmedAt = null;
        }
        count = current.ResetCount;
        if (current.ResetCount is not > 0)
        {
            expiry = null;
            ConfirmedAt = null;
            Save();
            return current with { ResetExpiry = null };
        }
        if (current.ResetExpiry is { } fresh && fresh > now.ToUnixTimeSeconds())
        {
            expiry = identity == null ? null : fresh;
            ConfirmedAt = identity == null ? null : now;
            Save();
            return current;
        }
        if (detailsMissing && identity != null && expiry != null)
        {
            IsCached = true;
            return current with { ResetExpiry = expiry };
        }
        expiry = null;
        ConfirmedAt = null;
        Save();
        return current with { ResetExpiry = null };
    }
}
