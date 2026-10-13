using System.Text.Json;

namespace CodexMeter;

internal sealed record Quota(double? Remaining, long? ResetsAt)
{
    public string Percent => Remaining is { } n ? $"{n:0}%" : "未提供";
    public string Countdown(DateTimeOffset now)
    {
        if (ResetsAt is not { } at) return "—";
        var seconds = Math.Max(0, at - now.ToUnixTimeSeconds());
        var t = TimeSpan.FromSeconds(seconds);
        if (seconds == 0) return "待刷新";
        return t.TotalDays >= 1 ? $"{(int)t.TotalDays}d{t.Hours}h" : $"{(int)t.TotalHours}h{t.Minutes}m";
    }
}

internal sealed record Usage(Quota FiveHour, Quota Weekly, int? ResetCount, long? ResetExpiry)
{
    public static readonly Usage Empty = new(new(null, null), new(null, null), null, null);

    public static Usage Parse(JsonElement result)
    {
        var bucket = result.Field("rateLimitsByLimitId").Field("codex");
        if (bucket.ValueKind != JsonValueKind.Object) bucket = result.Field("rateLimits");
        var windows = new[] { bucket.Field("primary"), bucket.Field("secondary") };
        Quota Read(int minutes)
        {
            var window = windows.FirstOrDefault(x => x.Field("windowDurationMins").Number() == minutes);
            var used = window.Field("usedPercent").Number();
            return new(used.HasValue ? Math.Clamp(100 - used.Value, 0, 100) : null,
                window.Field("resetsAt").Integer());
        }
        var reset = result.Field("rateLimitResetCredits");
        long? expiry = null;
        var rows = reset.Field("credits");
        if (rows.ValueKind == JsonValueKind.Array)
            foreach (var row in rows.EnumerateArray())
            {
                var status = row.Field("status").String();
                if (status != "available") continue;
                if (row.Field("expiresAt").Integer() is { } at && (expiry == null || at < expiry)) expiry = at;
            }
        return new(Read(300), Read(10080), reset.Field("availableCount").Integer() is { } count ? checked((int)count) : null, expiry);
    }
}

internal static class JsonFields
{
    public static JsonElement Field(this JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var found) ? found : default;
    public static double? Number(this JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) ? n : null;
    public static long? Integer(this JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) ? n : null;
    public static string? String(this JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
