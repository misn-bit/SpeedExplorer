using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpeedExplorer;

public partial class MainForm
{
    BrowserState IOpenTargetHost.BrowserState => State;
    bool IOpenTargetHost.IsSearchMode => IsSearchMode;
    Stack<string> IOpenTargetHost.BackHistory => _nav.BackHistory;
    Stack<string> IOpenTargetHost.ForwardHistory => _nav.ForwardHistory;
    string IOpenTargetHost.GetSelectedPath() => GetSelectedPath();
    string? IOpenTargetHost.GetShellParentPath(string shellPath) => GetShellParentPath(shellPath);
    void IOpenTargetHost.OpenPathInNewTab(
        string path,
        bool activate,
        Stack<string>? inheritedBackHistory,
        Stack<string>? inheritedForwardHistory)
        => _tabsController.OpenPathInNewTab(path, activate, inheritedBackHistory, inheritedForwardHistory);

    private sealed class OpenTargetController
    {
        public enum NewTabHistoryMode
        {
            None,
            BackButtonTarget,
            ForwardButtonTarget
        }

        private readonly IOpenTargetHost _host;
        private BrowserState State => _host.BrowserState;

        public OpenTargetController(IOpenTargetHost host)
        {
            _host = host;
        }

        public string? GetOpenInOtherTargetPath()
        {
            string path = _host.GetSelectedPath();
            if (string.IsNullOrEmpty(path))
            {
            if (!string.IsNullOrEmpty(State.CurrentPath) && State.CurrentPath != ThisPcPath && !_host.IsSearchMode)
                path = State.CurrentPath;
            else if (!string.IsNullOrEmpty(State.CurrentPath) && State.CurrentPath == ThisPcPath)
                path = State.CurrentPath;
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
            var item = State.Items.FirstOrDefault(i => i.FullPath == path);
                if (item != null)
                {
                    if (item.IsDirectory)
                        return path;
                    var parentShell = _host.GetShellParentPath(path);
                    return string.IsNullOrEmpty(parentShell) ? null : parentShell;
                }

                var shellParent = _host.GetShellParentPath(path);
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
            _host.OpenPathInNewTab(path, activate, inheritedBackHistory, inheritedForwardHistory);
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

                var back = CloneHistory(_host.BackHistory);
                var forward = CloneHistory(_host.ForwardHistory);

                if (historyMode == NewTabHistoryMode.BackButtonTarget)
                {
                    if (back.Count > 0 && string.Equals(back.Peek(), path, StringComparison.OrdinalIgnoreCase))
                        back.Pop();
            if (!string.IsNullOrWhiteSpace(State.CurrentPath))
                forward.Push(State.CurrentPath);
                }
                else if (historyMode == NewTabHistoryMode.ForwardButtonTarget)
                {
            if (!string.IsNullOrWhiteSpace(State.CurrentPath))
                back.Push(State.CurrentPath);
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
