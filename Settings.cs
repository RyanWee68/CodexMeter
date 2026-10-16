using System.Text.Json;
using Microsoft.Win32;

namespace CodexMeter;

internal sealed record Settings(string? CodexPath = null, bool AutoStartDisabled = false)
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMeter", "settings.json");
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this));
        File.Move(temp, FilePath, true);
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string ShortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "CodexMeter.lnk");
    private static string? ExecutablePath =>
        string.Equals(Path.GetFileName(Environment.ProcessPath), "CodexMeter.exe", StringComparison.OrdinalIgnoreCase)
            ? Environment.ProcessPath
            : null;

    public static void EnsureAutoStart()
    {
        try
        {
            if (!Load().AutoStartDisabled && ExecutablePath != null) AutoStart = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
        }
    }

    public static bool AutoStart
    {
        get
        {
            if (File.Exists(ShortcutPath)) return true;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("CodexMeter") != null;
        }
        set
        {
            if (value)
            {
                var exe = ExecutablePath ?? throw new InvalidOperationException("请运行已发布的 CodexMeter.exe。");
                var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("无法创建开机启动快捷方式。");
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(ShortcutPath);
                shortcut.TargetPath = exe;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
                shortcut.Save();
            }
            else File.Delete(ShortcutPath);
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue("CodexMeter", false);
            (Load() with { AutoStartDisabled = !value }).Save();
        }
    }
}
