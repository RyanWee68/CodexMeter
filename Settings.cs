using System.Text.Json;
using Microsoft.Win32;

namespace CodexMeter;

internal sealed record Settings(string? CodexPath = null)
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
    public static bool AutoStart
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("CodexMeter") != null; }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("CodexMeter", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("CodexMeter", false);
        }
    }
}
