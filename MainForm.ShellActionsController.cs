using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class ShellActionsController
    {
        private readonly MainForm _owner;

        public ShellActionsController(MainForm owner)
        {
            _owner = owner;
        }

        public void OpenWithDialog()
        {
            string path = _owner.GetSelectedPath();
            if (string.IsNullOrEmpty(path))
                return;
            if (IsShellPath(path))
            {
                _owner.OpenShellPath(path);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}") { UseShellExecute = true });
            }
            catch { }
        }

        public void ShowInExplorer()
        {
            string path = _owner.GetSelectedPath();
            if (string.IsNullOrEmpty(path))
            {
                if (!string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath != ThisPcPath)
                    path = _owner._currentPath;
                else
                    return;
            }

            try
            {
                if (IsShellPath(path))
                {
                    _owner.OpenShellPath(path);
                    return;
                }

                if (Directory.Exists(path))
                    Process.Start("explorer.exe", $"\"{path}\"");
                else
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch { }
        }

        public void CopyPathToClipboard()
        {
            string path = _owner.GetSelectedPath();
            if (string.IsNullOrEmpty(path))
            {
                if (!string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath != ThisPcPath)
                    path = _owner._currentPath;
                else
                    return;
            }
            Clipboard.SetText(path);
        }

        public void ShowProperties()
        {
            var paths = _owner.GetSelectedPaths();
            if (paths.Length == 0)
            {
                if (!string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath != ThisPcPath)
                    paths = new[] { _owner._currentPath };
                else
                    return;
            }

            if (paths.Any(IsShellPath))
            {
                _owner.OpenShellPath(paths.First(p => IsShellPath(p)));
                _owner._statusLabel.Text = Localization.T("status_properties_unavailable");
                return;
            }

            if (paths.Length == 1)
            {
                ShowSingleFileProperties(paths[0]);
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
