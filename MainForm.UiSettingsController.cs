using System;
using System.Drawing;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class UiSettingsController
    {
        private readonly MainForm _owner;

        public UiSettingsController(MainForm owner)
        {
            _owner = owner;
        }

        public void UpdateScale()
        {
            // 1. Scale ImageLists
            int iconSize = Math.Clamp(AppSettings.Current.IconSize, 16, 192);
            int largeSize = Math.Min(iconSize * 2, 256);
            _owner._smallIcons.ImageSize = new Size(iconSize, iconSize);
            _owner._largeIcons.ImageSize = new Size(largeSize, largeSize);

            // 2. Scale Layout Panels
            _owner._titleBar.Height = _owner.Scale(34);
            _owner._navPanel.Height = _owner.Scale(37);
            _owner._navPanel.Padding = _owner.Scale(new System.Windows.Forms.Padding(10, 5, 10, 5));
            _owner._searchControl.Width = _owner.Scale(250);
            _owner.ApplySidebarSplit();

            // 3. Scale Sidebar
            _owner._sidebar.ItemHeight = _owner.Scale(24);

            // 4. Scale ListView Columns & Tiles
            if (_owner.IsTileView)
                _owner.UpdateTileViewMetrics();
            _owner.RescaleListViewColumns();
            _owner.UpdateSearchOverlayTextAndStyle();

            // 5. Scale Chat Panel if exists
            if (_owner._llmChatPanel != null)
            {
                _owner._llmChatPanel.UpdateLayoutForScale();
            }

            // 6. Rescale Title Bar Buttons (Fixed positions)
            if (_owner._navButtonsPanel != null)
            {
                _owner._navButtonsPanel.Height = _owner.Scale(37);
            }

            _owner.RefreshFrame();
        }

        public void ApplySettings()
        {
            var s = AppSettings.Current;
            if (_owner._listView == null || _owner._listView.IsDisposed)
                return;

            int selectedIndexBefore = -1;
            try
            {
                if (_owner._listView.SelectedIndices.Count > 0)
                    selectedIndexBefore = _owner._listView.SelectedIndices[0];
            }
            catch { }

            _owner._listView.BeginUpdate();
            try
            {
                if (_owner.IsTileView)
                {
                    _owner._tileViewController.ApplyViewModeForNavigation();
                }
                _owner._listView.Font = new Font("Segoe UI Emoji", s.FontSize);

                // Recreate ImageLists
                _owner._smallIcons.Images.Clear();
                _owner._largeIcons.Images.Clear();
                _owner._iconLoadService?.CancelPending(); // Clear pending async loads

                int largeSize = Math.Min(s.IconSize * 2, 256);
                _owner._smallIcons.ImageSize = new Size(s.IconSize, s.IconSize);
                _owner._largeIcons.ImageSize = new Size(largeSize, largeSize); // Cap at 256 (ImageList limit)

                // Reload defaults (Internal)
                _owner._smallIcons.Images.Add("folder", _owner.CreateFolderIcon(s.IconSize));
                _owner._smallIcons.Images.Add("file", _owner.CreateFileIcon(s.IconSize));
                _owner._smallIcons.Images.Add("image", _owner.CreateImageIcon(s.IconSize));
                _owner._smallIcons.Images.Add("drive", _owner.CreateDriveIcon(s.IconSize));
                _owner._smallIcons.Images.Add("usb", _owner.CreateUsbIcon(s.IconSize));
                _owner._smallIcons.Images.Add("computer", _owner.CreateComputerIcon(s.IconSize));

                _owner._largeIcons.Images.Add("folder", _owner.CreateFolderIcon(largeSize));
                _owner._largeIcons.Images.Add("file", _owner.CreateFileIcon(largeSize));
                _owner._largeIcons.Images.Add("image", _owner.CreateImageIcon(largeSize));
                _owner._largeIcons.Images.Add("drive", _owner.CreateDriveIcon(largeSize));
                _owner._largeIcons.Images.Add("usb", _owner.CreateUsbIcon(largeSize));
                _owner._largeIcons.Images.Add("computer", _owner.CreateComputerIcon(largeSize));

                // Pre-load System Folder icons (Colored & Gray) to avoid async delay/pop-in
                // Use dummy path for generic folder icon
                string folderPath = "C:\\DummyFolder";
                try
                {
                    // Colored "sys_folder"
                    var smallSys = IconHelper.GetIconSized(folderPath, true, _owner._smallIcons.ImageSize.Width, false, grayscale: false);
                    var largeSys = IconHelper.GetIconSized(folderPath, true, _owner._largeIcons.ImageSize.Width, false, grayscale: false);
                    if (smallSys != null) _owner._smallIcons.Images.Add("sys_folder", smallSys);
                    if (largeSys != null) _owner._largeIcons.Images.Add("sys_folder", largeSys);

                    // Grayscale "gray_folder"
                    var smallGray = IconHelper.GetIconSized(folderPath, true, _owner._smallIcons.ImageSize.Width, false, grayscale: true);
                    var largeGray = IconHelper.GetIconSized(folderPath, true, _owner._largeIcons.ImageSize.Width, false, grayscale: true);
                    if (smallGray != null) _owner._smallIcons.Images.Add("gray_folder", smallGray);
                    if (largeGray != null) _owner._largeIcons.Images.Add("gray_folder", largeGray);
                }
                catch { }

                _owner._listView.SmallImageList = _owner._smallIcons;
                _owner._listView.LargeImageList = _owner._largeIcons;
                _owner.UpdateSearchOverlayTextAndStyle();
                if (_owner.IsTileView)
                {
                    _owner.PopulateTileItems();
                }
                else
                {
                    try
                    {
                        _owner._listView.VirtualListSize = _owner._items.Count;
                        if (_owner._items.Count > 0)
                        {
                            int idx = selectedIndexBefore >= 0 && selectedIndexBefore < _owner._items.Count ? selectedIndexBefore : 0;
                            _owner._listView.SelectedIndices.Clear();
                            _owner._listView.SelectedIndices.Add(idx);
                            try { _owner._listView.FocusedItem = _owner._listView.Items[idx]; } catch { }
                            try { _owner._listView.EnsureVisible(idx); } catch { }
                        }
                    }
                    catch { }
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
            _owner.RefreshSearchOverlayVisibility();
            _owner.EnsureListViewportAndPaint("SETTINGS-apply");
            var refreshTask = _owner.RefreshCurrentAsync();
            _owner.ObserveTask(refreshTask, "UiSettingsController.ApplySettings/refresh");
            refreshTask.ContinueWith(_ =>
            {
                try
                {
                    if (_owner.IsHandleCreated && !_owner.IsDisposed)
                        _owner.BeginInvoke((Action)(() => _owner.EnsureListViewportAndPaint("SETTINGS-post-refresh")));
                }
                catch { }
            }, System.Threading.Tasks.TaskScheduler.Default);
        }
    }
}
