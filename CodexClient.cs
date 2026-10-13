using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexMeter;

// The official CLI owns login and token refresh; no credentials are persisted by this app.
internal sealed class CodexClient : IDisposable
{
    private readonly Process process;
    private int nextId;

    public static string FindExecutable()
    {
        var configured = Settings.Load().CodexPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var local = Path.Combine(AppContext.BaseDirectory, "codex.exe");
        if (File.Exists(local)) return local;
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex", "node_modules", "@openai");
        if (Directory.Exists(npm))
        {
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
            var package = Path.Combine(npm, $"codex-win32-{arch}", "vendor");
            if (Directory.Exists(package))
            {
                var exe = Directory.EnumerateFiles(package, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (exe != null) return exe;
            }
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(dir.Trim('"'), "codex.exe");
            if (File.Exists(path)) return path;
        }
        throw new InvalidOperationException("未找到 Codex CLI，请在设置中选择 codex.exe。");
    }

    public CodexClient()
    {
        process = new Process { StartInfo = new ProcessStartInfo(FindExecutable(), "app-server")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        }};
        process.Start();
        // Drain diagnostics without persisting potentially sensitive server output.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
    }

    public async Task Initialize(CancellationToken ct)
    {
        await Call("initialize", new { clientInfo = new { name = "codex_meter", title = "Codex 用量条", version = "1.0.0" } }, ct);
        await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}".AsMemory(), ct);
        await process.StandardInput.FlushAsync(ct);
    }

    public async Task<JsonElement> Call(string method, object? parameters, CancellationToken ct)
    {
        var id = ++nextId;
        var line = JsonSerializer.Serialize(new { id, method, @params = parameters });
        await process.StandardInput.WriteLineAsync(line.AsMemory(), ct);
        await process.StandardInput.FlushAsync(ct);
        while (true)
        {
            using var doc = await Read(ct);
            var root = doc.RootElement;
            if (root.Field("id").Integer() != id) continue;
            if (root.Field("error").ValueKind == JsonValueKind.Object)
            {
                var message = root.Field("error").Field("message").String() ?? "";
                var hints = new[] { "401", "403", "404", "429", "attestation", "refresh", "not authenticated", "timed out", "unexpected", "connect", "certificate" };
                var hint = string.Join(", ", hints.Where(x => message.Contains(x, StringComparison.OrdinalIgnoreCase)));
                throw new InvalidOperationException($"Codex {method} 请求失败（{root.Field("error").Field("code").Integer()} {hint}），请检查登录状态、网络或 CLI 版本。");
            }
            return root.GetProperty("result").Clone();
        }
    }

    private async Task<JsonDocument> Read(CancellationToken ct)
    {
        var line = await process.StandardOutput.ReadLineAsync(ct);
        if (line == null) throw new IOException("Codex 服务已退出，请检查 CLI 和登录状态。");
        return JsonDocument.Parse(line);
    }

    public async Task Login(CancellationToken ct)
    {
        var response = await Call("account/login/start", new { type = "chatgpt" }, ct);
        var uri = new Uri(response.GetProperty("authUrl").GetString()!);
        if (uri.Scheme != "https" || !(uri.Host == "auth.openai.com" || uri.Host == "chatgpt.com"))
            throw new InvalidOperationException("授权网址不属于 OpenAI，已停止打开。");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        while (true)
        {
            using var doc = await Read(ct);
            if (doc.RootElement.Field("method").String() != "account/login/completed") continue;
            if (doc.RootElement.Field("params").Field("success").ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("登录未完成，请重试。");
            return;
        }
    }

    public void Dispose()
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        process.Dispose();
    }
}
