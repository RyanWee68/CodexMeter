using System.Text.Json;

namespace CodexMeter;

internal static class SelfTest
{
    public static void Run()
    {
        var count = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAILED: " + name);
            count++;
        }
        Usage Parse(string json) { using var doc = JsonDocument.Parse(json); return Usage.Parse(doc.RootElement); }
        var usage = Parse("""
            {"rateLimits":{"primary":{"usedPercent":99,"windowDurationMins":300}},
             "rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":6,"windowDurationMins":10080,"resetsAt":2000000000},"secondary":{"usedPercent":39,"windowDurationMins":300},"credits":{"balance":"12.5"}}},
             "rateLimitResetCredits":{"availableCount":3,"credits":[{"status":"available","expiresAt":2000000000},{"status":"available","expiresAt":1900000000},{"status":"used","expiresAt":1000000000}]}}
            """);
        Check(usage.FiveHour.Remaining == 61, "select five-hour by duration, prefer codex bucket");
        Check(usage.Weekly.Remaining == 94, "select weekly by duration");
        Check(usage.ResetCount == 3, "count authoritative over detail list");
        Check(usage.ResetExpiry == 1900000000, "earliest available expiry");
        Check(Parse("{}").FiveHour.Remaining == null && Parse("{}").ResetCount == null, "missing is not zero");
        Check(Parse("""{"rateLimits":{"primary":{"usedPercent":101,"windowDurationMins":300}}} """).FiveHour.Remaining == 0, "clamp remaining");
        Check(Parse("""{"rateLimits":{"primary":{"usedPercent":5,"windowDurationMins":15}}} """).FiveHour.Remaining == null, "do not relabel other windows");
        Check(Parse("""{"rateLimitResetCredits":{"availableCount":0,"credits":[]}}""").ResetCount == 0, "real zero");
        var now = DateTimeOffset.FromUnixTimeSeconds(1000000);
        Check(new Quota(61, 1015000).Countdown(now) == "4h10m", "hour countdown");
        Check(new Quota(94, 1601200).Countdown(now) == "6d23h", "day countdown");
        Check(new Quota(0, 1).Countdown(now) == "待刷新", "expired does not imply quota restored");
        var cache = new ResetExpiryCache(null);
        var known = new Usage(new(61, null), new(94, null), 3, now.AddDays(10).ToUnixTimeSeconds());
        var missing = known with { ResetExpiry = null, FiveHour = new(42, null) };
        Check(cache.Apply("account-a", missing, true, now).ResetExpiry == null, "no invented expiry before first detail read");
        cache.Apply("account-a", known, false, now);
        var fallback = cache.Apply("account-a", missing, true, now.AddMinutes(5));
        Check(fallback.ResetExpiry == known.ResetExpiry && cache.IsCached, "missing details reuse same-account same-count expiry");
        Check(fallback.FiveHour.Remaining == 42, "fallback does not overwrite fresh quota");
        Check(cache.ConfirmedAt == now, "fallback does not extend cache lifetime");
        Check(cache.Apply("account-a", missing, true, now.AddHours(1)).ResetExpiry == null, "expiry confirmation ages out after one hour");
        cache.Apply("account-a", known, false, now);
        Check(cache.Apply("account-b", missing, true, now).ResetExpiry == null, "account switch invalidates cache");
        cache.Apply("account-a", known, false, now);
        cache.SelectAccount("account-b");
        cache.SelectAccount("account-a");
        Check(cache.Apply("account-a", missing, true, now).ResetExpiry == null, "account switch clears cache even if quota fetch fails");
        cache.Apply("account-a", known, false, now);
        Check(cache.Apply("account-a", missing with { ResetCount = 2 }, true, now).ResetExpiry == null, "count change invalidates cache");
        Check(cache.Apply("account-a", missing, true, now).ResetExpiry == null, "old count returning does not resurrect invalidated cache");
        cache.Apply("account-a", known, false, now);
        Check(cache.Apply("account-a", missing, false, now).ResetExpiry == null, "explicit detail result without expiry invalidates cache");
        cache.Apply("account-a", known, false, now);
        Check(cache.Apply(null, missing, true, now).ResetExpiry == null, "unidentified account cannot reuse cache");
        cache.Apply("account-a", known, false, now);
        Check(cache.Apply("account-a", missing with { ResetCount = 0 }, true, now).ResetExpiry == null, "zero credits clears expiry");
        Check(cache.Apply("account-a", known, false, now).ResetExpiry == known.ResetExpiry && !cache.IsCached, "fresh details supersede fallback");
        Check(cache.Apply("account-a", missing, true, now.AddDays(10)).ResetExpiry == null, "expired card cannot be retained");
        Check(cache.Apply("account-a", known with { ResetExpiry = now.AddSeconds(-1).ToUnixTimeSeconds() }, false, now).ResetExpiry == null, "expired upstream detail is not displayed");
        using var identityA = JsonDocument.Parse("""{"email":"USER@example.com","type":"chatgpt","planType":"pro"}""");
        using var identityB = JsonDocument.Parse("""{"planType":"pro","type":"chatgpt","email":"user@example.com"}""");
        Check(ResetExpiryCache.Identity(identityA.RootElement) == ResetExpiryCache.Identity(identityB.RootElement), "account identity is stable across JSON order and email case");
        Directory.CreateDirectory("artifacts");
        var cacheFile = Path.GetFullPath(Path.Combine("artifacts", $"cache-test-{Guid.NewGuid():N}.json"));
        try
        {
            new ResetExpiryCache(cacheFile).Apply("account-a", known, false, now);
            Check(!File.ReadAllText(cacheFile).Contains("account-a"), "disk cache does not store account identity in plaintext");
            var restarted = new ResetExpiryCache(cacheFile);
            Check(restarted.Apply("account-a", missing, true, now.AddMinutes(5)).ResetExpiry == known.ResetExpiry && restarted.IsCached, "restart restores confirmed expiry when details missing");
            Check(restarted.ConfirmedAt == now, "restart preserves original confirmation time");
            Check(new ResetExpiryCache(cacheFile).Apply("account-a", missing, true, now.AddHours(1)).ResetExpiry == null, "disk cache expires after one hour");
            new ResetExpiryCache(cacheFile).Apply("account-a", known, false, now);
            Check(new ResetExpiryCache(cacheFile).Apply("account-b", missing, true, now).ResetExpiry == null && !File.Exists(cacheFile), "switching account clears persisted date");
            new ResetExpiryCache(cacheFile).Apply("account-a", known, false, now);
            new ResetExpiryCache(cacheFile).Apply("account-a", missing with { ResetCount = 2 }, true, now);
            Check(new ResetExpiryCache(cacheFile).Apply("account-a", missing, true, now).ResetExpiry == null, "invalidated disk cache cannot return after restart");
            new ResetExpiryCache(cacheFile).Apply("account-a", known, false, now.AddMinutes(1));
            Check(new ResetExpiryCache(cacheFile).Apply("account-a", missing, true, now).ResetExpiry == null, "future confirmation timestamp is discarded");
            File.WriteAllText(cacheFile, "broken-json");
            Check(new ResetExpiryCache(cacheFile).Apply("account-a", missing, true, now).ResetExpiry == null, "corrupt cache does not break refresh");
        }
        finally { File.Delete(cacheFile); File.Delete(cacheFile + ".tmp"); }
        Usage Backend(string json) { using var doc = JsonDocument.Parse(json); return ResetCreditDetails.Parse(missing, doc.RootElement); }
        var direct = Backend("""
            {"available_count":3,"credits":[
              {"status":"available","expires_at":"2026-10-22T19:05:16.935879Z"},
              {"status":"available","expires_at":"2026-10-29T18:35:55Z"},
              {"status":"redeemed","expires_at":"2026-01-01T00:00:00Z"}]}
            """);
        Check(direct.ResetExpiry == 1792695916 && direct.ResetCount == 3, "direct details parse fractional UTC and ignore redeemed cards");
        Check(direct.FiveHour.Remaining == missing.FiveHour.Remaining, "direct details preserve independently fetched quota");
        Check(Backend("""{"available_count":1,"credits":[{"status":"available","expires_at":null}]}""").ResetExpiry == null, "direct details accept explicit nullable expiry");
        bool RejectBackend(string json) { try { Backend(json); return false; } catch (JsonException) { return true; } }
        Check(RejectBackend("""{"available_count":3,"credits":null}"""), "incomplete direct response cannot clear a known date");
        Check(RejectBackend("""{"available_count":-1,"credits":[]}"""), "invalid direct count rejected");
        Check(RejectBackend("""{"available_count":1,"credits":[{"status":"available","expires_at":"bad-date"}]}"""), "invalid direct date rejected");
        Check(ResetCreditDetails.MatchesAccount(identityA.RootElement, identityB.RootElement), "credential email binding ignores case");
        using var anotherAccount = JsonDocument.Parse("""{"email":"other@example.com"}""");
        Check(!ResetCreditDetails.MatchesAccount(identityA.RootElement, anotherAccount.RootElement), "changed credential account cannot supply details");
        using var emptyClaims = JsonDocument.Parse("{}");
        Check(!ResetCreditDetails.MatchesAccount(emptyClaims.RootElement, emptyClaims.RootElement), "missing credential identity cannot pass binding");
        Directory.CreateDirectory("artifacts");
        File.WriteAllText("artifacts/self-test.txt", $"PASS: {count} checks");
    }
}
