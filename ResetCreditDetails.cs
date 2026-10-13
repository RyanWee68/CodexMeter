using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CodexMeter;

internal static class ResetCreditDetails
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 1024 * 1024 };

    public static async Task<Usage?> Read(JsonElement account, JsonElement usageResponse, Usage current, CancellationToken ct)
    {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var path = Path.Combine(home, "auth.json");
        if (!File.Exists(path)) return null;
        using var auth = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        if (auth.RootElement.Field("auth_mode").String() != "chatgpt") return null;
        var tokens = auth.RootElement.Field("tokens");
        var token = tokens.Field("access_token").String();
        var idToken = tokens.Field("id_token").String();
        var accountId = tokens.Field("account_id").String();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(accountId)) return null;
        var expectedId = usageResponse.Field("accountId").String();
        if (expectedId != null && expectedId != accountId) return null;
        var parts = idToken.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            using var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '='))));
            if (!MatchesAccount(claims.RootElement, account)) return null;
        }
        catch (Exception e) when (e is FormatException or JsonException) { return null; }

        // The CLI caps this detail request at 5 seconds; allow the caller's longer timeout.
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/rate-limit-reset-credits");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("ChatGPT-Account-Id", accountId);
        request.Headers.UserAgent.ParseAdd("codex_meter/1.0.0");
        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var details = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return Parse(current, details.RootElement);
    }

    internal static bool MatchesAccount(JsonElement claims, JsonElement account)
    {
        var email = claims.Field("email").String();
        return !string.IsNullOrWhiteSpace(email) &&
            string.Equals(email.Trim(), account.Field("email").String()?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    internal static Usage Parse(Usage current, JsonElement details)
    {
        var count = details.Field("available_count").Integer();
        var rows = details.Field("credits");
        if (count is null or < 0 or > int.MaxValue || rows.ValueKind != JsonValueKind.Array)
            throw new JsonException("Invalid reset credit details.");
        long? expiry = null;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.Field("status").String() != "available") continue;
            var date = row.Field("expires_at");
            if (date.ValueKind == JsonValueKind.Null) continue;
            if (!DateTimeOffset.TryParse(date.String(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                throw new JsonException("Invalid reset credit expiry.");
            var at = parsed.ToUnixTimeSeconds();
            if (expiry == null || at < expiry) expiry = at;
        }
        return current with { ResetCount = (int)count.Value, ResetExpiry = expiry };
    }
}
