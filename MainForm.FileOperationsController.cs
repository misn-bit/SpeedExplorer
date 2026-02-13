using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class FileOperationsController
    {
        private readonly MainForm _owner;

        public FileOperationsController(MainForm owner)
        {
            _owner = owner;
        }

        public void StartRenameAfterCreation(string newPath)
        {
            _ = _owner.RefreshCurrentAsync();

            // Wait a bit for the refresh to complete and items to be populated
            Task.Delay(100).ContinueWith(_ =>
            {
                _owner.BeginInvoke(() =>
                {
                    var item = _owner._items.FirstOrDefault(i => i.FullPath.Equals(newPath, StringComparison.OrdinalIgnoreCase));
                    if (item != null)
                    {
                        int index = _owner._items.IndexOf(item);
                        if (index >= 0)
                        {
                            _owner._listView.SelectedIndices.Clear();
                            _owner._listView.SelectedIndices.Add(index);
                            _owner._listView.EnsureVisible(index);
                            _owner._listView.Focus();
                            StartRename();
                        }
                    }
                });
            });
        }

        public void StartRename()
        {
            if (_owner._listView.SelectedIndices.Count == 0)
                return;
            int index = _owner._listView.SelectedIndices[0];
            if (index < 0 || index >= _owner._items.Count)
                return;

            var item = _owner._items[index];

            // Get bounds of the item text
            var bounds = _owner._listView.GetItemRect(index, ItemBoundsPortion.Label);

            // Adjust bounds for icons and padding
            // User wants it exactly 4px further left and 1px up from previous position.
            // Previous was (iconOffset - 14). New is (iconOffset - 18).
            int iconOffset = AppSettings.Current.ShowIcons ? (AppSettings.Current.IconSize + 6) : 4;
            bounds.X += (iconOffset - 18);
            bounds.Y -= 1;
            bounds.Width -= (iconOffset - 18);
            bounds.Height = Math.Max(bounds.Height, 22);

            _owner._renameTextBox = new TextBox
            {
                Text = item.Name,
                Bounds = bounds,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = _owner._listView.Font
            };

            // Select filename only
            int dotIdx = item.Name.LastIndexOf('.');
            if (dotIdx > 0 && !item.IsDirectory)
                _owner._renameTextBox.Select(0, dotIdx);
            else
                _owner._renameTextBox.SelectAll();

            _owner._renameTextBox.LostFocus += (s, e) => EndRename(true);
            _owner._renameTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    EndRename(true);
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    EndRename(false);
                }
            };

            _owner._listView.Controls.Add(_owner._renameTextBox);
            _owner._renameTextBox.Focus();
        }

        public void EndRename(bool commit)
        {
            if (_owner._renameTextBox == null)
                return;

            string newName = _owner._renameTextBox.Text;
            var textBox = _owner._renameTextBox;
            _owner._renameTextBox = null;
            _owner._listView.Controls.Remove(textBox);
            textBox.Dispose();

            if (commit && !string.IsNullOrEmpty(newName))
            {
                int index = _owner._listView.SelectedIndices.Count > 0 ? _owner._listView.SelectedIndices[0] : -1;
                if (index >= 0 && index < _owner._items.Count)
                {
                    var item = _owner._items[index];
                    if (newName != item.Name)
                    {
                        string oldPath = item.FullPath;
                        string newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, newName);
                        FileSystemService.ShellRename(oldPath, newPath, _owner.Handle);
                        TagManager.Instance.HandleRename(oldPath, newPath);
                        _ = _owner.RefreshCurrentAsync();
                    }
                }
            }
        }

        public void CopySelected()
        {
            var paths = _owner.GetSelectedPaths();
            if (paths.Length > 0)
            {
                _owner._cutPaths.Clear();
                PerformClipboardOperation(paths, isCut: false);
                ShowStatusMessage($"Copied {paths.Length} item(s)");
                _owner._listView.Invalidate();
            }
        }

        public void CutSelected()
        {
            var paths = _owner.GetSelectedPaths();
            if (paths.Length > 0)
            {
                _owner._cutPaths.Clear();
                foreach (var p in paths)
                    _owner._cutPaths.Add(p);

                PerformClipboardOperation(paths, isCut: true);
                ShowStatusMessage($"Cut {paths.Length} item(s)");
                _owner._listView.Invalidate();
            }
        }

        public async void Paste()
        {
            if (string.IsNullOrEmpty(_owner._currentPath))
                return;

            var data = Clipboard.GetDataObject();
            if (data != null && data.GetDataPresent(DataFormats.FileDrop))
            {
                var rawPaths = data.GetData(DataFormats.FileDrop);
                if (rawPaths is not string[] paths || paths.Length == 0)
                    return;

                // Detect same-folder operations to prevent "Same File" errors
                bool isSameFolder = paths.Any(p =>
                    string.Equals(Path.GetDirectoryName(p), _owner._currentPath, StringComparison.OrdinalIgnoreCase));

                // Check if it's a Cut operation
                bool isCut = false;
                var dropEffect = data.GetData("Preferred DropEffect") as MemoryStream;
                if (dropEffect != null)
                {
                    int effect = dropEffect.ReadByte();
                    if (effect == 2)
                        isCut = true;
                }

                List<string> addedPaths;

                if (isCut)
                {
                    addedPaths = FileSystemService.ShellMove(paths, _owner._currentPath, _owner.Handle, isSameFolder);
                    _owner._cutPaths.Clear();
                }
                else
                {
                    // For same folder, use renameOnCollision=true for " - Copy" behavior.
                    // For different folders, use renameOnCollision=false to get Windows conflict dialog.
                    addedPaths = FileSystemService.ShellCopy(paths, _owner._currentPath, _owner.Handle, isSameFolder);
                }

                // Refresh with explicit selection of new files.
                await _owner.RefreshCurrentAsync(addedPaths);
            }
        }

        public void DeleteSelected(bool permanent)
        {
            var paths = _owner.GetSelectedPaths();
            if (paths.Length > 0)
            {
                bool effectivePermanent = permanent || AppSettings.Current.PermanentDeleteByDefault;
                FileSystemService.ShellDelete(paths, _owner.Handle, recordOperation: !effectivePermanent, permanent: effectivePermanent);
                _ = _owner.RefreshCurrentAsync();
            }
        }

        public void ShowStatusMessage(string msg)
        {
            _owner._statusLabel.Text = msg;

            if (_owner._statusTimer == null)
            {
                _owner._statusTimer = new System.Windows.Forms.Timer { Interval = 3000 };
                _owner._statusTimer.Tick += (s, e) =>
                {
                    _owner._statusTimer.Stop();
                    _owner._statusLabel.Text = string.Format(Localization.T("status_ready_items"), _owner._items.Count);
                };
            }
            else
            {
                _owner._statusTimer.Stop();
            }
            _owner._statusTimer.Start();
        }

        public void PerformClipboardOperation(string[] paths, bool isCut)
        {
            try
            {
                var dataObject = new DataObject();
                dataObject.SetData(DataFormats.FileDrop, true, paths);

                // "Preferred DropEffect" indicates Copy (1) or Cut (2).
                byte[] dropEffect = new byte[] { (byte)(isCut ? 2 : 1), 0, 0, 0 };
                using var ms = new MemoryStream(dropEffect);
                dataObject.SetData("Preferred DropEffect", ms);

                Clipboard.SetDataObject(dataObject, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Clipboard error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public bool IsClipboardFileContentPresent()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                    return true;

                var data = Clipboard.GetDataObject();
                if (data == null)
                    return false;

                return data.GetDataPresent(DataFormats.FileDrop) ||
                       data.GetDataPresent("FileDrop") ||
                       data.GetDataPresent("FileNameW");
            }
            catch
            {
                return false;
            }
        }
    }
}
