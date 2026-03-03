using System.IO;
using System.Linq;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class OpenTargetController
    {
        private readonly MainForm _owner;

        public OpenTargetController(MainForm owner)
        {
            _owner = owner;
        }

        public string? GetOpenInOtherTargetPath()
        {
            string path = _owner.GetSelectedPath();
            if (string.IsNullOrEmpty(path))
            {
                if (!string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath != ThisPcPath && !_owner.IsSearchMode)
                    path = _owner._currentPath;
                else if (!string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath == ThisPcPath)
                    path = _owner._currentPath;
                else
                    return null;
            }

            return NormalizeOpenDirectoryPath(path);
        }

        public string? NormalizeOpenDirectoryPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (IsShellPath(path))
            {
                var item = _owner._items.FirstOrDefault(i => i.FullPath == path);
                if (item != null)
                {
                    if (item.IsDirectory)
                        return path;
                    var parentShell = _owner.GetShellParentPath(path);
                    return string.IsNullOrEmpty(parentShell) ? null : parentShell;
                }

                var shellParent = _owner.GetShellParentPath(path);
                return string.IsNullOrEmpty(shellParent) ? null : shellParent;
            }

            if (path == ThisPcPath)
                return path;
            if (Directory.Exists(path))
                return path;
            if (File.Exists(path))
                return Path.GetDirectoryName(path);

            return null;
        }

        public void OpenInOtherTarget()
        {
            string? path = GetOpenInOtherTargetPath();
            if (string.IsNullOrEmpty(path))
                return;

            bool defaultIsTab = AppSettings.Current.MiddleClickOpensNewTab;
            if (defaultIsTab)
            {
                Program.MultiWindowContext.Instance.ShowNext(new MainForm(path));
            }
            else
            {
                OpenPathInNewTab(path, activate: true);
            }
        }

        public void OpenPathInNewTab(string path, bool activate = true)
        {
            _owner._tabsController.OpenPathInNewTab(path, activate);
        }

        public void OpenPathByMiddleClickPreference(string path, bool activateTab = false)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (AppSettings.Current.MiddleClickOpensNewTab)
            {
                OpenPathInNewTab(path, activateTab);
            }
            else
            {
                Program.MultiWindowContext.Instance.ShowNext(new MainForm(path));
            }
        }
    }
}
