using System;
using System.IO;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class WatcherController : IDisposable
    {
        private readonly MainForm _owner;
        private readonly System.Windows.Forms.Timer _watcherTimer;
        private FileSystemWatcher? _watcher;

        public WatcherController(MainForm owner)
        {
            _owner = owner;

            // Debounce FS events (avoid refresh storms).
            _watcherTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _watcherTimer.Tick += (s, e) =>
            {
                _watcherTimer.Stop();
                _owner.ObserveTask(_owner.RefreshCurrentAsync(), "WatcherController.TickRefresh");
            };
        }

        public void UpdateWatcher(string path)
        {
            try
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                    _watcher = null;
                }

                if (!Directory.Exists(path)) return;

                _watcher = new FileSystemWatcher(path)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };

                _watcher.Created += (s, e) => RequestWatcherRefresh();
                _watcher.Deleted += (s, e) => RequestWatcherRefresh();
                _watcher.Changed += (s, e) => RequestWatcherRefresh();
                _watcher.Renamed += (s, e) =>
                {
                    TagManager.Instance.HandleRename(e.OldFullPath, e.FullPath);
                    _watcherTimer.Stop();
                    _watcherTimer.Start();
                };
            }
            catch { }
        }

        public void RequestWatcherRefresh()
        {
            try
            {
                _watcherTimer.Stop();
                _watcherTimer.Start();
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                _watcherTimer.Stop();
                _watcherTimer.Dispose();
            }
            catch { }

            try
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                    _watcher = null;
                }
            }
            catch { }
        }
    }
}
