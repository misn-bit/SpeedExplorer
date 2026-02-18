using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class DragDropController : IDisposable
    {
        private readonly MainForm _owner;
        private ListView? _listView;

        private int _dragHoverIndex = -1;
        private DragGhostForm? _dragGhostForm;
        private Bitmap? _dragGhostBitmap;
        private Point _dragGhostOffset = new Point(12, 12);

        public int HoverIndex => _dragHoverIndex;

        public DragDropController(MainForm owner)
        {
            _owner = owner;
        }

        public void AttachToListView(ListView lv)
        {
            _listView = lv;

            lv.AllowDrop = true;
            lv.ItemDrag += ListView_ItemDrag;
            lv.DragEnter += ListView_DragEnter;
            lv.DragOver += ListView_DragOver;
            lv.DragDrop += ListView_DragDrop;
            lv.DragLeave += (s, e) => ClearListViewDragHover();
            lv.GiveFeedback += ListView_GiveFeedback;
            lv.QueryContinueDrag += ListView_QueryContinueDrag;
        }

        private void ListView_ItemDrag(object? sender, ItemDragEventArgs e)
        {
            if (_listView == null) return;
            if (IsShellPath(_owner._currentPath)) return;

            var paths = _owner.GetSelectedPaths();
            if (paths.Any(IsShellPath)) return;
            if (paths.Length <= 0) return;

            // Initiate drag
            if (e.Item is ListViewItem lvi)
            {
                BeginListViewDragImage(lvi);
            }

            try
            {
                _listView.DoDragDrop(new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy | DragDropEffects.Move);
            }
            finally
            {
                EndListViewDragImage();
                ClearListViewDragHover();
            }
        }

        private void ListView_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = GetDragDropEffect(e, _owner._currentPath);
        }

        private void ListView_DragOver(object? sender, DragEventArgs e)
        {
            if (_listView == null) return;

            string dest = GetDropTarget(e.X, e.Y);
            e.Effect = GetDragDropEffect(e, dest);
            UpdateListViewDragHover(e.X, e.Y);
        }

        private void ListView_DragDrop(object? sender, DragEventArgs e)
        {
            if (_listView == null) return;
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (IsShellPath(_owner._currentPath)) return;

            var paths = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0) return;

            // Determine destination
            string destination = GetDropTarget(e.X, e.Y);

            if (string.IsNullOrEmpty(destination) || destination == ThisPcPath)
            {
                MessageBox.Show("Cannot drop files here.", "Drop Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Determine operation type (Copy vs Move)
            DragDropEffects effect = GetDragDropEffect(e, destination);
            bool isCopy = effect == DragDropEffects.Copy;
            string normalizedDestination = Path.TrimEndingDirectorySeparator(destination);

            // Prevent moving/copying into itself
            foreach (var path in paths)
            {
                string normalizedPath = Path.TrimEndingDirectorySeparator(path);
                string normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path) ?? "");

                // Dragging folder and dropping onto the same folder should do nothing.
                if (string.Equals(normalizedPath, normalizedDestination, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (string.Equals(normalizedParent, normalizedDestination, StringComparison.OrdinalIgnoreCase))
                {
                    // Source dir = Dest dir. Nothing to do.
                    return;
                }
            }

            try
            {
                if (isCopy)
                {
                    FileSystemService.ShellCopy(paths, destination, _owner.Handle, renameOnCollision: true);
                }
                else
                {
                    FileSystemService.ShellMove(paths, destination, _owner.Handle, renameOnCollision: true);
                }

                _ = _owner.RefreshCurrentAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Drop operation failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ClearListViewDragHover();
            }
        }

        private string GetDropTarget(int x, int y)
        {
            if (_listView == null) return _owner._currentPath;

            var point = _listView.PointToClient(new Point(x, y));
            var hitItem = _listView.GetItemAt(point.X, point.Y);
            if (hitItem != null && hitItem.Tag is FileItem fi && fi.IsDirectory)
                return fi.FullPath;
            return _owner._currentPath;
        }

        private static DragDropEffects GetDragDropEffect(DragEventArgs e, string destination)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return DragDropEffects.None;

            if (IsShellPath(destination)) return DragDropEffects.None;

            // Force Move if Shift is held (4)
            if ((e.KeyState & 4) == 4) return DragDropEffects.Move;

            // Force Copy if Ctrl is held (8)
            if ((e.KeyState & 8) == 8) return DragDropEffects.Copy;

            // Default Behavior: Same drive = Move, Different Drive = Copy
            if (!string.IsNullOrEmpty(destination) && destination != ThisPcPath)
            {
                var paths = (string[]?)e.Data.GetData(DataFormats.FileDrop);
                if (paths != null && paths.Length > 0)
                {
                    try
                    {
                        string srcRoot = Path.GetPathRoot(paths[0]) ?? "";
                        string dstRoot = Path.GetPathRoot(destination) ?? "";
                        if (!string.Equals(srcRoot, dstRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            return DragDropEffects.Copy;
                        }
                    }
                    catch { }
                }
            }

            return DragDropEffects.Move;
        }

        private void UpdateListViewDragHover(int screenX, int screenY)
        {
            if (_listView == null) return;

            if (_dragGhostForm != null && !_dragGhostForm.IsDisposed)
            {
                _dragGhostForm.Location = new Point(screenX + _dragGhostOffset.X, screenY + _dragGhostOffset.Y);
            }

            var point = _listView.PointToClient(new Point(screenX, screenY));
            var hit = _listView.GetItemAt(point.X, point.Y);
            int newHover = -1;
            if (hit != null && hit.Tag is FileItem fi && fi.IsDirectory)
                newHover = hit.Index;

            if (newHover != _dragHoverIndex)
            {
                int old = _dragHoverIndex;
                _dragHoverIndex = newHover;
                if (old != -1) _owner.InvalidateListItem(old);
                if (_dragHoverIndex != -1) _owner.InvalidateListItem(_dragHoverIndex);
            }
        }

        private void ClearListViewDragHover()
        {
            if (_dragHoverIndex != -1)
            {
                int old = _dragHoverIndex;
                _dragHoverIndex = -1;
                _owner.InvalidateListItem(old);
            }
        }

        private void BeginListViewDragImage(ListViewItem item)
        {
            if (_listView == null) return;

            EndListViewDragImage();

            Rectangle rect;
            try { rect = _listView.GetItemRect(item.Index); }
            catch { rect = new Rectangle(0, 0, _owner.Scale(200), _owner.Scale(24)); }

            int width = Math.Min(Math.Max(rect.Width, _owner.Scale(160)), _owner.Scale(256));
            int height = Math.Max(rect.Height, _owner.Scale(22));

            using var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                var bg = Color.FromArgb(180, 45, 45, 45);
                using var brush = new SolidBrush(bg);
                g.FillRectangle(brush, new Rectangle(0, 0, width, height));

                int x = _owner.Scale(6);
                int iconSize = _listView.SmallImageList?.ImageSize.Width ?? 16;
                if (!string.IsNullOrEmpty(item.ImageKey) && _owner._smallIcons.Images.ContainsKey(item.ImageKey))
                {
                    var img = _owner._smallIcons.Images[item.ImageKey];
                    if (img != null)
                    {
                        g.DrawImage(img, new Rectangle(x, (height - iconSize) / 2, iconSize, iconSize));
                        x += iconSize + _owner.Scale(6);
                    }
                }

                var textRect = new Rectangle(x, 0, width - x - _owner.Scale(6), height);
                TextRenderer.DrawText(
                    g,
                    item.Text,
                    _listView.Font,
                    textRect,
                    ForeColor_Dark,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            _dragGhostBitmap?.Dispose();
            _dragGhostBitmap = (Bitmap)bmp.Clone();
            if (_dragGhostForm == null || _dragGhostForm.IsDisposed)
                _dragGhostForm = new DragGhostForm();
            _dragGhostForm.SetImage(_dragGhostBitmap);
            var screenPoint = Cursor.Position;
            _dragGhostForm.Location = new Point(screenPoint.X + _dragGhostOffset.X, screenPoint.Y + _dragGhostOffset.Y);
            _dragGhostForm.Show();
        }

        private void EndListViewDragImage()
        {
            if (_dragGhostForm != null && !_dragGhostForm.IsDisposed)
            {
                _dragGhostForm.Hide();
                _dragGhostForm.Dispose();
            }

            _dragGhostForm = null;
            if (_dragGhostBitmap != null)
            {
                _dragGhostBitmap.Dispose();
                _dragGhostBitmap = null;
            }
        }

        private void ListView_GiveFeedback(object? sender, GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = true;
        }

        private void ListView_QueryContinueDrag(object? sender, QueryContinueDragEventArgs e)
        {
            if (e.Action == DragAction.Cancel || e.Action == DragAction.Drop)
            {
                EndListViewDragImage();
            }
        }

        public void Dispose()
        {
            try { EndListViewDragImage(); } catch { }
            try { ClearListViewDragHover(); } catch { }
        }

        private sealed class DragGhostForm : Form
        {
            private Image? _image;

            public DragGhostForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.FromArgb(45, 45, 45);
                Opacity = 0.9;
                Enabled = false;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    const int WS_EX_TOOLWINDOW = 0x00000080;
                    const int WS_EX_NOACTIVATE = 0x08000000;
                    const int WS_EX_TOPMOST = 0x00000008;
                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                    return cp;
                }
            }

            public void SetImage(Image image)
            {
                _image = image;
                Size = image.Size;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (_image == null) return;
                e.Graphics.DrawImage(_image, 0, 0, _image.Width, _image.Height);
            }
        }
    }
}
