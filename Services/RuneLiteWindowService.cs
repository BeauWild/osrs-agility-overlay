using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OSRSAgilityOverlay.Services;

public sealed class RuneLiteWindowService
{
    public Rectangle TargetRect { get; private set; } = Rectangle.Empty;

    public Rectangle Update(string titleContains, bool anchorToRuneLite, Rectangle fallback)
    {
        if (!anchorToRuneLite)
        {
            TargetRect = new Rectangle(0, 0, fallback.Width, fallback.Height);
            return TargetRect;
        }

        IntPtr hwnd = FindTargetWindow(titleContains);
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT rect))
            TargetRect = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);

        return TargetRect;
    }

    private static IntPtr FindTargetWindow(string titleContains)
    {
        titleContains = string.IsNullOrWhiteSpace(titleContains) ? "RuneLite" : titleContains;

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero &&
                    process.MainWindowTitle.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                    return process.MainWindowHandle;

                if (process.MainWindowHandle != IntPtr.Zero &&
                    process.MainWindowTitle.Contains("Old School RuneScape", StringComparison.OrdinalIgnoreCase))
                    return process.MainWindowHandle;
            }
            catch { }
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
