using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace SpeedExplorer;

static class Program
{
    private static Mutex _mutex = new Mutex(true, "SpeedExplorerSingleInstanceMutex");
    private static NativeUnhandledExceptionFilter? _nativeCrashFilter;
    private static string? _diagnosticsDir;
    private static readonly object _diagLock = new();

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr SetUnhandledExceptionFilter(NativeUnhandledExceptionFilter lpTopLevelExceptionFilter);

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        int processId,
        IntPtr hFile,
        MINIDUMP_TYPE dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    private delegate int NativeUnhandledExceptionFilter(IntPtr exceptionInfo);

    [Flags]
    private enum MINIDUMP_TYPE : uint
    {
        MiniDumpNormal = 0x00000000,
        MiniDumpWithThreadInfo = 0x00001000,
        MiniDumpWithUnloadedModules = 0x00000020,
        MiniDumpWithIndirectlyReferencedMemory = 0x00000040
    }

    [STAThread]
    static void Main(string[] args)
    {
        args ??= Array.Empty<string>();
        LogCrash("Program.Main", null, $"START args=\"{string.Join(" ", args)}\"");
        if (args.Any(FileManagerIntegrationService.IsApplyArg))
        {
            LogCrash("Program.Main", null, "ApplyFromCommandLine(true)");
            FileManagerIntegrationService.ApplyFromCommandLine(true);
            return;
        }
        if (args.Any(FileManagerIntegrationService.IsRemoveArg))
        {
            LogCrash("Program.Main", null, "ApplyFromCommandLine(false)");
            FileManagerIntegrationService.ApplyFromCommandLine(false);
            return;
        }

        if (!_mutex.WaitOne(TimeSpan.Zero, true))
        {
            // Already running, send arguments to the existing instance
            LogCrash("Program.Main", null, "SingleInstance redirect to existing process");
            SendToMainInstance(args);
            return;
        }

        // Delay startup if launched by Windows to avoid slowing down boot
        if (args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase)))
        {
            Thread.Sleep(15000);
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        InstallNativeCrashHandler();

        AppDomain.CurrentDomain.UnhandledException += (s, e) => 
        {
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception, e.ExceptionObject?.ToString());
            WriteMiniDump("appdomain");
            MessageBox.Show(e.ExceptionObject?.ToString() ?? "(null)", "Unhandled Exception");
        };
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            LogCrash("AppDomain.ProcessExit", null, "Process exiting.");
        };
        Application.ThreadException += (s, e) => 
        {
            LogCrash("Application.ThreadException", e.Exception);
            WriteMiniDump("thread");
            MessageBox.Show(e.Exception.ToString(), "Thread Exception");
        };
        Application.ApplicationExit += (s, e) =>
        {
            LogCrash("Application.ApplicationExit", null, "Application exiting.");
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            WriteMiniDump("taskscheduler");
            e.SetObserved();
        };

        try
        {
            var context = new MultiWindowContext();
            
            // Listen for arguments from other instances
            StartPipeListener(context);

            StartupService.SyncWithSettings();

            string? startPath = ExtractStartPathFromArgs(args);
            bool startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            if (IsExplorerShellArgument(startPath))
            {
                LogCrash("Program.Main", null, $"ExplorerShellArg redirect path=\"{startPath}\"");
                try { Process.Start(new ProcessStartInfo("explorer.exe", startPath!) { UseShellExecute = true }); } catch { }
                return;
            }

            if (!startMinimized)
            {
                context.ShowNext(new MainForm(startPath));
            }
            
            Application.Run(context);
            LogCrash("Program.Main", null, "Application.Run returned");
        }
        catch (Exception ex)
        {
            LogCrash("Program.Main", ex);
            MessageBox.Show(ex.ToString(), "Startup Error");
        }
    }

    private static void InstallNativeCrashHandler()
    {
        try
        {
            _nativeCrashFilter = NativeCrashHandler;
            SetUnhandledExceptionFilter(_nativeCrashFilter);
            LogCrash("NativeCrashHandler", null, $"Installed SetUnhandledExceptionFilter. diagnosticsDir=\"{GetDiagnosticsDirectory()}\"");
        }
        catch (Exception ex)
        {
            LogCrash("NativeCrashHandler.InstallFailed", ex);
        }
    }

    private static int NativeCrashHandler(IntPtr exceptionInfo)
    {
        LogCrash("NativeCrashHandler.UnhandledException", null, $"ExceptionPointers=0x{exceptionInfo.ToInt64():X}");
        WriteMiniDump("native");
        // Continue default OS unhandled-exception processing.
        return 0;
    }

    private static void LogCrash(string source, Exception? ex, string? extra = null)
    {
        try
        {
            var lines = new StringBuilder();
            lines.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}");
            if (!string.IsNullOrWhiteSpace(extra))
                lines.AppendLine(extra);
            else if (ex != null)
                lines.AppendLine(ex.ToString());
            lines.AppendLine();
            File.AppendAllText(GetCrashLogPath(), lines.ToString());
        }
        catch
        {
        }
    }

    private static void WriteMiniDump(string source)
    {
        try
        {
            string dumpPath = Path.Combine(
                GetDiagnosticsDirectory(),
                $"debug_crash_{source}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.dmp");

            using var fs = new FileStream(dumpPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            SafeFileHandle handle = fs.SafeFileHandle;
            var proc = Process.GetCurrentProcess();
            var dumpType = MINIDUMP_TYPE.MiniDumpWithThreadInfo |
                           MINIDUMP_TYPE.MiniDumpWithUnloadedModules |
                           MINIDUMP_TYPE.MiniDumpWithIndirectlyReferencedMemory;

            bool ok = MiniDumpWriteDump(
                proc.Handle,
                proc.Id,
                handle.DangerousGetHandle(),
                dumpType,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            int err = Marshal.GetLastWin32Error();
            LogCrash("MiniDumpWriteDump", null, $"source={source} ok={ok} err={err} path=\"{dumpPath}\"");
        }
        catch (Exception ex)
        {
            LogCrash("MiniDumpWriteDump.Failed", ex, $"source={source}");
        }
    }

    private static string GetCrashLogPath()
        => Path.Combine(GetDiagnosticsDirectory(), "debug_crash.log");

    private static string GetDiagnosticsDirectory()
    {
        lock (_diagLock)
        {
            if (!string.IsNullOrWhiteSpace(_diagnosticsDir) && Directory.Exists(_diagnosticsDir))
                return _diagnosticsDir;

            string[] candidates = new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpeedExplorer"),
                Path.Combine(Path.GetTempPath(), "SpeedExplorer")
            };

            foreach (var dir in candidates)
            {
                if (TryEnsureWritableDirectory(dir))
                {
                    _diagnosticsDir = dir;
                    return dir;
                }
            }

            _diagnosticsDir = AppDomain.CurrentDomain.BaseDirectory;
            return _diagnosticsDir;
        }
    }

    private static bool TryEnsureWritableDirectory(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".diag_probe.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SendToMainInstance(string[] args)
    {
        try
        {
            // Allow the main instance to steal focus
            var currentProcess = Process.GetCurrentProcess();
            var mainProcess = Process.GetProcessesByName(currentProcess.ProcessName)
                .FirstOrDefault(p => p.Id != currentProcess.Id);
            
            if (mainProcess != null)
            {
                AllowSetForegroundWindow((uint)mainProcess.Id);
            }

            var pathArg = ExtractStartPathFromArgs(args) ?? "";
            if (IsExplorerShellArgument(pathArg))
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", pathArg) { UseShellExecute = true }); } catch { }
                return;
            }

            using var client = new NamedPipeClientStream(".", "SpeedExplorerPipe", PipeDirection.Out);
            client.Connect(1000);
            using var writer = new StreamWriter(client);
            var payload = args != null && args.Length > 0 ? string.Join("\u001F", args) : pathArg;
            writer.WriteLine(payload);
            writer.Flush();
        }
        catch { }
    }

    private static void StartPipeListener(MultiWindowContext context)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream("SpeedExplorerPipe", PipeDirection.In);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server);
                    var raw = await reader.ReadLineAsync();

                    context.Invoke(() =>
                    {
                        var startPath = ParsePipePayload(raw);
                        if (IsExplorerShellArgument(startPath))
                        {
                            try { Process.Start(new ProcessStartInfo("explorer.exe", startPath!) { UseShellExecute = true }); } catch { }
                            return;
                        }
                        var existing = Application.OpenForms.Cast<Form>()
                            .OfType<MainForm>()
                            .LastOrDefault(f => !f.IsDisposed);
                        if (existing != null)
                        {
                            // Restore if minimized
                            if (existing.WindowState == FormWindowState.Minimized)
                            {
                                existing.WindowState = FormWindowState.Normal;
                            }
                            
                            // Ensure visible first
                            existing.Show();
                            existing.Opacity = 1; 
                            
                            existing.Activate();
                            existing.BringToFront();

                            // Now handle navigation once window is ready
                            existing.HandleExternalPath(startPath);
                        }
                        else
                        {
                            context.ShowNext(new MainForm(startPath));
                        }
                    });
                }
                catch (Exception ex) 
                { 
                    Debug.WriteLine($"Pipe error: {ex.Message}");
                    await Task.Delay(1000); 
                }
            }
        });
    }

    public class MultiWindowContext : ApplicationContext
    {
        private static MultiWindowContext? _instance;
        public static MultiWindowContext Instance => _instance ?? throw new InvalidOperationException("Context not initialized");

        private int _formCount = 0;
        private NotifyIcon _trayIcon = null!;
        private readonly Control _marshalControl;

        public MultiWindowContext()
        {
            _instance = this;
            
            // Create a hidden control to act as a synchronization anchor for the UI thread
            _marshalControl = new Control();
            _marshalControl.CreateControl(); 
            
            InitializeTrayIcon();
        }

        public void Invoke(Action action)
        {
            if (_marshalControl.InvokeRequired)
            {
                _marshalControl.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application, // Fallback to system icon
                Text = "Speed Explorer",
                Visible = AppSettings.Current.ShowTrayIcon
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open Speed Explorer", null, (s, e) => ShowNext(new MainForm()));
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, e) => {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                ExitThread();
            });

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => ShowNext(new MainForm());

            // Try to load app icon if possible
            try
            {
                string exePath = Application.ExecutablePath;
                _trayIcon.Icon = Icon.ExtractAssociatedIcon(exePath);
            }
            catch { }
        }

        public void SetTrayIconVisible(bool visible)
        {
            if (_trayIcon == null) return;
            _trayIcon.Visible = visible;
        }

        public void ShowNext(Form form)
        {
            // Find reference window for cascading offset
            // We use LastOrDefault to get the most recently active window
            var referenceForm = Application.OpenForms.Cast<Form>().LastOrDefault(f => f.Visible && f.WindowState == FormWindowState.Normal);
            
            if (referenceForm != null)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(referenceForm.Location.X + 40, referenceForm.Location.Y + 40);
            }

            _formCount++;
            form.FormClosed += (s, e) =>
            {
                _formCount--;
                // Don't exit thread if we have tray icon and want to stay in background
                // We only exit when the user explicitly clicks Exit from the tray
            };
            form.Show();
            form.Activate();    // Pull focus
            form.BringToFront(); // Force to top of Z-order
        }
    }

    internal static string? ExtractStartPathFromArgs(string[] args)
    {
        if (args == null || args.Length == 0) return null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg)) continue;
            if (arg.StartsWith("--", StringComparison.OrdinalIgnoreCase)) continue;

            var lower = arg.ToLowerInvariant();
            if (lower.StartsWith("/select") || lower.StartsWith("-select"))
            {
                int comma = arg.IndexOf(',');
                if (comma >= 0 && comma + 1 < arg.Length)
                    return arg.Substring(comma + 1).Trim().Trim('"');

                // Handle /select:"path" or /select:path or /select=path
                int sep = arg.IndexOf(':');
                if (sep < 0) sep = arg.IndexOf('=');
                if (sep >= 0 && sep + 1 < arg.Length)
                    return arg.Substring(sep + 1).Trim().Trim('"');

                if (i + 1 < args.Length)
                    return args[i + 1].Trim().Trim('"');
                return null;
            }

            if (lower.StartsWith("/e,") || lower.StartsWith("/root,") || lower.StartsWith("/root"))
            {
                int comma = arg.IndexOf(',');
                if (comma >= 0 && comma + 1 < arg.Length)
                    return arg.Substring(comma + 1).Trim().Trim('"');
                if (i + 1 < args.Length)
                    return args[i + 1].Trim().Trim('"');
                return null;
            }

            return arg.Trim().Trim('"');
        }

        return null;
    }

    internal static string? ExtractStartPathFromSingleArg(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return null;
        return ExtractStartPathFromArgs(new[] { arg });
    }

    private static string? ParsePipePayload(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.Contains("\u001F"))
        {
            var parts = raw.Split('\u001F', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
                return ExtractStartPathFromArgs(parts);
        }
        return ExtractStartPathFromSingleArg(raw);
    }

    internal static bool IsExplorerShellArgument(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var p = path.Trim();
        if (p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.StartsWith("::", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
