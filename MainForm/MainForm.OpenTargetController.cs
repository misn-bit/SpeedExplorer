using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class OpenTargetController
    {
        public enum NewTabHistoryMode
        {
            None,
            BackButtonTarget,
            ForwardButtonTarget
        }

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

        public void OpenPathInNewTab(
            string path,
            bool activate = true,
            Stack<string>? inheritedBackHistory = null,
            Stack<string>? inheritedForwardHistory = null)
        {
            _owner._tabsController.OpenPathInNewTab(path, activate, inheritedBackHistory, inheritedForwardHistory);
        }

        public void OpenPathByMiddleClickPreference(
            string path,
            bool activateTab = false,
            NewTabHistoryMode historyMode = NewTabHistoryMode.None)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (AppSettings.Current.MiddleClickOpensNewTab)
            {
                if (historyMode == NewTabHistoryMode.None)
                {
                    OpenPathInNewTab(path, activateTab);
                    return;
                }

                var back = CloneHistory(_owner._nav.BackHistory);
                var forward = CloneHistory(_owner._nav.ForwardHistory);

                if (historyMode == NewTabHistoryMode.BackButtonTarget)
                {
                    if (back.Count > 0 && string.Equals(back.Peek(), path, StringComparison.OrdinalIgnoreCase))
                        back.Pop();
                    if (!string.IsNullOrWhiteSpace(_owner._currentPath))
                        forward.Push(_owner._currentPath);
                }
                else if (historyMode == NewTabHistoryMode.ForwardButtonTarget)
                {
                    if (!string.IsNullOrWhiteSpace(_owner._currentPath))
                        back.Push(_owner._currentPath);
                    if (forward.Count > 0 && string.Equals(forward.Peek(), path, StringComparison.OrdinalIgnoreCase))
                        forward.Pop();
                }

                OpenPathInNewTab(path, activateTab, back, forward);
            }
            else
            {
                Program.MultiWindowContext.Instance.ShowNext(new MainForm(path));
            }
        }

        private static Stack<string> CloneHistory(Stack<string> source)
            => new(source.Reverse());
    }
}
