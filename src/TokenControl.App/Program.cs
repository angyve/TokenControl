namespace TokenControl.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\TokenControl.SingleInstance", out var isFirst);
        if (!isFirst) return;

        // Leave a trace in activity.log if the app ever disappears unexpectedly.
        Application.ThreadException += (_, e) => AppLog.Write($"ui error {e.Exception.GetType().Name}: {e.Exception.Message}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write($"crash {e.ExceptionObject.GetType().Name}");
        Microsoft.Win32.SystemEvents.SessionEnding += (_, e) => AppLog.Write($"session ending ({e.Reason})");

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
        AppLog.Write("exited");
    }
}
