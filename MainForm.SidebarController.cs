using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class SidebarController
    {
        private readonly MainForm _owner;
        private TreeView? _tree;

        private TreeNode? _hoveredNode;
        private string? _dragPath;
        private int _dragInsertIndex = -1;
        private string? _dragBlockId;
        private int _dragBlockInsertIndex = -1;

        private const string SidebarBlockDragPrefix = "__SIDEBAR_BLOCK__:";
        private static readonly string[] SidebarBlockOrderDefault = { "portable", "common", "pinned", "recent" };
        private const int GWL_STYLE = -16;
        private const int TVS_NOHSCROLL = 0x8000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public SidebarController(MainForm owner)
        {
            _owner = owner;
        }

        public TreeView CreateSidebar(ContextMenuStrip contextMenu)
        {
            var tree = new TreeView
            {
                BackColor = SidebarColor,
                ForeColor = ForeColor_Dark,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI Emoji", 10),
                ShowLines = false,
                FullRowSelect = true,
                ItemHeight = 28,
                Scrollable = true,
                DrawMode = TreeViewDrawMode.OwnerDrawText,
                Indent = 0
            };

            tree.DrawNode += Sidebar_DrawNode;
            tree.BeforeSelect += (s, e) =>
            {
                if (IsSeparatorNode(e.Node, out _))
                    e.Cancel = true;
            };

            // Double buffering
            typeof(TreeView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(tree, true);

            tree.NodeMouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    if (!IsSeparatorNode(e.Node, out _))
                        tree.SelectedNode = e.Node;
                }

                if (e.Button == MouseButtons.Left && e.Node?.Tag is string path && !IsSeparatorTag(path))
                {
                    if (tree.SelectedNode != e.Node)
                        tree.SelectedNode = e.Node;

                    if (IsShellPath(path))
                    {
                        _owner.ObserveTask(_owner.NavigateTo(path), "Sidebar.AfterPinNavigate");
                        _owner._listView.Focus();
                    }
                    else if (path == ThisPcPath || Directory.Exists(path))
                    {
                        _owner.ObserveTask(_owner.NavigateTo(path), "Sidebar.MiddleClickNavigate");
                        _owner._listView.Focus();
                    }
                }
            };

            tree.NodeMouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && e.Node?.Tag is string path && !IsSeparatorTag(path))
                {
                    if (File.Exists(path))
                    {
                        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
                        _owner._listView.Focus();
                    }
                }
            };

            tree.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Middle)
                {
                    var hit = tree.HitTest(e.Location);
                    if (hit.Node != null && hit.Node.Tag is string path && !IsSeparatorTag(path))
                    {
                        if (path == ThisPcPath || (Directory.Exists(path) && FileSystemService.IsAccessible(path)))
                        {
                            if (AppSettings.Current.MiddleClickOpensNewTab)
                                _owner.OpenPathInNewTab(path, activate: false);
                            else
                                Program.MultiWindowContext.Instance.ShowNext(new MainForm(path));
                        }
                        else
                        {
                            _owner._statusLabel.Text = string.Format(Localization.T("status_access_denied"), path);
                        }
                    }
                }
            };

            tree.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;

                    if (tree.SelectedNode?.Tag is string path && !IsSeparatorTag(path))
                    {
                        if (File.Exists(path))
                            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
                        else
                            _owner.ObserveTask(_owner.NavigateTo(path), "Sidebar.NodeNavigate");

                        _owner._listView.Focus();
                    }
                }
            };

            tree.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) return;
                if (tree.Nodes.Count == 0) return;
                if (tree.SelectedNode == null) return;

                int direction = e.KeyCode == Keys.Down ? 1 : -1;
                int index = tree.SelectedNode.Index;
                int nextIndex = index + direction;

                while (nextIndex >= 0 && nextIndex < tree.Nodes.Count)
                {
                    var node = tree.Nodes[nextIndex];
                    if (!IsSeparatorNode(node, out _))
                    {
                        tree.SelectedNode = node;
                        e.Handled = true;
                        break;
                    }
                    nextIndex += direction;
                }
            };

            tree.KeyPress += (s, e) =>
            {
                if (e.KeyChar == (char)Keys.Enter)
                    e.Handled = true;
            };

            tree.HandleCreated += (s, e) =>
            {
                SetWindowTheme(tree.Handle, "DarkMode_Explorer", null);
                int darkMode = 1;
                DwmSetWindowAttribute(tree.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                ApplySidebarScrollbars(tree);
            };

            tree.Resize += (s, e) =>
            {
                ApplySidebarScrollbars(tree);
                tree.Invalidate();
            };

            tree.AllowDrop = true;
            tree.ItemDrag += Sidebar_ItemDrag;
            tree.DragOver += Sidebar_DragOver;
            tree.DragLeave += (s, e) => ClearSidebarDragVisuals();
            tree.DragDrop += Sidebar_DragDrop;
            tree.MouseMove += Sidebar_MouseMove;
            tree.MouseLeave += (s, e) =>
            {
                if (_hoveredNode != null)
                {
                    var oldNode = _hoveredNode;
                    _hoveredNode = null;
                    InvalidateSidebarRow(oldNode);
                }
            };

            tree.ContextMenuStrip = contextMenu;

            _tree = tree;
            _owner._sidebar = tree;
            PopulateSidebar();

            return tree;
        }

        private void ApplySidebarScrollbars(TreeView tree)
        {
            try
            {
                int style = GetWindowLong(tree.Handle, GWL_STYLE);
                if ((style & TVS_NOHSCROLL) == 0)
                {
                    SetWindowLong(tree.Handle, GWL_STYLE, style | TVS_NOHSCROLL);
                    SetWindowPos(tree.Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
                }
            }
            catch
            {
            }

            try { ShowScrollBar(tree.Handle, SB_HORZ, false); } catch { }
            try { ShowScrollBar(tree.Handle, SB_VERT, AppSettings.Current.ShowSidebarVerticalScrollbar); } catch { }
        }

        public void PopulateSidebar()
        {
            if (_tree == null) return;
            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            // 0. This PC
            var thisPcNode = new TreeNode($"🖥️ {Localization.T("this_pc")}")
            {
                Tag = ThisPcPath,
                ImageKey = "computer",
                SelectedImageKey = "computer"
            };
            _tree.Nodes.Add(thisPcNode);

            // 1. Drives
            foreach (var drive in FileSystemService.GetDrives())
            {
                var isUsb = drive.DriveType == DriveType.Removable;
                var label = string.IsNullOrEmpty(drive.VolumeLabel) ? (isUsb ? Localization.T("usb_drive") : Localization.T("local_disk")) : drive.VolumeLabel;
                var emoji = "💾";

                var node = new TreeNode($"{emoji} {drive.Name.TrimEnd('\\')} {label}")
                {
                    Tag = drive.RootDirectory.FullName
                };
                _tree.Nodes.Add(node);
            }

            var blockNodes = new Dictionary<string, List<TreeNode>>(StringComparer.OrdinalIgnoreCase)
            {
                ["portable"] = CreatePortableDeviceNodes(),
                ["common"] = CreateCommonFolderNodes(),
                ["pinned"] = CreatePinnedNodes(),
                ["recent"] = CreateRecentFolderNodes()
            };

            foreach (var blockId in GetSidebarBlockOrder())
            {
                if (!blockNodes.TryGetValue(blockId, out var nodes) || nodes.Count == 0)
                    continue;

                _tree.Nodes.Add(new TreeNode("") { ForeColor = Color.FromArgb(80, 80, 80), Tag = CreateSeparatorTag(blockId) });
                foreach (var node in nodes)
                {
                    _tree.Nodes.Add(node);
                }
            }

            _tree.EndUpdate();
            ApplySidebarScrollbars(_tree);
        }

        private static bool IsSeparatorTag(string? tag)
            => !string.IsNullOrEmpty(tag) && tag.StartsWith(SidebarSeparatorTag, StringComparison.Ordinal);

        private static string CreateSeparatorTag(string blockId)
            => $"{SidebarSeparatorTag}:{blockId}";

        private static bool TryParseSeparatorBlockId(string? tag, out string blockId)
        {
            blockId = string.Empty;
            if (!IsSeparatorTag(tag))
                return false;

            int idx = tag!.IndexOf(':');
            if (idx < 0 || idx == tag.Length - 1)
                return false;

            blockId = tag.Substring(idx + 1).Trim().ToLowerInvariant();
            return blockId.Length > 0;
        }

        private static bool IsSeparatorNode(TreeNode? node, out string blockId)
        {
            blockId = string.Empty;
            if (node?.Tag is not string tag)
                return false;
            return TryParseSeparatorBlockId(tag, out blockId) || string.Equals(tag, SidebarSeparatorTag, StringComparison.Ordinal);
        }

        private static bool TryGetDraggedBlockId(string raw, out string blockId)
        {
            blockId = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            if (!raw.StartsWith(SidebarBlockDragPrefix, StringComparison.Ordinal))
                return false;
            blockId = raw.Substring(SidebarBlockDragPrefix.Length).Trim().ToLowerInvariant();
            return blockId.Length > 0;
        }

        private static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        private List<string> GetSidebarBlockOrder()
        {
            var stored = AppSettings.Current.SidebarBlockOrder ?? new List<string>();
            var normalized = new List<string>(SidebarBlockOrderDefault.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in stored)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (!SidebarBlockOrderDefault.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
                if (!seen.Add(id)) continue;
                normalized.Add(id.ToLowerInvariant());
            }

            foreach (var id in SidebarBlockOrderDefault)
            {
                if (seen.Add(id))
                    normalized.Add(id);
            }

            return normalized;
        }

        private List<TreeNode> CreatePortableDeviceNodes()
        {
            var nodes = new List<TreeNode>();
            var portableDevices = GetPortableDevices();
            foreach (var device in portableDevices)
            {
                var shellId = _owner.RegisterShellItem(device.Item, null);
                nodes.Add(new TreeNode($"📱 {device.Name}") { Tag = shellId });
            }
            return nodes;
        }

        private List<TreeNode> CreateCommonFolderNodes()
        {
            var nodes = new List<TreeNode>();
            var s = AppSettings.Current;
            if (!s.ShowSidebarCommon)
                return nodes;

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var commonFolders = new List<(string Name, string Path)>();
            if (s.ShowSidebarDesktop) commonFolders.Add(($"📂 {Localization.T("desktop")}", Environment.GetFolderPath(Environment.SpecialFolder.Desktop)));
            if (s.ShowSidebarDocuments) commonFolders.Add(($"📂 {Localization.T("documents")}", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
            if (s.ShowSidebarDownloads) commonFolders.Add(($"📂 {Localization.T("downloads")}", Path.Combine(userProfile, "Downloads")));
            if (s.ShowSidebarPictures) commonFolders.Add(($"📂 {Localization.T("pictures")}", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)));

            foreach (var (name, path) in commonFolders)
            {
                if (!Directory.Exists(path)) continue;
                nodes.Add(new TreeNode(name) { Tag = path });
            }

            return nodes;
        }

        private List<TreeNode> CreatePinnedNodes()
        {
            var nodes = new List<TreeNode>();
            var pinned = AppSettings.Current.PinnedPaths;
            if (pinned == null) return nodes;

            foreach (var path in pinned)
            {
                if (!Directory.Exists(path) && !File.Exists(path)) continue;

                var isDir = Directory.Exists(path);
                var name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) name = path;

                string icon = isDir ? "📂" : GetTypeIconForPinnedFile(path);
                nodes.Add(new TreeNode($"{icon} {name}") { Tag = path, Name = SidebarPinnedNodeName });
            }

            return nodes;
        }

        private List<TreeNode> CreateRecentFolderNodes()
        {
            var nodes = new List<TreeNode>();
            if (!AppSettings.Current.ShowSidebarRecent)
                return nodes;

            foreach (var path in GetRecentFolders(maxCount: 12))
            {
                var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var name = Path.GetFileName(trimmed);
                if (string.IsNullOrWhiteSpace(name)) name = path;
                nodes.Add(new TreeNode($"🕘 {name}") { Tag = path });
            }
            return nodes;
        }

        private static List<string> GetRecentFolders(int maxCount)
        {
            var recentFolders = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var recentPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                if (string.IsNullOrEmpty(recentPath) || !Directory.Exists(recentPath))
                    return recentFolders;

                var links = Directory.EnumerateFiles(recentPath, "*.lnk", SearchOption.TopDirectoryOnly)
                    .Select(static p => new FileInfo(p))
                    .OrderByDescending(static fi => fi.LastWriteTimeUtc);

                var wshType = Type.GetTypeFromProgID("WScript.Shell");
                if (wshType == null)
                    return recentFolders;

                dynamic? wsh = Activator.CreateInstance(wshType);
                if (wsh == null)
                    return recentFolders;

                foreach (var link in links)
                {
                    try
                    {
                        dynamic? shortcut = wsh.CreateShortcut(link.FullName);
                        string? target = shortcut?.TargetPath as string;
                        if (string.IsNullOrWhiteSpace(target)) continue;
                        if (!Directory.Exists(target)) continue;
                        if (!seen.Add(target)) continue;

                        recentFolders.Add(target);
                        if (recentFolders.Count >= maxCount)
                            break;
                    }
                    catch
                    {
                        // Ignore malformed links.
                    }
                }
            }
            catch
            {
                // Keep sidebar usable even if recent resolution fails.
            }

            return recentFolders;
        }

        private List<(string Name, object Item)> GetPortableDevices()
        {
            var devices = new List<(string Name, object Item)>();
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return devices;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return devices;

                dynamic? folder = shell.NameSpace(0x11);
                if (folder == null) return devices;

                foreach (var item in folder.Items())
                {
                    try
                    {
                        bool isFileSystem = item.IsFileSystem;
                        string name = item.Name;

                        if (!isFileSystem && !string.IsNullOrEmpty(name))
                            devices.Add((name, item));
                    }
                    catch { }
                }
            }
            catch { }

            return devices;
        }

        private string GetTypeIconForPinnedFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".ico" or ".tiff" or ".tif" => "🖼️ ",
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" => "🎬 ",
                ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or ".aac" => "🎵 ",
                ".txt" or ".md" or ".log" => "📝 ",
                ".pdf" => "📕 ",
                ".doc" or ".docx" => "📘 ",
                ".xls" or ".xlsx" => "📗 ",
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "📦 ",
                ".exe" or ".msi" => "⚙️ ",
                ".dll" => "🔧 ",
                ".cs" or ".py" or ".js" or ".ts" or ".cpp" or ".c" or ".h" or ".java" => "💻 ",
                ".html" or ".htm" or ".css" => "🌐 ",
                ".json" or ".xml" or ".yaml" or ".yml" => "📋 ",
                _ => "📄 "
            };
        }

        private void Sidebar_DrawNode(object? sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == null) return;

            var isSelected = e.Node.IsSelected;
            var tree = (TreeView)sender!;
            bool isSeparator = IsSeparatorNode(e.Node, out var separatorBlockId);

            int right = tree.ClientRectangle.Width;
            var bgRect = new Rectangle(0, e.Bounds.Y, right, e.Bounds.Height);

            Color rowColor = SidebarColor;
            if (!isSeparator && isSelected)
                rowColor = Color.FromArgb(0, 120, 212);
            else if (!isSeparator && e.Node == _hoveredNode)
                rowColor = Color.FromArgb(60, 60, 60);

            using (var brush = new SolidBrush(rowColor))
                e.Graphics.FillRectangle(brush, bgRect);

            if (isSeparator)
            {
                int y = e.Bounds.Top + (e.Bounds.Height / 2);
                using var pen = new Pen(Color.FromArgb(80, 80, 80), 1);
                e.Graphics.DrawLine(pen, _owner.Scale(8), y, right - _owner.Scale(8), y);

                if (!string.IsNullOrEmpty(_dragBlockId) && _dragBlockInsertIndex >= 0 && !string.IsNullOrEmpty(separatorBlockId))
                {
                    var order = GetSidebarBlockOrder();
                    int idx = order.IndexOf(separatorBlockId);
                    if (idx >= 0)
                    {
                        if (_dragBlockInsertIndex == idx)
                        {
                            int yTop = e.Bounds.Top;
                            using var insertPen = new Pen(Color.FromArgb(140, 140, 140), 1);
                            e.Graphics.DrawLine(insertPen, _owner.Scale(6), yTop, right - _owner.Scale(6), yTop);
                        }
                        else if (_dragBlockInsertIndex == idx + 1)
                        {
                            int yBottom = e.Bounds.Bottom - 1;
                            using var insertPen = new Pen(Color.FromArgb(140, 140, 140), 1);
                            e.Graphics.DrawLine(insertPen, _owner.Scale(6), yBottom, right - _owner.Scale(6), yBottom);
                        }
                    }
                }

                return;
            }

            var text = e.Node.Text;
            var textColor = isSelected ? Color.White : ForeColor_Dark;
            var textRect = new Rectangle(0, e.Bounds.Y, right, e.Bounds.Height);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping;
            TextRenderer.DrawText(e.Graphics, text, tree.Font, textRect, textColor, flags);

            // Ghost preview for dragged pinned item
            if (!string.IsNullOrEmpty(_dragPath) && e.Node.Tag is string nodePath &&
                string.Equals(nodePath, _dragPath, StringComparison.OrdinalIgnoreCase))
            {
                using var overlay = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
                e.Graphics.FillRectangle(overlay, new Rectangle(0, e.Bounds.Y, right, e.Bounds.Height));
            }

            // Insertion line
            if (_dragInsertIndex >= 0 && IsPinnedNode(e.Node))
            {
                var pinned = AppSettings.Current.PinnedPaths;
                int idx = pinned.IndexOf(e.Node.Tag as string ?? "");
                if (idx >= 0 && _dragInsertIndex == idx)
                {
                    int y = e.Bounds.Top;
                    using var pen = new Pen(Color.FromArgb(140, 140, 140), 1);
                    e.Graphics.DrawLine(pen, _owner.Scale(6), y, right - _owner.Scale(6), y);
                }
                else if (idx >= 0 && _dragInsertIndex == idx + 1)
                {
                    int y = e.Bounds.Bottom - 1;
                    using var pen = new Pen(Color.FromArgb(140, 140, 140), 1);
                    e.Graphics.DrawLine(pen, _owner.Scale(6), y, right - _owner.Scale(6), y);
                }
            }
        }

        private void Sidebar_MouseMove(object? sender, MouseEventArgs e)
        {
            if (_tree == null) return;

            var hit = _tree.HitTest(e.Location);
            var newNode = hit.Node;
            if (newNode != _hoveredNode)
            {
                var oldNode = _hoveredNode;
                _hoveredNode = newNode;
                if (oldNode != null) InvalidateSidebarRow(oldNode);
                if (_hoveredNode != null) InvalidateSidebarRow(_hoveredNode);
            }
        }

        private void InvalidateSidebarRow(TreeNode node)
        {
            if (_tree == null || node == null) return;
            var rect = new Rectangle(0, node.Bounds.Y, _tree.ClientSize.Width, node.Bounds.Height);
            _tree.Invalidate(rect);
        }

        private bool IsPinnedNode(TreeNode? node)
        {
            if (node == null) return false;
            if (node.Name != SidebarPinnedNodeName) return false;
            if (node.Tag is not string path) return false;
            return AppSettings.Current.PinnedPaths.Contains(path);
        }

        private void Sidebar_ItemDrag(object? sender, ItemDragEventArgs e)
        {
            if (_tree == null) return;

            var node = e.Item as TreeNode;
            if (IsPinnedNode(node))
            {
                var path = node!.Tag as string;
                if (string.IsNullOrEmpty(path) || !AppSettings.Current.PinnedPaths.Contains(path))
                    return;

                _dragPath = path;
                _dragInsertIndex = -1;
                _dragBlockId = null;
                _dragBlockInsertIndex = -1;
                _tree.DoDragDrop(path, DragDropEffects.Move);
                return;
            }

            if (IsSeparatorNode(node, out var blockId) && !string.IsNullOrEmpty(blockId))
            {
                _dragPath = null;
                _dragInsertIndex = -1;
                _dragBlockId = blockId;
                _dragBlockInsertIndex = -1;
                _tree.DoDragDrop(SidebarBlockDragPrefix + blockId, DragDropEffects.Move);
            }
        }

        private void Sidebar_DragOver(object? sender, DragEventArgs e)
        {
            if (_tree == null) return;

            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                e.Effect = DragDropEffects.None;
                ClearSidebarDragVisuals();
                return;
            }

            var raw = e.Data.GetData(DataFormats.StringFormat) as string;
            if (string.IsNullOrWhiteSpace(raw))
            {
                e.Effect = DragDropEffects.None;
                ClearSidebarDragVisuals();
                return;
            }

            var clientPoint = _tree.PointToClient(new Point(e.X, e.Y));
            var target = _tree.GetNodeAt(clientPoint);

            if (TryGetDraggedBlockId(raw, out var dragBlockId))
            {
                var order = GetSidebarBlockOrder();
                int fromIndex = order.IndexOf(dragBlockId);
                if (fromIndex < 0)
                {
                    e.Effect = DragDropEffects.None;
                    ClearSidebarDragVisuals();
                    return;
                }

                int toIndex;
                if (target == null)
                {
                    toIndex = order.Count;
                }
                else if (IsSeparatorNode(target, out var targetBlockId) && !string.IsNullOrEmpty(targetBlockId))
                {
                    int targetIdx = order.IndexOf(targetBlockId);
                    if (targetIdx < 0)
                    {
                        e.Effect = DragDropEffects.None;
                        ClearSidebarDragVisuals();
                        return;
                    }
                    int midY = target.Bounds.Top + (target.Bounds.Height / 2);
                    toIndex = clientPoint.Y >= midY ? targetIdx + 1 : targetIdx;
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                    ClearSidebarDragVisuals();
                    return;
                }

                e.Effect = DragDropEffects.Move;
                _dragBlockId = dragBlockId;
                _dragBlockInsertIndex = Clamp(toIndex, 0, order.Count);
                _tree.Invalidate();
                return;
            }

            if (target == null)
            {
                // Allow dropping to the end when dragging below the last node.
                e.Effect = DragDropEffects.Move;
                _dragInsertIndex = AppSettings.Current.PinnedPaths.Count;
                _tree.Invalidate();
                return;
            }

            if (IsPinnedNode(target))
            {
                e.Effect = DragDropEffects.Move;
                _dragInsertIndex = GetPinnedInsertIndex(target, clientPoint);
                _tree.Invalidate();
                return;
            }

            if (IsSeparatorNode(target, out _))
            {
                // Only allow dropping on the separator that sits right before the pinned section.
                int idx = target.Index;
                if (idx + 1 < _tree.Nodes.Count && IsPinnedNode(_tree.Nodes[idx + 1]))
                {
                    e.Effect = DragDropEffects.Move;
                    _dragInsertIndex = 0;
                    _tree.Invalidate();
                    return;
                }
            }

            e.Effect = DragDropEffects.None;
            ClearSidebarDragVisuals();
        }

        private void Sidebar_DragDrop(object? sender, DragEventArgs e)
        {
            if (_tree == null) return;
            try
            {
                if (e.Data == null || !e.Data.GetDataPresent(DataFormats.StringFormat)) return;
                var rawData = e.Data.GetData(DataFormats.StringFormat) as string;
                if (string.IsNullOrWhiteSpace(rawData)) return;

                if (TryGetDraggedBlockId(rawData, out var dragBlockId))
                {
                    var order = GetSidebarBlockOrder();
                    int fromIndex = order.IndexOf(dragBlockId);
                    if (fromIndex < 0) return;

                    int toIndex = _dragBlockInsertIndex >= 0 ? _dragBlockInsertIndex : fromIndex;
                    toIndex = Clamp(toIndex, 0, order.Count);

                    if (toIndex == fromIndex || toIndex == fromIndex + 1) return;

                    order.RemoveAt(fromIndex);
                    if (toIndex > fromIndex) toIndex--;
                    toIndex = Clamp(toIndex, 0, order.Count);
                    order.Insert(toIndex, dragBlockId);

                    AppSettings.Current.SidebarBlockOrder = order;
                    AppSettings.Current.Save();

                    PopulateSidebar();
                    return;
                }

                var path = rawData;

                var clientPoint = _tree.PointToClient(new Point(e.X, e.Y));
                var target = _tree.GetNodeAt(clientPoint);

                var pinned = AppSettings.Current.PinnedPaths;
                int pinnedFromIndex = pinned.IndexOf(path);
                if (pinnedFromIndex < 0) return;

                int pinnedToIndex;
                if (_dragInsertIndex >= 0)
                {
                    pinnedToIndex = _dragInsertIndex;
                }
                else if (target != null && IsPinnedNode(target))
                {
                    pinnedToIndex = GetPinnedInsertIndex(target, clientPoint);
                }
                else
                {
                    pinnedToIndex = pinned.Count;
                }

                pinnedToIndex = Math.Max(0, Math.Min(pinnedToIndex, pinned.Count));
                if (pinnedToIndex == pinnedFromIndex || pinnedToIndex == pinnedFromIndex + 1) return;

                pinned.RemoveAt(pinnedFromIndex);
                if (pinnedToIndex > pinnedFromIndex) pinnedToIndex--;
                pinnedToIndex = Math.Max(0, Math.Min(pinnedToIndex, pinned.Count));
                pinned.Insert(pinnedToIndex, path);
                AppSettings.Current.Save();

                PopulateSidebar();
                SelectSidebarNodeByPath(path);
            }
            finally
            {
                ClearSidebarDragVisuals();
            }
        }

        private int GetPinnedInsertIndex(TreeNode target, Point clientPoint)
        {
            var pinned = AppSettings.Current.PinnedPaths;
            if (pinned.Count == 0) return 0;
            if (!IsPinnedNode(target) || target.Tag is not string p) return pinned.Count;

            int idx = pinned.IndexOf(p);
            if (idx < 0) return pinned.Count;

            int midY = target.Bounds.Top + (target.Bounds.Height / 2);
            return clientPoint.Y >= midY ? idx + 1 : idx;
        }

        private void ClearSidebarDragVisuals()
        {
            _dragPath = null;
            _dragInsertIndex = -1;
            _dragBlockId = null;
            _dragBlockInsertIndex = -1;
            if (_tree != null) _tree.Invalidate();
        }

        private void SelectSidebarNodeByPath(string path)
        {
            if (_tree == null) return;
            foreach (TreeNode node in _tree.Nodes)
            {
                if (node.Tag is string nodePath && string.Equals(nodePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    _tree.SelectedNode = node;
                    break;
                }
            }
        }
    }
}
