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
        NormalizeWorkingDirectory();
        string argsDump = args.Length == 0
            ? "(none)"
            : string.Join(" || ", args.Select((a, i) => $"[{i}]={a}"));
        LogCrash("Program.Main", null, $"START argsCount={args.Length} args=\"{argsDump}\" cwd=\"{Environment.CurrentDirectory}\" exeDir=\"{GetExecutableDirectory()}\"");
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

        // Delay startup only for explicit startup launches (no path payload).
        bool hasStartupFlag = args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        bool hasPathLikeArg = args.Any(a => !string.IsNullOrWhiteSpace(a) && !a.StartsWith("--", StringComparison.Ordinal));
        if (hasStartupFlag && !hasPathLikeArg)
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
                GetExecutableDirectory(),
                AppContext.BaseDirectory,
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
            if (string.IsNullOrWhiteSpace(dir))
                return false;
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

    private static void NormalizeWorkingDirectory()
    {
        try
        {
            string exeDir = GetExecutableDirectory();
            if (string.IsNullOrWhiteSpace(exeDir))
                return;
            if (string.Equals(Environment.CurrentDirectory, exeDir, StringComparison.OrdinalIgnoreCase))
                return;
            Environment.CurrentDirectory = exeDir;
        }
        catch
        {
        }
    }

    private static string GetExecutableDirectory()
    {
        try
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                string? dir = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    return dir;
            }
        }
        catch
        {
        }

        try
        {
            string exePath = Application.ExecutablePath;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                string? dir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    return dir;
            }
        }
        catch
        {
        }

        return AppContext.BaseDirectory;
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

            var swConnect = Stopwatch.StartNew();
            using var client = new NamedPipeClientStream(".", "SpeedExplorerPipe", PipeDirection.Out);
            client.Connect(1000);
            swConnect.Stop();
            LogCrash("Pipe.ClientConnect", null, $"ms={swConnect.ElapsedMilliseconds}");
            using var writer = new StreamWriter(client);
            var payload = args != null && args.Length > 0 ? string.Join("\u001F", args) : pathArg;
            writer.WriteLine(payload);
            writer.Flush();
            LogCrash("Pipe.ClientSend", null, $"payloadLen={(payload?.Length ?? 0)}");
        }
        catch (TimeoutException tex)
        {
            LogCrash("Pipe.ClientTimeout", tex);
        }
        catch (Exception ex)
        {
            LogCrash("Pipe.ClientError", ex);
        }
    }

    private static void StartPipeListener(MultiWindowContext context)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var swWait = Stopwatch.StartNew();
                    using var server = new NamedPipeServerStream("SpeedExplorerPipe", PipeDirection.In);
                    await server.WaitForConnectionAsync();
                    swWait.Stop();
                    LogCrash("Pipe.ServerAccepted", null, $"waitMs={swWait.ElapsedMilliseconds}");
                    using var reader = new StreamReader(server);
                    var raw = await reader.ReadLineAsync();

                    context.Invoke(() =>
                    {
                        var startPath = ParsePipePayload(raw);
                        LogCrash("Pipe.Receive", null, $"raw=\"{raw}\" startPath=\"{startPath}\"");
                        if (IsExplorerShellArgument(startPath))
                        {
                            try { Process.Start(new ProcessStartInfo("explorer.exe", startPath!) { UseShellExecute = true }); } catch { }
                            return;
                        }
                        var existing = GetBestMainFormForExternalOpen();
                        if (existing != null)
                        {
                            LogCrash("Pipe.Target", null, $"targetHandle=0x{existing.Handle.ToInt64():X} visible={existing.Visible} state={existing.WindowState}");
                            RestoreWindowForExternalOpen(existing);
                            
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

    private static MainForm? GetBestMainFormForExternalOpen()
    {
        var forms = Application.OpenForms.Cast<Form>()
            .OfType<MainForm>()
            .Where(f => !f.IsDisposed)
            .ToList();
        if (forms.Count == 0)
            return null;

        if (Form.ActiveForm is MainForm activeMain && !activeMain.IsDisposed)
            return activeMain;

        var focusedVisible = forms.FirstOrDefault(f => f.Visible && (f.Focused || f.ContainsFocus));
        if (focusedVisible != null)
            return focusedVisible;

        var visibleNonMinimized = forms.FirstOrDefault(f => f.Visible && f.WindowState != FormWindowState.Minimized);
        if (visibleNonMinimized != null)
            return visibleNonMinimized;

        var anyVisible = forms.FirstOrDefault(f => f.Visible);
        if (anyVisible != null)
            return anyVisible;

        return forms[^1];
    }

    private static void RestoreWindowForExternalOpen(MainForm form)
    {
        if (form.WindowState != FormWindowState.Minimized)
            return;

        bool shouldMaximize = AppSettings.Current.MainWindowMaximized || AppSettings.Current.MainWindowFullscreen;
        form.WindowState = shouldMaximize ? FormWindowState.Maximized : FormWindowState.Normal;
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
        if (args == null || args.Length == 0)
            return null;

        var tokens = args
            .Where(static a => !string.IsNullOrWhiteSpace(a))
            .Select(static a => a.Trim())
            .ToArray();
        if (tokens.Length == 0)
            return null;

        // Pass 1: explicit /select wins, no matter argument order.
        for (int i = 0; i < tokens.Length; i++)
        {
            if (TryExtractSelectPath(tokens, i, out var selectedPath))
                return selectedPath;
        }

        // Pass 2: other Explorer routing switches (/root, /e).
        for (int i = 0; i < tokens.Length; i++)
        {
            if (TryExtractExplorerOptionPath(tokens, i, out var optionPath))
                return optionPath;
        }

        // Pass 3: best-effort plain path resolution.
        string? firstDirectory = null;
        string? firstFallback = null;
        var unresolvedCandidates = new List<string>();
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (IsSwitchToken(token))
                continue;

            var candidate = NormalizePotentialPath(token);
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (File.Exists(candidate))
                return candidate;

            if (Directory.Exists(candidate))
            {
                firstDirectory ??= candidate;
                continue;
            }

            unresolvedCandidates.Add(candidate);
            firstFallback ??= candidate;
        }

        if (!string.IsNullOrWhiteSpace(firstDirectory) && unresolvedCandidates.Count > 0)
        {
            foreach (var unresolved in unresolvedCandidates)
            {
                if (string.IsNullOrWhiteSpace(unresolved))
                    continue;
                if (IsSwitchToken(unresolved))
                    continue;
                if (Path.IsPathRooted(unresolved))
                    continue;

                var trimmed = unresolved.Trim().Trim('"').TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                var combined = Path.Combine(firstDirectory!, trimmed);
                if (File.Exists(combined) || Directory.Exists(combined))
                    return combined;
            }
        }

        return firstDirectory ?? firstFallback;
    }

    internal static string? ExtractStartPathFromSingleArg(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return null;
        return ExtractStartPathFromArgs(new[] { arg });
    }

    private static bool TryExtractSelectPath(string[] tokens, int index, out string? path)
    {
        path = null;
        if (index < 0 || index >= tokens.Length)
            return false;

        string token = tokens[index];
        int selectIdx = token.IndexOf("/select", StringComparison.OrdinalIgnoreCase);
        if (selectIdx < 0)
            selectIdx = token.IndexOf("-select", StringComparison.OrdinalIgnoreCase);
        if (selectIdx < 0)
            return false;

        string tail = token.Substring(selectIdx);
        if (TryExtractPathFromSwitchTail(tail, ',', tokens, index, out path))
            return true;
        if (TryExtractPathFromSwitchTail(tail, ':', tokens, index, out path))
            return true;
        if (TryExtractPathFromSwitchTail(tail, '=', tokens, index, out path))
            return true;

        string normalizedTail = tail.Trim().Trim('"');
        if ((normalizedTail.Equals("/select", StringComparison.OrdinalIgnoreCase) ||
             normalizedTail.Equals("-select", StringComparison.OrdinalIgnoreCase) ||
             normalizedTail.EndsWith(",", StringComparison.Ordinal)) &&
            index + 1 < tokens.Length)
        {
            var next = NormalizePotentialPath(tokens[index + 1]);
            if (!string.IsNullOrWhiteSpace(next) && !IsSwitchToken(next))
            {
                path = next;
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractExplorerOptionPath(string[] tokens, int index, out string? path)
    {
        path = null;
        if (index < 0 || index >= tokens.Length)
            return false;

        string token = tokens[index];
        string lower = token.ToLowerInvariant();

        if (lower.StartsWith("/root", StringComparison.Ordinal))
        {
            if (TryExtractPathFromSwitchTail(token, ',', tokens, index, out path))
                return true;
            if (TryExtractPathFromSwitchTail(token, ':', tokens, index, out path))
                return true;
            if (TryExtractPathFromSwitchTail(token, '=', tokens, index, out path))
                return true;

            if (index + 1 < tokens.Length)
            {
                var next = NormalizePotentialPath(tokens[index + 1]);
                if (!string.IsNullOrWhiteSpace(next) && !IsSwitchToken(next))
                {
                    path = next;
                    return true;
                }
            }
            return false;
        }

        if (lower.StartsWith("/e,", StringComparison.Ordinal))
        {
            int comma = token.IndexOf(',');
            if (comma >= 0 && comma + 1 < token.Length)
            {
                var after = NormalizePotentialPath(token[(comma + 1)..]);
                if (!string.IsNullOrWhiteSpace(after))
                {
                    path = after;
                    return true;
                }
            }
            return false;
        }

        if (lower.Equals("/e", StringComparison.Ordinal) && index + 1 < tokens.Length)
        {
            var next = NormalizePotentialPath(tokens[index + 1]);
            if (!string.IsNullOrWhiteSpace(next) && !IsSwitchToken(next))
            {
                path = next;
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractPathFromSwitchTail(string tail, char delimiter, string[] tokens, int index, out string? path)
    {
        path = null;
        int sep = tail.IndexOf(delimiter);
        if (sep < 0)
            return false;

        string raw = sep + 1 < tail.Length ? tail[(sep + 1)..] : string.Empty;
        var candidate = NormalizePotentialPath(raw);
        if (!string.IsNullOrWhiteSpace(candidate) && !IsSwitchToken(candidate))
        {
            path = candidate;
            return true;
        }

        if (index + 1 < tokens.Length)
        {
            var next = NormalizePotentialPath(tokens[index + 1]);
            if (!string.IsNullOrWhiteSpace(next) && !IsSwitchToken(next))
            {
                path = next;
                return true;
            }
        }

        return false;
    }

    private static string? NormalizePotentialPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string candidate = raw.Trim();
        candidate = candidate.Trim().Trim('"');
        candidate = candidate.Trim().Trim(',').Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        if (candidate.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(candidate, UriKind.Absolute);
                if (uri.IsFile)
                    candidate = uri.LocalPath;
            }
            catch { }
        }

        int quotedArgSplit = candidate.IndexOf("\" ", StringComparison.Ordinal);
        if (quotedArgSplit > 0)
            candidate = candidate[..quotedArgSplit].Trim().Trim('"');

        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            int switchSplit = candidate.IndexOf(" /", StringComparison.Ordinal);
            if (switchSplit < 0)
                switchSplit = candidate.IndexOf(" -", StringComparison.Ordinal);
            if (switchSplit > 0)
            {
                var shortened = candidate[..switchSplit].Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(shortened))
                    candidate = shortened;
            }
        }

        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static bool IsSwitchToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return true;
        string t = token.Trim();
        if (t.StartsWith("--", StringComparison.Ordinal))
            return true;
        if (t.StartsWith("/", StringComparison.Ordinal))
            return true;
        if (t.StartsWith("-", StringComparison.Ordinal))
            return true;
        return false;
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
