using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private void ToggleTileView()
    {
        _tileViewController.Toggle();
    }

    private void ApplyTileView()
    {
        _tileViewController.Apply();
    }

    private void UpdateTileViewMetrics()
    {
        _tileViewController.UpdateMetrics();
    }

    private void UpdateViewToggleLabel()
    {
        _tileViewController.UpdateViewToggleLabel();
    }

    private sealed class TileViewController
    {
        private readonly MainForm _owner;
        private bool _isTileView;
        private System.Windows.Forms.Timer? _populateTimer;
        private System.Windows.Forms.Timer? _deferredUniqueLoadTimer;
        private readonly Dictionary<string, List<ListViewItem>> _iconBindings = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _scheduledUniqueLoads = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _deferredUniqueSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _deferredUniqueQueue = new();
        private bool _useDeferredUniqueLoading;
        private int _nextPopulateIndex;
        private int _populateTotal;
        private bool _isPopulating;
        private long _lastStatusTick;

        private const int AsyncPopulateThreshold = 1200;
        private const int InitialBatchSize = 120;
        private const int PopulateTimerIntervalMs = 16;
        private const int PopulateBatchMaxPerTick = 64;
        private const int PopulateChunkSize = 16;
        private const int PopulateTimeBudgetMs = 6;
        private const int LargeFolderUniqueDeferThreshold = 5000;
        private const int ImmediateUniqueBudget = 320;
        private const int DeferredUniqueTimerMs = 100;
        private const int DeferredUniqueLoadsPerTick = 24;
        private const int DeferredLoadPendingCap = 320;

        public bool IsTileView => _isTileView;

        public TileViewController(MainForm owner)
        {
            _owner = owner;
        }

        private bool IsThisPcContext()
            => _owner._currentPath == ThisPcPathConst && !_owner.IsSearchMode;

        private bool GetContextTilePreference()
            => IsThisPcContext() ? AppSettings.Current.TileViewThisPc : AppSettings.Current.TileViewFolders;

        private void SetContextTilePreference(bool tile)
        {
            var s = AppSettings.Current;
            if (IsThisPcContext())
            {
                if (s.TileViewThisPc == tile) return;
                s.TileViewThisPc = tile;
            }
            else
            {
                if (s.TileViewFolders == tile) return;
                s.TileViewFolders = tile;
            }
            s.Save();
        }

        private void SyncModeFromContext()
        {
            _isTileView = GetContextTilePreference();
        }

        public void Toggle()
        {
            SyncModeFromContext();
            _isTileView = !_isTileView;
            SetContextTilePreference(_isTileView);
            Apply();
        }

        public void Apply()
        {
            if (_owner._listView == null) return;
            SyncModeFromContext();

            _owner._listView.BeginUpdate();
            try
            {
                try { _owner._listView.SelectedIndices.Clear(); } catch { }
                try { _owner._listView.FocusedItem = null; } catch { }

                if (_isTileView)
                {
                    CancelPopulation();
                    _owner._listView.VirtualMode = false;
                    _owner._listView.View = View.Tile;
                    _owner._listView.OwnerDraw = false;
                    UpdateMetrics();
                    PopulateTileItems();
                }
                else
                {
                    CancelPopulation();
                    _owner._listView.View = View.Details;
                    _owner._listView.OwnerDraw = true;
                    _owner._listView.Items.Clear();
                    _owner._listView.VirtualMode = true;
                    // Restore the appropriate columns (This PC vs folder view) when switching back.
                    if (_owner._currentPath == ThisPcPathConst && !_owner.IsSearchMode)
                        _owner.SetupDriveColumns(_owner._listView);
                    else
                        _owner.SetupFileColumns(_owner._listView);
                    _owner._listView.VirtualListSize = _owner._items.Count;
                    _owner.EnsureHeaderTail();
                }
            }
            finally
            {
                _owner._listView.EndUpdate();
            }

            UpdateViewToggleLabel();
        }

        public void ApplyViewModeForNavigation()
        {
            if (_owner._listView == null) return;
            SyncModeFromContext();
            CancelPopulation();

            if (_isTileView)
            {
                bool alreadyTileMode =
                    _owner._listView.View == View.Tile &&
                    !_owner._listView.VirtualMode &&
                    !_owner._listView.OwnerDraw;

                if (alreadyTileMode)
                {
                    UpdateMetrics();
                    UpdateViewToggleLabel();
                    return;
                }

                _owner._listView.BeginUpdate();
                try
                {
                    // Reset virtual state before switching into concrete tile items.
                    if (_owner._listView.VirtualMode)
                    {
                        try { _owner._listView.VirtualListSize = 0; } catch { }
                    }
                    _owner._listView.VirtualMode = false;
                    _owner._listView.View = View.Tile;
                    _owner._listView.OwnerDraw = false;
                    UpdateMetrics();
                }
                finally
                {
                    _owner._listView.EndUpdate();
                }
            }
            else
            {
                bool alreadyDetailsVirtual =
                    _owner._listView.View == View.Details &&
                    _owner._listView.VirtualMode &&
                    _owner._listView.OwnerDraw;

                if (alreadyDetailsVirtual)
                {
                    UpdateViewToggleLabel();
                    return;
                }

                _owner._listView.BeginUpdate();
                try
                {
                    // IMPORTANT: VirtualMode cannot be enabled while concrete items exist.
                    try { _owner._listView.SelectedIndices.Clear(); } catch { }
                    try { _owner._listView.Items.Clear(); } catch { }
                    try { _owner._listView.VirtualListSize = 0; } catch { }
                    _owner._listView.View = View.Details;
                    _owner._listView.OwnerDraw = true;
                    _owner._listView.VirtualMode = true;
                }
                finally
                {
                    _owner._listView.EndUpdate();
                }
            }

            UpdateViewToggleLabel();
        }

        public void UpdateMetrics()
        {
            if (_owner._listView == null || !_isTileView) return;

            int icon = AppSettings.Current.IconSize;
            _owner._listView.TileSize = new Size(
                Math.Max(_owner.Scale(200), icon * 4),
                Math.Max(_owner.Scale(60), icon + _owner.Scale(24)));
        }

        public void PopulateTileItems()
        {
            if (_owner._listView == null) return;

            CancelPopulation();
            _owner._listView.VirtualMode = false;
            _owner._listView.VirtualListSize = 0;
            _owner._listView.Items.Clear();

            int total = _owner._items.Count;
            if (total == 0) return;
            _populateTotal = total;
            _useDeferredUniqueLoading = total >= LargeFolderUniqueDeferThreshold;

            if (total <= AsyncPopulateThreshold)
            {
                AddTileBatch(0, total);
                return;
            }

            int firstBatch = Math.Min(InitialBatchSize, total);
            AddTileBatch(0, firstBatch);
            _nextPopulateIndex = firstBatch;
            _isPopulating = true;
            _lastStatusTick = Environment.TickCount64;
            _owner._statusLabel.Text = string.Format(Localization.T("status_loading_tiles"), _nextPopulateIndex, _populateTotal);

            EnsurePopulateTimer();
            _populateTimer!.Start();
        }

        public void UpdateViewToggleLabel()
        {
            if (_owner._viewToggleLabel == null) return;
            _owner._viewToggleLabel.Text = _isTileView ? Localization.T("view_details") : Localization.T("view_tiles");
        }

        public void CancelPopulation()
        {
            _isPopulating = false;
            _nextPopulateIndex = 0;
            _populateTotal = 0;
            _populateTimer?.Stop();
            _deferredUniqueLoadTimer?.Stop();
            _iconBindings.Clear();
            _scheduledUniqueLoads.Clear();
            _deferredUniqueSet.Clear();
            _deferredUniqueQueue.Clear();
            _useDeferredUniqueLoading = false;
        }

        public void RegisterIconBinding(string key, ListViewItem listItem)
        {
            if (!_isTileView || _owner._listView == null || listItem == null || string.IsNullOrWhiteSpace(key))
                return;

            if (!_iconBindings.TryGetValue(key, out var items))
            {
                items = new List<ListViewItem>(1);
                _iconBindings[key] = items;
            }
            items.Add(listItem);
        }

        public void HandleIconReady(string key)
        {
            if (!_isTileView || _owner._listView == null || _owner._listView.IsDisposed || string.IsNullOrWhiteSpace(key))
                return;

            if (!_iconBindings.TryGetValue(key, out var items) || items.Count == 0)
                return;

            bool changed = false;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null) continue;
                try
                {
                    if (!ReferenceEquals(item.ListView, _owner._listView))
                        continue;
                    if (!string.Equals(item.ImageKey, key, StringComparison.OrdinalIgnoreCase))
                    {
                        item.ImageKey = key;
                        changed = true;
                    }
                }
                catch { }
            }

            _iconBindings.Remove(key);
            if (changed)
                _owner._listView.Invalidate();

            _deferredUniqueSet.Remove(key);
        }

        public bool ShouldQueueUniqueIconNow(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;
            if (_scheduledUniqueLoads.Contains(key))
                return false;

            _scheduledUniqueLoads.Add(key);

            if (!_isTileView || !_useDeferredUniqueLoading || _scheduledUniqueLoads.Count <= ImmediateUniqueBudget)
                return true;

            _deferredUniqueSet.Add(key);
            _deferredUniqueQueue.Enqueue(key);
            EnsureDeferredUniqueLoadTimer();
            return false;
        }

        private void EnsurePopulateTimer()
        {
            if (_populateTimer != null)
                return;

            _populateTimer = new System.Windows.Forms.Timer { Interval = PopulateTimerIntervalMs };
            _populateTimer.Tick += (s, e) => ContinuePopulate();
        }

        private void EnsureDeferredUniqueLoadTimer()
        {
            if (_deferredUniqueLoadTimer == null)
            {
                _deferredUniqueLoadTimer = new System.Windows.Forms.Timer { Interval = DeferredUniqueTimerMs };
                _deferredUniqueLoadTimer.Tick += (s, e) => PumpDeferredUniqueLoads();
            }

            if (!_deferredUniqueLoadTimer.Enabled)
                _deferredUniqueLoadTimer.Start();
        }

        private void PumpDeferredUniqueLoads()
        {
            if (!_isTileView || _owner._listView == null || _owner._listView.IsDisposed)
            {
                _deferredUniqueLoadTimer?.Stop();
                return;
            }

            if (_deferredUniqueQueue.Count == 0 || _owner._iconLoadService == null)
            {
                _deferredUniqueLoadTimer?.Stop();
                return;
            }

            int queuedNow = 0;
            bool colored = AppSettings.Current.UseSystemIcons;
            while (queuedNow < DeferredUniqueLoadsPerTick && _deferredUniqueQueue.Count > 0)
            {
                if (_owner._iconLoadService.PendingCount >= DeferredLoadPendingCap)
                    break;

                string key = _deferredUniqueQueue.Dequeue();
                if (!_deferredUniqueSet.Remove(key))
                    continue;
                if (_owner._smallIcons.Images.ContainsKey(key))
                    continue;

                _owner._iconLoadService.QueueIconLoad(key, false, colored);
                queuedNow++;
            }

            if (_deferredUniqueQueue.Count == 0)
                _deferredUniqueLoadTimer?.Stop();
        }

        private void ContinuePopulate()
        {
            if (!_isPopulating || !_isTileView || _owner._listView == null || _owner._listView.IsDisposed)
            {
                _populateTimer?.Stop();
                return;
            }

            if (_nextPopulateIndex >= _populateTotal)
            {
                _isPopulating = false;
                _populateTimer?.Stop();
                _owner._statusLabel.Text = string.Format(Localization.T("status_ready_items"), _owner._items.Count);
                return;
            }
            
            int added = 0;
            var sw = Stopwatch.StartNew();

            _owner._listView.BeginUpdate();
            try
            {
                while (_nextPopulateIndex < _populateTotal && added < PopulateBatchMaxPerTick)
                {
                    if (added > 0 && sw.ElapsedMilliseconds >= PopulateTimeBudgetMs)
                        break;

                    int remaining = _populateTotal - _nextPopulateIndex;
                    int chunk = Math.Min(PopulateChunkSize, Math.Min(PopulateBatchMaxPerTick - added, remaining));
                    if (chunk <= 0) break;

                    AddTileBatch(_nextPopulateIndex, chunk);
                    _nextPopulateIndex += chunk;
                    added += chunk;
                }
            }
            finally
            {
                _owner._listView.EndUpdate();
            }

            long now = Environment.TickCount64;
            if (now - _lastStatusTick >= 120 || _nextPopulateIndex >= _populateTotal)
            {
                _owner._statusLabel.Text = string.Format(Localization.T("status_loading_tiles"), _nextPopulateIndex, _populateTotal);
                _lastStatusTick = now;
            }
        }

        private void AddTileBatch(int startIndex, int count)
        {
            if (count <= 0) return;

            var batch = new ListViewItem[count];
            for (int i = 0; i < count; i++)
            {
                batch[i] = _owner.BuildListViewItem(_owner._items[startIndex + i], includeSubItems: false);
            }
            _owner._listView.Items.AddRange(batch);
        }
    }
}
