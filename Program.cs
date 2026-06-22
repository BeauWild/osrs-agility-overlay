using OSRSAgilityOverlay.Forms;

namespace OSRSAgilityOverlay;

internal static class Program
{
    public const string AppVersion = "v0.1.0";

    [STAThread]
    private static void Main()
    {
        Application.ThreadException += (_, e) => WriteCrashLog(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) WriteCrashLog(ex);
        };

        static void WriteCrashLog(Exception ex)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "crash.log");
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {AppVersion}\r\n{ex}\r\n\r\n");
            }
            catch { }
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new OverlayForm());
    }
}
