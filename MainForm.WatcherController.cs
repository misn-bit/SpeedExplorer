using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class WatcherController : IDisposable
    {
        private enum ChangeKind
        {
            Created,
            Deleted,
            Changed,
            Renamed
        }

        private readonly struct PendingChange
        {
            public PendingChange(ChangeKind kind, string path, string? oldPath = null)
            {
                Kind = kind;
                Path = path;
                OldPath = oldPath;
            }

            public ChangeKind Kind { get; }
            public string Path { get; }
            public string? OldPath { get; }
        }

        private const double PatchRatio = 0.03; // 3% of current folder size.
        private const int PatchMinDistinctPaths = 25;
        private const int PatchMaxDistinctPaths = 300;
        private const int PatchEventFactor = 2; // Events can be noisier than distinct paths.

        private readonly MainForm _owner;
        private readonly System.Windows.Forms.Timer _watcherTimer;
        private FileSystemWatcher? _watcher;
        private readonly object _pendingLock = new();
        private readonly List<PendingChange> _pendingChanges = new();

        public WatcherController(MainForm owner)
        {
            _owner = owner;

            // Debounce FS events (avoid refresh storms).
            _watcherTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _watcherTimer.Tick += (s, e) =>
            {
                _watcherTimer.Stop();
                var batch = DrainPendingChanges();
                if (!TryApplyLivePatch(batch))
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
                _watcher.InternalBufferSize = 64 * 1024;

                _watcher.Created += (s, e) => EnqueueChange(new PendingChange(ChangeKind.Created, e.FullPath));
                _watcher.Deleted += (s, e) => EnqueueChange(new PendingChange(ChangeKind.Deleted, e.FullPath));
                _watcher.Changed += (s, e) => EnqueueChange(new PendingChange(ChangeKind.Changed, e.FullPath));
                _watcher.Renamed += (s, e) =>
                {
                    TagManager.Instance.HandleRename(e.OldFullPath, e.FullPath);
                    EnqueueChange(new PendingChange(ChangeKind.Renamed, e.FullPath, e.OldFullPath));
                };
                _watcher.Error += (s, e) => RequestWatcherRefresh();
            }
            catch { }
        }

        public void RequestWatcherRefresh()
        {
            RestartTimer();
        }

        private void EnqueueChange(PendingChange change)
        {
            try
            {
                lock (_pendingLock)
                {
                    _pendingChanges.Add(change);
                }
            }
            catch { }
            RestartTimer();
        }

        private void RestartTimer()
        {
            try
            {
                if (_owner.IsDisposed || _owner.Disposing)
                    return;

                if (_owner.InvokeRequired)
                {
                    _owner.BeginInvoke((Action)RestartTimer);
                    return;
                }

                _watcherTimer.Stop();
                _watcherTimer.Start();
            }
            catch { }
        }

        private List<PendingChange> DrainPendingChanges()
        {
            lock (_pendingLock)
            {
                if (_pendingChanges.Count == 0)
                    return new List<PendingChange>();
                var copy = new List<PendingChange>(_pendingChanges);
                _pendingChanges.Clear();
                return copy;
            }
        }

        private bool TryApplyLivePatch(List<PendingChange> changes)
        {
            if (changes == null || changes.Count == 0)
                return false;

            if (_owner._nav.IsNavigating ||
                _owner.IsSearchMode ||
                MainForm.IsShellPath(_owner._currentPath) ||
                string.IsNullOrWhiteSpace(_owner._currentPath) ||
                _owner._currentPath == ThisPcPath ||
                _owner._listView == null ||
                _owner._listView.IsDisposed)
            {
                return false;
            }

            int distinctPaths = changes
                .SelectMany(c => c.Kind == ChangeKind.Renamed && !string.IsNullOrWhiteSpace(c.OldPath)
                    ? new[] { c.Path, c.OldPath! }
                    : new[] { c.Path })
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            int totalItems = Math.Max(_owner._allItems.Count, _owner._items.Count);
            int dynamicDistinctLimit = (int)Math.Round(totalItems * PatchRatio);
            dynamicDistinctLimit = Math.Max(PatchMinDistinctPaths, Math.Min(PatchMaxDistinctPaths, dynamicDistinctLimit));
            int dynamicEventLimit = dynamicDistinctLimit * PatchEventFactor;

            if (changes.Count > dynamicEventLimit)
                return false;

            if (distinctPaths > dynamicDistinctLimit)
                return false;

            var selectedPaths = _owner.GetSelectedPaths().ToList();
            string? topPath = null;
            try
            {
                if (_owner._listView.TopItem?.Tag is FileItem topItem && !string.IsNullOrWhiteSpace(topItem.FullPath))
                    topPath = topItem.FullPath;
            }
            catch { }

            var map = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in _owner._allItems)
            {
                if (!string.IsNullOrWhiteSpace(item.FullPath))
                    map[item.FullPath] = item;
            }

            int changedCount = 0;

            foreach (var change in changes)
            {
                if (string.IsNullOrWhiteSpace(change.Path))
                    continue;

                switch (change.Kind)
                {
                    case ChangeKind.Deleted:
                        if (map.Remove(change.Path))
                            changedCount++;
                        break;

                    case ChangeKind.Renamed:
                        if (!string.IsNullOrWhiteSpace(change.OldPath) && map.Remove(change.OldPath!))
                            changedCount++;
                        if (TryCreateFileItem(change.Path, out var renamedItem))
                        {
                            map[change.Path] = renamedItem;
                            changedCount++;
                        }
                        else if (map.Remove(change.Path))
                        {
                            changedCount++;
                        }
                        break;

                    case ChangeKind.Created:
                    case ChangeKind.Changed:
                        if (TryCreateFileItem(change.Path, out var updatedItem))
                        {
                            map[change.Path] = updatedItem;
                            changedCount++;
                        }
                        else if (change.Kind == ChangeKind.Changed && map.Remove(change.Path))
                        {
                            changedCount++;
                        }
                        break;
                }
            }

            if (changedCount == 0)
                return true;

            _owner._allItems = map.Values.ToList();
            FileSystemService.SortItems(_owner._allItems, _owner._sortColumn, _owner._sortDirection, _owner._taggedFilesOnTop);
            _owner._items = new List<FileItem>(_owner._allItems);

            _owner._listView.BeginUpdate();
            try
            {
                _owner._listView.SelectedIndices.Clear();
                _owner.SetListSelectionAnchor(-1);

                if (_owner.IsTileView)
                {
                    _owner.PopulateTileItems();
                }
                else
                {
                    _owner._listView.VirtualListSize = 0;
                    _owner._listView.VirtualListSize = _owner._items.Count;
                }

                if (selectedPaths.Count > 0)
                    _owner.SelectItems(selectedPaths);
            }
            finally
            {
                _owner._listView.EndUpdate();
            }

            if (!string.IsNullOrWhiteSpace(topPath))
            {
                int topIndex = _owner._items.FindIndex(x => x.FullPath.Equals(topPath, StringComparison.OrdinalIgnoreCase));
                if (topIndex >= 0)
                {
                    try
                    {
                        if (!_owner.IsTileView && topIndex < _owner._listView.Items.Count)
                            _owner._listView.TopItem = _owner._listView.Items[topIndex];
                        else
                            _owner._listView.EnsureVisible(topIndex);
                    }
                    catch { }
                }
            }

            _owner._listView.Invalidate();
            _owner._tabsController.SyncPathSnapshot(_owner._currentPath, _owner._items, _owner._allItems);
            _owner._statusLabel.Text = string.Format(Localization.T("status_ready_items"), _owner._items.Count);
            return true;
        }

        private static bool TryCreateFileItem(string fullPath, out FileItem item)
        {
            item = new FileItem();
            try
            {
                if (Directory.Exists(fullPath))
                {
                    var di = new DirectoryInfo(fullPath);
                    item = new FileItem
                    {
                        FullPath = di.FullName,
                        Name = di.Name,
                        IsDirectory = true,
                        Size = 0,
                        DateModified = di.LastWriteTime,
                        DateCreated = di.CreationTime,
                        Extension = ""
                    };
                    return true;
                }

                if (File.Exists(fullPath))
                {
                    var fi = new FileInfo(fullPath);
                    item = new FileItem
                    {
                        FullPath = fi.FullName,
                        Name = fi.Name,
                        IsDirectory = false,
                        Size = fi.Length,
                        DateModified = fi.LastWriteTime,
                        DateCreated = fi.CreationTime,
                        Extension = fi.Extension
                    };
                    return true;
                }
            }
            catch { }

            return false;
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
