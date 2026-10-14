using System.Text.Json;

namespace CodexMeter;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) { SelfTest.Run(); return; }
        if (args.Contains("--probe")) { Probe().GetAwaiter().GetResult(); return; }
        if (args.Contains("--preview"))
        {
            Directory.CreateDirectory("artifacts");
            using var form = new MonitorForm(preview: true);
            using var image = new Bitmap(MonitorForm.PanelWidth * 2, MonitorForm.PanelHeight * 2);
            form.Size = image.Size;
            form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.MakeTransparent(form.TransparencyKey);
            image.Save("artifacts/preview.png");
            return;
        }
        using var mutex = new Mutex(true, @"Local\CodexMeter", out var first);
        if (!first) return;
        Settings.EnsureAutoStart();
        using var monitor = new MonitorForm();
        using var smokeTimer = new System.Windows.Forms.Timer { Interval = 50000 };
        if (args.Contains("--smoke"))
        {
            smokeTimer.Tick += (_, _) => { smokeTimer.Stop(); monitor.SaveSmokeResult(); monitor.Close(); };
            smokeTimer.Start();
        }
        Application.Run(monitor);
    }

    private static async Task Probe()
    {
        Directory.CreateDirectory("artifacts");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            using var client = new CodexClient();
            await client.Initialize(timeout.Token);
            var auth = await client.Call("account/read", new { refreshToken = false }, timeout.Token);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var response = await client.Call("account/rateLimits/read", null, timeout.Token);
            var usage = Usage.Parse(response);
            var rows = response.Field("rateLimitResetCredits").Field("credits");
            var detailSource = "app-server";
            if (rows.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined && usage.ResetCount is > 0)
            {
                using var detailTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                detailTimeout.CancelAfter(TimeSpan.FromSeconds(25));
                var details = await ResetCreditDetails.Read(auth.Field("account"), response, usage, detailTimeout.Token);
                if (details != null) { usage = details; detailSource = "direct-details"; }
                else detailSource = "unavailable";
            }
            var detailRows = rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray().Select(row => new
            {
                status = row.Field("status").String(), expiresAtKind = row.Field("expiresAt").ValueKind.ToString(),
                expiresAt = row.Field("expiresAt").Integer()
            }).ToArray() : null;
            // Only derived quota data is exported: no account email, IDs or credentials.
            File.WriteAllText("artifacts/probe.json", JsonSerializer.Serialize(new { success = true, authType = auth.Field("account").Field("type").String(),
                hasEmail = !string.IsNullOrWhiteSpace(auth.Field("account").Field("email").String()),
                elapsedSeconds = watch.Elapsed.TotalSeconds, detailsKind = rows.ValueKind.ToString(), detailSource, detailRows, usage }));
        }
        catch (Exception e)
        {
            File.WriteAllText("artifacts/probe.json", JsonSerializer.Serialize(new { success = false, errorType = e.GetType().Name, message = e is InvalidOperationException ? e.Message : "Probe failed" }));
            Environment.ExitCode = 1;
        }
    }
}
