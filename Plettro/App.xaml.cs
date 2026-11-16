using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Plettro;

public partial class App : Application
{
    static Mutex? singleInstance;

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Plettro", "errori.log");

    /// <summary>Appends an error to %AppData%\Plettro\errori.log (thread-safe, never throws).</summary>
    public static void LogError(string where, Exception ex)
    {
        try
        {
            lock (typeof(App))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 512 * 1024) info.Delete();
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}: {ex}\n\n");
            }
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Only one Plettro at a time: a second copy could not open the Scarlett (ASIO)
        // and the two would overwrite each other's settings. Bring the first one forward instead.
        singleInstance = new Mutex(true, "Plettro.SingleInstance.v1", out bool first);
        if (!first)
        {
            var me = Process.GetCurrentProcess();
            var other = Process.GetProcessesByName(me.ProcessName).FirstOrDefault(p => p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero);
            if (other != null)
            {
                ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(other.MainWindowHandle);
            }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            LogError("interfaccia", args.Exception);
            MessageBox.Show("Si è verificato un errore:\n\n" + args.Exception.Message, "Plettro",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) LogError("errore grave", ex);
        };

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows is shutting down / logging off with Plettro open: keep the settings
        (MainWindow as MainWindow)?.SaveSettings();
        base.OnSessionEnding(e);
    }
}
