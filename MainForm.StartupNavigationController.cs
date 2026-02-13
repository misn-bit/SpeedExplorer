using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class StartupNavigationController
    {
        private readonly MainForm _owner;
        private static readonly string FolderSettingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "folder_settings.json");

        public StartupNavigationController(MainForm owner)
        {
            _owner = owner;
        }

        public void LoadDrives()
        {
            _owner._items.Clear();
            _owner._allItems.Clear();

            _owner._listView.BeginUpdate();
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                int driveNumber = 1;
                foreach (var d in drives)
                {
                    var isUsb = d.DriveType == DriveType.Removable;
                    var defaultLabel = isUsb ? Localization.T("usb_drive") : Localization.T("local_disk");
                    var label = string.IsNullOrEmpty(d.VolumeLabel) ? defaultLabel : d.VolumeLabel;

                    var item = new FileItem
                    {
                        DriveNumber = driveNumber++,
                        Name = $"{label} ({d.Name.TrimEnd('\\')})",
                        FullPath = d.Name,
                        IsDirectory = true,
                        Size = d.TotalSize,
                        FreeSpace = d.TotalFreeSpace,
                        DriveFormat = d.DriveFormat,
                        DriveType = d.DriveType.ToString(),
                        DateModified = DateTime.MinValue,
                        Extension = isUsb ? ".usb" : ".drive"
                    };
                    _owner._items.Add(item);
                }
                _owner._allItems = new List<FileItem>(_owner._items);
                if (_owner.IsTileView)
                {
                    _owner.PopulateTileItems();
                }
                else
                {
                    _owner._listView.VirtualListSize = _owner._items.Count;
                    _owner._listView.TileSize = new System.Drawing.Size(_owner.Scale(260), _owner.Scale(60));
                }

                _owner._statusLabel.Text = string.Format(Localization.T("status_drives_count"), _owner._items.Count);
                _owner._pathLabel.Text = Localization.T("this_pc");
                _owner.Text = Localization.T("this_pc");
                _owner._currentPath = ThisPcPath;
                _owner._addressBar.Text = Localization.T("this_pc");

                _owner._sortColumn = SortColumn.DriveNumber;
                _owner._sortDirection = SortDirection.Ascending;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading drives: {ex.Message}");
            }
            finally
            {
                _owner._listView.EndUpdate();
                _owner._listView.Invalidate();
            }
        }

        public bool IsDriveItemsOnly()
        {
            if (_owner._items.Count == 0)
                return false;
            foreach (var item in _owner._items)
            {
                if (item.Extension != ".drive" && item.Extension != ".usb")
                    return false;
            }
            return true;
        }

        public void LoadFolderSettings()
        {
            try
            {
                if (File.Exists(FolderSettingsPath))
                {
                    var json = File.ReadAllText(FolderSettingsPath);
                    var folderSettings = System.Text.Json.JsonSerializer.Deserialize<FolderSettings>(json);
                    if (folderSettings?.Settings != null)
                    {
                        _owner._nav.FolderSortSettings.Clear();
                        foreach (var kvp in folderSettings.Settings)
                        {
                            _owner._nav.FolderSortSettings[kvp.Key] = (kvp.Value.Column, kvp.Value.Direction);
                        }
                    }
                }
            }
            catch
            {
                // Keep defaults on malformed/missing settings.
            }
        }

        public void SaveFolderSettings()
        {
            try
            {
                var settings = new FolderSettings();
                foreach (var kvp in _owner._nav.FolderSortSettings)
                {
                    settings.Settings[kvp.Key] = new FolderSortState
                    {
                        Column = kvp.Value.Column,
                        Direction = kvp.Value.Direction
                    };
                }

                var json = System.Text.Json.JsonSerializer.Serialize(settings);
                File.WriteAllText(FolderSettingsPath, json);
            }
            catch
            {
                // Ignore persistence errors.
            }
        }
    }
}
