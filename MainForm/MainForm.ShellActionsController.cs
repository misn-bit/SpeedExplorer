using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    BrowserState IShellActionsHost.BrowserState => State;
    string IShellActionsHost.GetSelectedPath() => GetSelectedPath();
    string[] IShellActionsHost.GetSelectedPaths() => GetSelectedPaths();
    void IShellActionsHost.OpenShellPath(string path) => OpenShellPath(path);
    void IShellActionsHost.SetStatusMessage(string message) => _statusLabel.Text = message;

    private sealed class ShellActionsController
    {
        private readonly IShellActionsHost _host;
        private BrowserState State => _host.BrowserState;

        public ShellActionsController(IShellActionsHost host)
        {
            _host = host;
        }

        public void OpenWithDialog()
        {
            string path = _host.GetSelectedPath();
            if (string.IsNullOrEmpty(path))
                return;
            if (IsShellPath(path))
            {
                _host.OpenShellPath(path);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}") { UseShellExecute = true });
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
        }

        public void ShowInExplorer()
        {
            string path = _host.GetSelectedPath();
            if (string.IsNullOrEmpty(path))
            {
            if (!string.IsNullOrEmpty(State.CurrentPath) && State.CurrentPath != ThisPcPath)
                path = State.CurrentPath;
                else
                    return;
            }

            try
            {
                if (IsShellPath(path))
                {
                    _host.OpenShellPath(path);
                    return;
                }

                if (Directory.Exists(path))
                    Process.Start("explorer.exe", $"\"{path}\"");
                else
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
        }

        public void CopyPathToClipboard()
        {
            string path = _host.GetSelectedPath();
            if (string.IsNullOrEmpty(path))
            {
            if (!string.IsNullOrEmpty(State.CurrentPath) && State.CurrentPath != ThisPcPath)
                path = State.CurrentPath;
                else
                    return;
            }
            Clipboard.SetText(path);
        }

        public void ShowProperties()
        {
            var paths = _host.GetSelectedPaths();
            if (paths.Length == 0)
            {
            if (!string.IsNullOrEmpty(State.CurrentPath) && State.CurrentPath != ThisPcPath)
                paths = new[] { State.CurrentPath };
                else
                    return;
            }

            if (paths.Any(IsShellPath))
            {
                _host.OpenShellPath(paths.First(p => IsShellPath(p)));
                _host.SetStatusMessage(Localization.T("status_properties_unavailable"));
                return;
            }

            if (paths.Length == 1)
            {
                ShowSingleFileProperties(paths[0]);
                return;
            }

            if (paths.Length > 3)
            {
                MessageBox.Show(
                    string.Format(Localization.T("properties_multi_not_supported"), 3),
                    Localization.T("properties"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                // Open properties per item to avoid Shell data-object issues.
                foreach (var p in paths)
                    ShowSingleFileProperties(p);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not show properties: {ex.Message}", "Error");
            }
        }

        public void ShowSingleFileProperties(string path)
        {
            var info = new SHELLEXECUTEINFO();
            info.cbSize = Marshal.SizeOf(info);
            info.lpVerb = "properties";
            info.lpFile = path;
            info.nShow = 5;
            info.fMask = SEE_MASK_INVOKEIDLIST;
            ShellExecuteEx(ref info);
        }
    }
}
