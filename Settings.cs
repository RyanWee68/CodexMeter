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
    private static string? ExecutablePath =>
        string.Equals(Path.GetFileName(Environment.ProcessPath), "CodexMeter.exe", StringComparison.OrdinalIgnoreCase)
            ? Environment.ProcessPath
            : null;

    public static void EnsureAutoStart()
    {
        try
        {
            if (!Load().AutoStartDisabled && !AutoStart && ExecutablePath != null) AutoStart = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }

    public static bool AutoStart
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("CodexMeter") != null; }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value && ExecutablePath != null) key.SetValue("CodexMeter", $"\"{ExecutablePath}\"");
            else key.DeleteValue("CodexMeter", false);
            (Load() with { AutoStartDisabled = !value }).Save();
        }
    }
}
