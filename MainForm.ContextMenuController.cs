using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class ContextMenuController
    {
        private readonly MainForm _owner;

        public ContextMenuStrip Menu { get; }

        private readonly ToolStripMenuItem _openItem;
        private readonly ToolStripMenuItem _openWithItem;
        private readonly ToolStripMenuItem _showInExplorerItem;
        private readonly ToolStripMenuItem _openInOtherItem;
        private readonly ToolStripMenuItem _copyPathItem;
        private readonly ToolStripMenuItem _propertiesItem;

        private readonly ToolStripMenuItem _cutItem;
        private readonly ToolStripMenuItem _copyItem;
        private readonly ToolStripMenuItem _pasteItem;
        private readonly ToolStripMenuItem _deleteItem;
        private readonly ToolStripMenuItem _renameItem;
        private readonly ToolStripMenuItem _undoItem;
        private readonly ToolStripMenuItem _redoItem;

        private readonly ToolStripMenuItem _pinItem;
        private readonly ToolStripMenuItem _editTagsItem;

        private readonly ToolStripMenuItem _newItem;
        private readonly ToolStripMenuItem _archiveSubMenu;
        private readonly ToolStripMenuItem _winRarSubMenu;
        private readonly ToolStripMenuItem _integrationsMenu;
        private readonly ToolStripMenuItem _windowsShellItem;

        private readonly ToolStripSeparator _batchAiSeparator;
        private readonly ToolStripMenuItem _batchAiItem;

        // ZIP items
        private readonly ToolStripMenuItem _extractHereItem;
        private readonly ToolStripMenuItem _extractToItem;
        private readonly ToolStripMenuItem _createZipItem;

        // WinRAR items
        private readonly ToolStripMenuItem _addToArchiveItem;
        private readonly ToolStripMenuItem _winRarExtractHereItem;
        private readonly ToolStripMenuItem _winRarExtractToItem;

        public ContextMenuController(MainForm owner)
        {
            _owner = owner;

            Menu = new ContextMenuStrip
            {
                Renderer = new DarkToolStripRenderer(),
                ShowImageMargin = false,
                BackColor = Color.FromArgb(30, 30, 30)
            };

            _openItem = new ToolStripMenuItem(Localization.T("open"), null, (s, e) => _owner.OpenSelectedItem())
            {
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            _openWithItem = new ToolStripMenuItem(Localization.T("open_with"), null, (s, e) => _owner.OpenWithDialog());
            _showInExplorerItem = new ToolStripMenuItem(Localization.T("show_in_explorer"), null, (s, e) => _owner.ShowInExplorer());
            _openInOtherItem = new ToolStripMenuItem(Localization.T("open_new_window"), null, (s, e) => _owner.OpenInOtherTarget());
            _copyPathItem = new ToolStripMenuItem(Localization.T("copy_path"), null, (s, e) => _owner.CopyPathToClipboard());
            _propertiesItem = new ToolStripMenuItem(Localization.T("properties"), null, (s, e) => _owner.ShowProperties());

            _cutItem = new ToolStripMenuItem(Localization.T("cut"), null, (s, e) => _owner.CutSelected());
            _copyItem = new ToolStripMenuItem(Localization.T("copy"), null, (s, e) => _owner.CopySelected());
            _pasteItem = new ToolStripMenuItem(Localization.T("paste"), null, (s, e) => _owner.Paste());
            _deleteItem = new ToolStripMenuItem(Localization.T("delete"), null, (s, e) => _owner.DeleteSelected(permanent: false));
            _renameItem = new ToolStripMenuItem(Localization.T("rename"), null, (s, e) => _owner.StartRename());
            _undoItem = new ToolStripMenuItem(Localization.T("undo"), null, (s, e) => FileSystemService.PerformUndo());
            _redoItem = new ToolStripMenuItem(Localization.T("redo"), null, (s, e) => FileSystemService.PerformRedo());

            _pinItem = new ToolStripMenuItem(Localization.T("pin_sidebar"), null, (s, e) => _owner.TogglePinSelected());
            _editTagsItem = new ToolStripMenuItem(Localization.T("edit_tags"), null, (s, e) => _owner.EditTags());

            _newItem = new ToolStripMenuItem(Localization.T("new"));
            var newFolderItem = new ToolStripMenuItem(Localization.T("new_folder"), null, (s, e) => _owner.CreateNewFolder());
            var newFileItem = new ToolStripMenuItem(Localization.T("new_text_file"), null, (s, e) => _owner.CreateNewTextFile());
            _newItem.DropDownItems.AddRange(new ToolStripItem[] { newFolderItem, newFileItem });

            // Archive (ZIP)
            _archiveSubMenu = new ToolStripMenuItem(Localization.T("archive")) { Visible = false };
            _extractHereItem = new ToolStripMenuItem(Localization.T("extract_here"), null, (s, e) => _owner.ExtractZipHere());
            _extractToItem = new ToolStripMenuItem(Localization.T("extract_to"), null, (s, e) => _owner.ExtractZipToFolder());
            _createZipItem = new ToolStripMenuItem(Localization.T("create_zip"), null, (s, e) => _owner.CreateZipFromSelection());
            _archiveSubMenu.DropDownItems.AddRange(new ToolStripItem[] { _extractHereItem, _extractToItem, new ToolStripSeparator(), _createZipItem });

            // WinRAR
            _winRarSubMenu = new ToolStripMenuItem(Localization.T("winrar")) { Visible = false };
            _addToArchiveItem = new ToolStripMenuItem(Localization.T("add_to_archive"), null, (s, e) => _owner.ExecuteWinRarAddPrompt(_owner.GetSelectedPaths()));
            _winRarExtractHereItem = new ToolStripMenuItem(Localization.T("extract_here"), null, (s, e) => _owner.ExecuteWinRarExtractHere());
            _winRarExtractToItem = new ToolStripMenuItem(Localization.T("extract_to"), null, (s, e) => _owner.ExecuteWinRarExtractTo());
            _winRarSubMenu.DropDownItems.AddRange(new ToolStripItem[] { _addToArchiveItem, new ToolStripSeparator(), _winRarExtractHereItem, _winRarExtractToItem });

            _integrationsMenu = new ToolStripMenuItem(Localization.T("integrations")) { Visible = false };

            _windowsShellItem = new ToolStripMenuItem(Localization.T("windows_shell"), null, (s, e) =>
            {
                var paths = _owner.GetSelectedPaths();
                if (paths.Length == 0)
                {
                    if (!string.IsNullOrEmpty(_owner._currentPath) && !IsShellPath(_owner._currentPath))
                        paths = new[] { _owner._currentPath };
                }

                if (paths.Length == 0) return;

                var pos = Cursor.Position;
                if (!ShellContextMenuService.ShowShellMenu(_owner.Handle, paths, pos.X, pos.Y))
                    _owner._statusLabel.Text = Localization.T("status_shell_unavailable");
            })
            { Visible = false };

            _batchAiSeparator = new ToolStripSeparator() { Visible = false };
            _batchAiItem = new ToolStripMenuItem(Localization.T("batch_ai"), null, _owner.BatchProcess_Click) { Visible = false };

            Menu.Opening += ContextMenu_Opening;

            Menu.Items.AddRange(new ToolStripItem[]
            {
                _openItem,
                _openWithItem,
                _windowsShellItem,
                new ToolStripSeparator(),
                _undoItem,
                _redoItem,
                new ToolStripSeparator(),
                _newItem,
                new ToolStripSeparator(),
                _cutItem,
                _copyItem,
                _pasteItem,
                _deleteItem,
                _renameItem,
                _editTagsItem,
                new ToolStripSeparator(),
                _pinItem,
                _archiveSubMenu,
                _winRarSubMenu,
                _openInOtherItem,
                _showInExplorerItem,
                _copyPathItem,
                new ToolStripSeparator(),
                _integrationsMenu,
                _batchAiSeparator,
                _batchAiItem,
                new ToolStripSeparator(),
                _propertiesItem
            });
        }

        private void ContextMenu_Opening(object? sender, CancelEventArgs e)
        {
            foreach (ToolStripItem item in Menu.Items) item.Visible = true;

            var paths = _owner.GetSelectedPaths();
            bool hasSelection = paths.Length > 0;
            string? firstPath = paths.FirstOrDefault();
            bool isShell = IsShellPath(_owner._currentPath);
            bool allFileSystem = hasSelection 
                ? paths.All(p => File.Exists(p) || Directory.Exists(p))
                : (!string.IsNullOrEmpty(_owner._currentPath) && Directory.Exists(_owner._currentPath));

            if (AppSettings.Current.UseWindowsContextMenu && hasSelection && allFileSystem && !isShell)
            {
                e.Cancel = true;
                var pos = Cursor.Position;
                if (!ShellContextMenuService.ShowShellMenu(_owner.Handle, paths, pos.X, pos.Y))
                    _owner._statusLabel.Text = Localization.T("status_shell_unavailable");
                return;
            }

            bool canManipulate = _owner.CanManipulateSelected();
            bool showFileActions = _owner.IsSearchMode || _owner._currentPath != ThisPcPath;

            _pinItem.Visible = hasSelection;
            _copyItem.Visible = showFileActions; _copyItem.Enabled = canManipulate && hasSelection;
            _cutItem.Visible = showFileActions; _cutItem.Enabled = canManipulate && hasSelection;
            _deleteItem.Visible = showFileActions; _deleteItem.Enabled = canManipulate && hasSelection;
            _renameItem.Visible = showFileActions; _renameItem.Enabled = canManipulate && paths.Length == 1;

            _editTagsItem.Visible = showFileActions;
            _editTagsItem.Enabled = canManipulate && hasSelection;

            _integrationsMenu.DropDownItems.Clear();
            string[] manualActionPaths = paths;
            if (manualActionPaths.Length == 0 &&
                !string.IsNullOrEmpty(_owner._currentPath) &&
                _owner._currentPath != ThisPcPath &&
                !_owner.IsSearchMode &&
                !isShell)
            {
                manualActionPaths = new[] { _owner._currentPath };
            }

            var manualItems = ManualContextActionService.BuildMenuItems(manualActionPaths);
            if (manualItems.Count > 0)
            {
                _integrationsMenu.DropDownItems.AddRange(manualItems.ToArray());
                _integrationsMenu.Visible = true;
            }
            else
            {
                _integrationsMenu.Visible = false;
            }

            _windowsShellItem.Visible = AppSettings.Current.EnableShellContextMenu && allFileSystem && !isShell;

            // OpenWith only for files
            bool isFile = hasSelection && paths.Length == 1 && File.Exists(firstPath) && !Directory.Exists(firstPath);
            _openWithItem.Enabled = isFile;

            bool canPaste = !string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath != ThisPcPath;
            _pasteItem.Visible = canPaste;
            _pasteItem.Enabled = canPaste && !isShell && _owner.IsClipboardFileContentPresent();

            // Undo/Redo descriptions
            var manager = UndoRedoManager.Instance;
            _undoItem.Enabled = manager.CanUndo;
            _undoItem.Text = manager.GetUndoDescription();
            _redoItem.Enabled = manager.CanRedo;
            _redoItem.Text = manager.GetRedoDescription();

            // Pin logic for selection or current path
            if (hasSelection && paths.Length == 1 && firstPath != null)
            {
                _pinItem.Visible = !isShell;
                if (!isShell)
                {
                    bool isPinned = AppSettings.Current.PinnedPaths.Contains(firstPath);
                    _pinItem.Text = isPinned ? Localization.T("unpin_sidebar") : Localization.T("pin_sidebar");
                }
            }
            else if (!hasSelection && !string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath != ThisPcPath && !_owner.IsSearchMode)
            {
                _pinItem.Visible = !isShell;
                if (!isShell)
                {
                    bool isPinned = AppSettings.Current.PinnedPaths.Contains(_owner._currentPath);
                    _pinItem.Text = isPinned ? Localization.T("unpin_current_folder") : Localization.T("pin_current_folder");
                }
            }
            else
            {
                _pinItem.Visible = false;
            }

            // Edit Tags visibility on empty space
            if (!hasSelection && !string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath != ThisPcPath && !_owner.IsSearchMode)
            {
                _editTagsItem.Visible = true;
                _editTagsItem.Enabled = !isShell;
                _editTagsItem.Text = Localization.T("edit_tags");
            }
            else
            {
                _editTagsItem.Visible = showFileActions;
                _editTagsItem.Enabled = canManipulate && hasSelection;
                _editTagsItem.Text = Localization.T("edit_tags");
            }

            // New item should not be visible in search mode or This PC root, or when invoked from sidebar
            _newItem.Visible = !string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath != ThisPcPath &&
                               !_owner.IsSearchMode && Menu.SourceControl != _owner._sidebar;

            // Show in Explorer / open current folder
            _showInExplorerItem.Visible = hasSelection || (!string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath != ThisPcPath);
            _showInExplorerItem.Text = hasSelection ? Localization.T("show_in_explorer") : Localization.T("open_current_folder");

            // Copy Path / copy current path
            _copyPathItem.Visible = hasSelection || (!string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath != ThisPcPath);
            _copyPathItem.Text = hasSelection ? Localization.T("copy_path") : Localization.T("copy_current_path");

            // Open in other target (tab/window depends on setting)
            bool defaultIsTab = AppSettings.Current.MiddleClickOpensNewTab;
            string? openOtherPath = _owner.GetOpenInOtherTargetPath();
            _openInOtherItem.Visible = openOtherPath != null;
            _openInOtherItem.Enabled = openOtherPath != null;
            _openInOtherItem.Text = defaultIsTab ? Localization.T("open_new_window") : Localization.T("open_new_tab");

            // Batch AI item
            bool llmEnabled = AppSettings.Current.LlmEnabled;
            bool allFiles = hasSelection && paths.All(p => File.Exists(p) && !Directory.Exists(p));
            _batchAiItem.Visible = llmEnabled && allFiles;
            _batchAiSeparator.Visible = llmEnabled && allFiles;

            // Archive (ZIP)
            bool isZip = hasSelection && paths.Length == 1 && IsZipFilePath(firstPath);
            _archiveSubMenu.Visible = !isShell && hasSelection && Menu.SourceControl != _owner._sidebar;
            _extractHereItem.Visible = isZip;
            _extractToItem.Visible = isZip;
            _createZipItem.Visible = hasSelection;
            _createZipItem.Enabled = canManipulate && paths.All(p => File.Exists(p) || Directory.Exists(p));
            _extractToItem.Text = Localization.T("extract_to");

            // WinRAR
            string winRarPath = _owner.GetWinRarPath();
            bool winRarExists = !string.IsNullOrEmpty(winRarPath);
            bool allowWinRar = _owner._currentPath != ThisPcPath && !_owner.IsSearchMode;
            _winRarSubMenu.Visible = allowWinRar && winRarExists && hasSelection && Menu.SourceControl != _owner._sidebar;
            bool singleArchive = hasSelection && paths.Length == 1 && IsArchiveFilePath(firstPath);
            _winRarExtractHereItem.Visible = singleArchive;
            _winRarExtractToItem.Visible = singleArchive;

            _propertiesItem.Visible = hasSelection || (!string.IsNullOrEmpty(_owner._currentPath) && _owner._currentPath != ThisPcPath);

            // Hide disabled items if invoked from sidebar
            if (Menu.SourceControl == _owner._sidebar)
            {
                foreach (ToolStripItem item in Menu.Items)
                {
                    if (item is ToolStripMenuItem mi && !mi.Enabled)
                        mi.Visible = false;
                }
            }

            // Auto-hide redundant separators (leading/trailing/consecutive)
            bool visibleItemAbove = false;
            ToolStripSeparator? pendingSeparator = null;

            foreach (ToolStripItem item in Menu.Items)
            {
                if (item is ToolStripSeparator sep)
                {
                    sep.Available = false;
                    if (visibleItemAbove) pendingSeparator = sep;
                }
                else if (item.Available)
                {
                    if (pendingSeparator != null)
                    {
                        pendingSeparator.Available = true;
                        pendingSeparator = null;
                    }
                    visibleItemAbove = true;
                }
            }
        }
    }
}
