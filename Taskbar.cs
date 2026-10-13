using System.Runtime.InteropServices;

namespace CodexMeter;

internal static class Taskbar
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    public static void Place(Form form)
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == 0 || !GetWindowRect(tray, out var r)) { form.Hide(); return; }
        var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        var screen = Screen.FromRectangle(bounds).Bounds;
        var visible = Rectangle.Intersect(bounds, screen);
        var foreground = GetForegroundWindow();
        var desktop = FindWindow("Progman", null);
        var fullscreen = foreground != tray && foreground != desktop && foreground != form.Handle &&
            GetWindowRect(foreground, out var f) && f.Left <= screen.Left && f.Top <= screen.Top && f.Right >= screen.Right && f.Bottom >= screen.Bottom;
        if (visible.Height < 8 || visible.Width < 8 || fullscreen) { form.Hide(); return; }
        var scale = form.DeviceDpi / 96f;
        var width = (int)(MonitorForm.PanelWidth * scale);
        var height = (int)(MonitorForm.PanelHeight * scale);
        var x = bounds.Left + (int)(4 * scale);
        var y = bounds.Top;
        if (bounds.Width > bounds.Height) height = Math.Min(height, bounds.Height);
        else { x = bounds.Right; y = bounds.Bottom - height; }
        // shortcut: Windows 11 offers no public deskband API; overlay the left edge until a supported host exists.
        if (!form.Visible) form.Show();
        SetWindowPos(form.Handle, new nint(-1), x, y, width, height, 0x0010 | 0x0040);
    }
}
