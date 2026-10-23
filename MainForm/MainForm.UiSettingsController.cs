using System;
using System.Drawing;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class UiSettingsController
    {
        private readonly MainForm _owner;
        private int _appliedListViewFontSize;
        private string _appliedListViewFontFamily;
        private BrowserState State => _owner.State;

        public UiSettingsController(MainForm owner)
        {
            _owner = owner;
            _appliedListViewFontSize = AppSettings.Current.FontSize;
            _appliedListViewFontFamily = FontFamilyCatalog.ResolveTextFamily(AppSettings.Current.TextFontFamily);
        }

        public void UpdateScale()
        {
            // 1. Scale ImageLists
            int iconSize = _owner.GetEffectiveIconSize();
            int largeSize = Math.Min(iconSize * 2, MaxImageListIconSize);
            bool iconSizeChanged = _owner._smallIcons.ImageSize.Width != iconSize ||
                                   _owner._largeIcons.ImageSize.Width != largeSize;

            if (iconSizeChanged)
            {
                // Cached icon bitmaps were fetched at the old pixel size; the
                // ImageList would just stretch them (blurry). Drop them and
                // re-fetch visible icons/thumbnails at the new physical size.
                _owner._iconLoadService?.CancelPending();
                _owner.RebuildDefaultIconSets();
                if (_owner.IsTileView)
                    _owner._tileViewController.PopulateTileItems();
                else
                {
                    // Cached virtual rows may still point at icons removed
                    // from the old-size ImageList; rebuild them against the
                    // new generic placeholders before queuing thumbnails.
                    _owner._listViewInteractionController.InvalidateRowCache();
                    _owner._listViewInteractionController.RequeueVisibleIcons();
                }
                if (_owner._listView != null && !_owner._listView.IsDisposed)
                    _owner._listView.Invalidate();
            }

            // 2. Scale Layout Panels
            _owner._titleBar.Height = _owner.Scale(LogicalTitleBarHeight);
            _owner._layoutController.UpdateNavLayoutForDpi();

            // 3. Scale Sidebar
            _owner._sidebar.ItemHeight = _owner.Scale(24);

            // 4. Scale ListView Columns & Tiles
            if (_owner.IsTileView)
                _owner.UpdateTileViewMetrics();
            _owner.RescaleListViewColumns();
            if (_owner._splitContainer != null && !_owner._splitContainer.IsDisposed)
            {
                _owner._splitContainer.SplitterWidth = _owner.Scale(3);
                _owner._splitContainer.Panel1MinSize = _owner.Scale(120);
                _owner.ApplySidebarSplit();
            }

            // 5. Scale Chat Panel if exists
            if (_owner._llmChatPanel != null)
            {
                _owner._llmChatPanel.UpdateLayoutForScale();
            }

            // 6. Rescale Title Bar Buttons (Fixed positions)
            _owner._windowChromeController.UpdateLayoutForDpi();
            _owner._tabsController.UpdateLayoutForDpi();
            _owner._layoutController.UpdateStatusBarForDpi();

            _owner.RefreshFrame();
        }

        public void ApplySettings(bool refreshCurrent = true, bool applyTheme = true)
        {
            var s = AppSettings.Current;
            if (_owner._listView == null || _owner._listView.IsDisposed)
                return;

            if (applyTheme)
                _owner._themeController.ApplyMainWindowTheme();

            int selectedIndexBefore = -1;
            try
            {
                if (_owner._listView.SelectedIndices.Count > 0)
                    selectedIndexBefore = _owner._listView.SelectedIndices[0];
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

            _owner._listView.BeginUpdate();
            try
            {
                _owner._listViewInteractionController.InvalidateRowCache();

                if (_owner.IsTileView)
                {
                    _owner._tileViewController.ApplyViewModeForNavigation();
                }
                // Zoom and other settings refreshes also pass through here.
                // Reassigning the raw settings size on each refresh discards
                // WinForms' DPI-scaled font after a monitor DPI change.
                string textFontFamily = FontFamilyCatalog.ResolveTextFamily(s.TextFontFamily);
                if (_appliedListViewFontSize != s.FontSize ||
                    !string.Equals(_appliedListViewFontFamily, textFontFamily, StringComparison.OrdinalIgnoreCase))
                {
                    _owner._listView.Font = new Font(textFontFamily, s.FontSize);
                    _appliedListViewFontSize = s.FontSize;
                    _appliedListViewFontFamily = textFontFamily;
                }

                if (_owner._sidebar is { IsDisposed: false } sidebar &&
                    !string.Equals(sidebar.Font.FontFamily.Name, textFontFamily, StringComparison.OrdinalIgnoreCase))
                    sidebar.Font = new Font(textFontFamily, sidebar.Font.Size, sidebar.Font.Style, sidebar.Font.Unit);

                // Recreate ImageLists at the DPI-scaled icon size
                _owner._iconLoadService?.CancelPending(); // Clear pending async loads
                _owner.RebuildDefaultIconSets();

                _owner._listView.SmallImageList = _owner._smallIcons;
                _owner._listView.LargeImageList = _owner._largeIcons;
                if (_owner.IsTileView)
                {
                    _owner.PopulateTileItems();
                }
                else
                {
                    try
                    {
            _owner._listView.VirtualListSize = State.Items.Count;
            if (State.Items.Count > 0)
                        {
            int idx = selectedIndexBefore >= 0 && selectedIndexBefore < State.Items.Count ? selectedIndexBefore : 0;
                            _owner._listView.SelectedIndices.Clear();
                            _owner._listView.SelectedIndices.Add(idx);
                            try { _owner._listView.FocusedItem = _owner._listView.Items[idx]; } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                            try { _owner._listView.EnsureVisible(idx); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                        }
                    }
                    catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
                }
            }
            finally
            {
                _owner._listView.EndUpdate();
            }
            _owner._sidebarController.PopulateSidebar(); // Refresh sidebar to apply visibility changes
            if (_owner._splitContainer != null)
            {
                _owner._splitContainer.Panel1Collapsed = !s.ShowSidebar;
            }
            _owner._llmChatPanel?.UpdateFromSettings(); // Update LLM panel visibility and settings
            _owner._searchController.RefreshProgressRow();
            _owner.EnsureListViewportAndPaint("SETTINGS-apply");
            if (!refreshCurrent)
                return;

            var refreshTask = _owner.RefreshCurrentAsync();
            _owner.ObserveTask(refreshTask, "UiSettingsController.ApplySettings/refresh");
            refreshTask.ContinueWith(_ =>
            {
                try
                {
                    if (_owner.IsHandleCreated && !_owner.IsDisposed)
                        _owner.BeginInvoke((Action)(() => _owner.EnsureListViewportAndPaint("SETTINGS-post-refresh")));
                }
                catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
            }, System.Threading.Tasks.TaskScheduler.Default);
        }
    }
}
