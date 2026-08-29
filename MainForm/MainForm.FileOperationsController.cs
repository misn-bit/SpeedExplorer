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
    BrowserState IFileOperationsHost.BrowserState => State;
    ListView IFileOperationsHost.FileListView => _listView;
    TextBox? IFileOperationsHost.RenameTextBox
    {
        get => _renameTextBox;
        set => _renameTextBox = value;
    }
    IntPtr IFileOperationsHost.WindowHandle => Handle;
    int IFileOperationsHost.EffectiveIconSize => GetEffectiveIconSize();
    string[] IFileOperationsHost.GetSelectedPaths() => GetSelectedPaths();
    Task IFileOperationsHost.RefreshCurrentAsync(List<string>? selectPaths)
        => RefreshCurrentAsync(selectPaths);
    void IFileOperationsHost.ApplyMoveToCachedSnapshots(IEnumerable<string> sourcePaths)
        => _tabsController.ApplyMoveToCachedSnapshots(sourcePaths);
    void IFileOperationsHost.ShowStatusMessage(string message)
        => ShowFileOperationStatusMessage(message);

    private void ShowFileOperationStatusMessage(string msg)
    {
        _statusLabel.Text = msg;

        if (_statusTimer == null)
        {
            _statusTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _statusTimer.Tick += (s, e) =>
            {
                _statusTimer.Stop();
                _statusLabel.Text = string.Format(Localization.T("status_ready_items"), State.Items.Count);
            };
        }
        else
        {
            _statusTimer.Stop();
        }

        _statusTimer.Start();
    }

    private sealed class FileOperationsController
    {
        private readonly IFileOperationsHost _host;
        private BrowserState State => _host.BrowserState;
        private bool _renameCommitting;

        public FileOperationsController(IFileOperationsHost host)
        {
            _host = host;
        }

        public async void StartRenameAfterCreation(string newPath)
        {
            await _host.RefreshCurrentAsync();

            var item = State.Items.FirstOrDefault(i => i.FullPath.Equals(newPath, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                int index = State.Items.IndexOf(item);
                if (index >= 0)
                {
                    _host.FileListView.SelectedIndices.Clear();
                    _host.FileListView.SelectedIndices.Add(index);
                    _host.FileListView.EnsureVisible(index);
                    _host.FileListView.Focus();
                    StartRename();
                }
            }
        }

        public void StartRename()
        {
            if (_host.FileListView.SelectedIndices.Count == 0)
                return;
            int index = _host.FileListView.SelectedIndices[0];
            if (index < 0 || index >= State.Items.Count)
                return;

            var item = State.Items[index];

            // Get bounds of the item text
            var bounds = _host.FileListView.GetItemRect(index, ItemBoundsPortion.Label);

            // Adjust bounds for icons and padding
            // User wants it exactly 4px further left and 1px up from previous position.
            // Previous was (iconOffset - 14). New is (iconOffset - 18).
            int iconOffset = AppSettings.Current.ShowIcons ? (_host.EffectiveIconSize + 6) : 4;
            bounds.X += (iconOffset - 18);
            bounds.Y -= 1;
            bounds.Width -= (iconOffset - 18);
            bounds.Height = Math.Max(bounds.Height, 22);

            var renameTextBox = new TextBox
            {
                Text = item.Name,
                Bounds = bounds,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = _host.FileListView.Font
            };

            // Select filename only
            int dotIdx = item.Name.LastIndexOf('.');
            if (dotIdx > 0 && !item.IsDirectory)
                renameTextBox.Select(0, dotIdx);
            else
                renameTextBox.SelectAll();

            renameTextBox.LostFocus += (s, e) => EndRename(true);
            renameTextBox.KeyDown += (s, e) =>
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

            _host.RenameTextBox = renameTextBox;
            _host.FileListView.Controls.Add(renameTextBox);
            renameTextBox.Focus();
        }

        public void EndRename(bool commit)
        {
            var renameTextBox = _host.RenameTextBox;
            if (renameTextBox == null || _renameCommitting)
                return;
            _renameCommitting = true;

            string newName = renameTextBox.Text;
            _host.RenameTextBox = null;
            _host.FileListView.Controls.Remove(renameTextBox);
            renameTextBox.Dispose();
            _renameCommitting = false;

            if (commit && !string.IsNullOrEmpty(newName))
            {
                int index = _host.FileListView.SelectedIndices.Count > 0 ? _host.FileListView.SelectedIndices[0] : -1;
                if (index >= 0 && index < State.Items.Count)
                {
                    var item = State.Items[index];
                    if (newName != item.Name)
                    {
                        string oldPath = item.FullPath;
                        string newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, newName);
                        if (FileSystemService.ShellRename(oldPath, newName, _host.WindowHandle))
                        {
                            TagManager.Instance.HandleRename(oldPath, newPath);
                            _ = _host.RefreshCurrentAsync();
                        }
                    }
                }
            }
        }

        public void CopySelected()
        {
            var paths = _host.GetSelectedPaths();
            if (paths.Length > 0)
            {
                State.CutPaths.Clear();
                PerformClipboardOperation(paths, isCut: false);
                _host.ShowStatusMessage($"Copied {paths.Length} item(s)");
                _host.FileListView.Invalidate();
            }
        }

        public void CutSelected()
        {
            var paths = _host.GetSelectedPaths();
            if (paths.Length > 0)
            {
                State.CutPaths.Clear();
                foreach (var p in paths)
                    State.CutPaths.Add(p);

                PerformClipboardOperation(paths, isCut: true);
                _host.ShowStatusMessage($"Cut {paths.Length} item(s)");
                _host.FileListView.Invalidate();
            }
        }

        public async void Paste()
        {
            if (string.IsNullOrEmpty(State.CurrentPath))
                return;

            var data = Clipboard.GetDataObject();
            if (data != null && data.GetDataPresent(DataFormats.FileDrop))
            {
                var rawPaths = data.GetData(DataFormats.FileDrop);
                if (rawPaths is not string[] paths || paths.Length == 0)
                    return;

                // Detect same-folder operations to prevent "Same File" errors
                bool isSameFolder = paths.Any(p =>
                    string.Equals(Path.GetDirectoryName(p), State.CurrentPath, StringComparison.OrdinalIgnoreCase));

                // Check if it's a Cut operation
                bool isCut = false;
                var dropEffect = data.GetData("Preferred DropEffect") as MemoryStream;
                if (dropEffect != null)
                {
                    int effect = dropEffect.ReadByte();
                    if (effect == 2)
                        isCut = true;
                }

                try
                {
                    List<string> addedPaths;

                    if (isCut)
                    {
                        addedPaths = await FileSystemService.ShellMoveAsync(paths, State.CurrentPath, _host.WindowHandle, isSameFolder);
                        State.CutPaths.Clear();
                        _host.ApplyMoveToCachedSnapshots(paths);
                    }
                    else
                    {
                        // For same folder, use renameOnCollision=true for " - Copy" behavior.
                        // For different folders, use renameOnCollision=false to get Windows conflict dialog.
                        addedPaths = await FileSystemService.ShellCopyAsync(paths, State.CurrentPath, _host.WindowHandle, isSameFolder);
                    }

                    // Refresh with explicit selection of new files.
                    await _host.RefreshCurrentAsync(addedPaths);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Paste operation failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public async void DeleteSelected(bool permanent)
        {
            var paths = _host.GetSelectedPaths();
            if (paths.Length > 0)
            {
                bool effectivePermanent = permanent || AppSettings.Current.PermanentDeleteByDefault;
                try
                {
                    await FileSystemService.ShellDeleteAsync(paths, _host.WindowHandle, recordOperation: !effectivePermanent, permanent: effectivePermanent);
                    _ = _host.RefreshCurrentAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Delete operation failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
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
