using Microsoft.Win32;

namespace CodexMeter;

internal sealed class MonitorForm : Form
{
    internal const int PanelWidth = 262;
    internal const int PanelHeight = 48;
    private Usage usage = Usage.Empty;
    private DateTimeOffset? refreshed;
    private string status = "正在读取…";
    private string account = "正在检测本机 Codex 登录状态";
    private bool busy;
    private bool hasChatGptAccount;
    private readonly ResetExpiryCache resetExpiryCache = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMeter", "reset-expiry.json"));
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 300000 };
    private readonly System.Windows.Forms.Timer positionTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer clickTimer = new() { Interval = SystemInformation.DoubleClickTime };
    private readonly NotifyIcon tray;
    private readonly ToolTip tip = new() { AutoPopDelay = 20000 };
    private Form? settingsForm;

    public MonitorForm(bool preview = false)
    {
        Text = "Codex 用量条";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new(PanelWidth, PanelHeight);
        DoubleBuffered = true;
        BackColor = Color.Magenta;
        TransparencyKey = BackColor;
        AccessibleName = "Codex 用量；单击刷新，双击设置";
        Cursor = Cursors.Hand;
        var menu = new ContextMenuStrip();
        menu.Items.Add("立即刷新", null, async (_, _) => await RefreshUsage());
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add("退出", null, (_, _) => Close());
        ContextMenuStrip = menu;
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Codex 用量条", ContextMenuStrip = menu, Visible = !preview };
        tray.DoubleClick += (_, _) => OpenSettings();
        refreshTimer.Tick += async (_, _) => await RefreshUsage();
        positionTimer.Tick += (_, _) => { Taskbar.Place(this); Invalidate(); };
        clickTimer.Tick += async (_, _) => { clickTimer.Stop(); await RefreshUsage(); };
        MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) clickTimer.Start(); };
        MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) { clickTimer.Stop(); OpenSettings(); } };
        if (preview)
        {
            usage = new(new(61, DateTimeOffset.Now.AddHours(4).AddMinutes(10).ToUnixTimeSeconds()), new(94, DateTimeOffset.Now.AddDays(6).AddHours(23).ToUnixTimeSeconds()), 3, DateTimeOffset.Now.AddDays(10).ToUnixTimeSeconds());
            refreshed = DateTimeOffset.Now.AddMinutes(-5);
            status = "";
        }
        else Shown += async (_, _) =>
        {
            Taskbar.Place(this);
            refreshTimer.Start(); positionTimer.Start();
            await RefreshUsage();
        };
    }

    protected override bool ShowWithoutActivation => true;

    internal void SaveSmokeResult()
    {
        Directory.CreateDirectory("artifacts");
        using var image = new Bitmap(Width, Height);
        DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.MakeTransparent(TransparencyKey);
        image.Save("artifacts/live-panel.png");
        File.WriteAllText("artifacts/smoke.json", System.Text.Json.JsonSerializer.Serialize(new
        {
            visible = Visible, bounds = new { Left, Top, Width, Height }, dpi = DeviceDpi,
            refreshed = refreshed.HasValue, status, usage,
            resetExpiryCached = resetExpiryCache.IsCached, resetExpiryConfirmedAt = resetExpiryCache.ConfirmedAt
        }));
    }
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; return cp; }
    }

    private async Task RefreshUsage()
    {
        if (busy || lifetime.IsCancellationRequested) return;
        busy = true; status = "刷新中"; Invalidate();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(40));
            using var client = new CodexClient();
            await client.Initialize(timeout.Token);
            var auth = await client.Call("account/read", new { refreshToken = false }, timeout.Token);
            var a = auth.Field("account");
            hasChatGptAccount = a.Field("type").String() is "chatgpt" or "chatgptAuthTokens";
            if (!hasChatGptAccount)
            {
                account = "未检测到 ChatGPT 订阅登录；请双击设置进行授权。";
                usage = Usage.Empty; refreshed = null;
                resetExpiryCache.Apply(null, usage, false, DateTimeOffset.Now);
                throw new InvalidOperationException("请先登录");
            }
            account = $"{a.Field("email").String() ?? "已登录"} · {a.Field("planType").String() ?? "ChatGPT"}";
            var identity = ResetExpiryCache.Identity(a);
            resetExpiryCache.SelectAccount(identity);
            // Discard another account's values before fetching the current account.
            usage = Usage.Empty; refreshed = null;
            var response = await client.Call("account/rateLimits/read", null, timeout.Token);
            bool DetailsMissing(System.Text.Json.JsonElement result) =>
                result.Field("rateLimitResetCredits").Field("credits").ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined;
            usage = resetExpiryCache.Apply(identity, Usage.Parse(response), DetailsMissing(response), DateTimeOffset.Now);
            refreshed = DateTimeOffset.Now;
            status = "";
            Invalidate();
            if (usage.ResetCount is > 0 && DetailsMissing(response))
            {
                // Preserve the successful quota read even if the detail-only retry fails.
                using var retry = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                retry.CancelAfter(TimeSpan.FromSeconds(25));
                try
                {
                    await Task.Delay(1000, retry.Token);
                    var details = await ResetCreditDetails.Read(a, response, usage, retry.Token);
                    if (details != null)
                    {
                        usage = resetExpiryCache.Apply(identity, details, false, DateTimeOffset.Now);
                        refreshed = DateTimeOffset.Now;
                    }
                }
                catch (Exception e) when (e is OperationCanceledException or IOException or InvalidOperationException or System.Net.Http.HttpRequestException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or OverflowException) { }
            }
        }
        catch (OperationCanceledException) { status = "刷新超时"; }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or OverflowException)
        { status = e is InvalidOperationException ? e.Message : "刷新失败，请检查设置"; }
        finally
        {
            busy = false;
            if (!IsDisposed)
            {
                var expiryNote = resetExpiryCache.IsCached ? $"\n* 到期日期沿用 {resetExpiryCache.ConfirmedAt:HH:mm} 的记录，等待接口确认（最多沿用一小时）。" : "";
                tip.SetToolTip(this, $"{account}\n{status}\n五小时：{usage.FiveHour.Percent}；每周：{usage.Weekly.Percent}\n重置卡：{usage.ResetCount?.ToString() ?? "接口未提供"}{expiryNote}\n每 5 分钟刷新；单击刷新，双击设置，右键退出。");
                AccessibleDescription = tip.GetToolTip(this);
                Invalidate();
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.ScaleTransform(Width / (float)PanelWidth, Height / (float)PanelHeight);
        // Avoid blending text edges with the transparency key.
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        using var font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var theme = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var dark = theme?.GetValue("SystemUsesLightTheme") is int light && light == 0;
        using var ink = new SolidBrush(dark ? Color.FromArgb(235, 240, 247) : Color.FromArgb(42, 64, 95));
        using var muted = new SolidBrush(dark ? Color.FromArgb(185, 197, 213) : Color.FromArgb(103, 115, 134));
        using var track = new SolidBrush(Color.FromArgb(225, 230, 236));
        using var accent = new SolidBrush(Color.FromArgb(0, 155, 181));
        using var warning = new SolidBrush(Color.FromArgb(208, 117, 35));
        using var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        void TextAt(string text, float x, float y, float width, Brush? brush = null) =>
            g.DrawString(text, font, brush ?? ink, new RectangleF(x, y, width, 15), format);
        void Row(string label, Quota quota, int y)
        {
            TextAt(label, 5, y, 40);
            g.FillRectangle(track, 48, y + 7, 70, 3);
            if (quota.Remaining is { } n) g.FillRectangle(n < 20 ? warning : accent, 48, y + 7, (float)(70 * n / 100), 3);
            TextAt(quota.Percent, 134, y, 40);
            TextAt(quota.Countdown(DateTimeOffset.Now), 176, y, 84, muted);
        }
        Row("5 小时", usage.FiveHour, 0);
        Row("每周", usage.Weekly, 15);
        TextAt("重置", 5, 30, 40);
        var expiryText = usage.ResetCount == 0 ? "无可用卡" : "到期未知";
        if (usage.ResetExpiry is { } expiry && expiry > DateTimeOffset.Now.ToUnixTimeSeconds() &&
            (!resetExpiryCache.IsCached || resetExpiryCache.ConfirmedAt > DateTimeOffset.Now.AddHours(-1)))
            expiryText = DateTimeOffset.FromUnixTimeSeconds(expiry).ToLocalTime().ToString("M月d日到期") + (resetExpiryCache.IsCached ? "*" : "");
        TextAt(expiryText, 48, 30, 84, muted);
        TextAt(usage.ResetCount is { } count ? $"{count} 次" : "未提供", 134, 30, 40);
        var age = refreshed is { } at ? (int)(DateTimeOffset.Now - at).TotalMinutes : -1;
        var refreshText = status.Length > 0 ? (status == "刷新中" ? "刷新中" : "刷新失败") : age <= 0 ? "刚刚" : $"{age} 分钟前";
        TextAt(refreshText, 176, 30, 64, status.Length > 0 && status != "刷新中" ? warning : muted);
        TextAt("⟳", 244, 30, 16, muted);
    }

    private void OpenSettings()
    {
        if (settingsForm is { IsDisposed: false }) { settingsForm.Activate(); return; }
        var form = new Form { Text = "Codex 用量条 设置", ClientSize = new(510, 330), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = new("Microsoft YaHei UI", 9) };
        settingsForm = form;
        var info = new Label { Text = $"自动跟随本机 Codex 登录账号\n{account}\n\n每 5 分钟自动刷新；单击刷新，双击打开设置。", AutoSize = false, Bounds = new(20, 15, 470, 90) };
        var auto = new CheckBox { Text = "开机自动启动", Checked = Settings.AutoStart, AutoSize = true, Location = new(20, 112) };
        var restoringAuto = false;
        auto.CheckedChanged += (_, _) =>
        {
            if (restoringAuto) return;
            try { Settings.AutoStart = auto.Checked; }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                restoringAuto = true;
                auto.Checked = Settings.AutoStart;
                restoringAuto = false;
                MessageBox.Show(form, "无法修改开机启动设置。", "Codex 用量条");
            }
        };
        var path = new TextBox { Bounds = new(20, 169, 365, 26), Text = Settings.Load().CodexPath ?? "" };
        var pathLabel = new Label { Text = "Codex CLI 路径（留空则自动查找）", AutoSize = true, Location = new(20, 146) };
        var browse = new Button { Text = "选择…", Bounds = new(395, 167, 90, 30) };
        browse.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Codex CLI|codex.exe", Title = "选择官方 Codex CLI" };
            if (dialog.ShowDialog(form) != DialogResult.OK) return;
            try { (Settings.Load() with { CodexPath = dialog.FileName }).Save(); path.Text = dialog.FileName; await RefreshUsage(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(form, "保存设置失败。", "Codex 用量条"); }
        };
        path.ReadOnly = true;
        var login = new Button { Text = "浏览器授权登录", Bounds = new(20, 213, 145, 32), Enabled = !hasChatGptAccount };
        login.Click += async (_, _) =>
        {
            login.Enabled = false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                using var client = new CodexClient();
                await client.Initialize(timeout.Token);
                await client.Login(timeout.Token);
                await RefreshUsage();
                info.Text = $"自动跟随本机 Codex 登录账号\n{account}";
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
            { if (!form.IsDisposed) MessageBox.Show(form, "授权未完成，请检查 CLI 和网络后重试。", "Codex 用量条"); }
            finally { if (!form.IsDisposed) login.Enabled = !hasChatGptAccount; }
        };
        var note = new Label { Text = "授权由官方 Codex CLI 完成，本工具不保存令牌。\n任务栏左侧为覆盖面板；建议任务栏图标居中并关闭小组件。\n重置卡仅展示，接口未提供的数据不会显示为 0。", Bounds = new(20, 260, 475, 65) };
        form.Controls.AddRange([info, auto, pathLabel, path, browse, login, note]);
        form.Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lifetime.Cancel();
            refreshTimer.Dispose(); positionTimer.Dispose(); clickTimer.Dispose();
            tray.Dispose(); tip.Dispose(); settingsForm?.Dispose();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
