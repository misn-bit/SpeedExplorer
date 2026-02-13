using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class ListViewController
    {
        private readonly MainForm _owner;

        public ListViewController(MainForm owner)
        {
            _owner = owner;
        }

        public void SetupFileColumns(ListView lv)
        {
            lv.BeginUpdate();
            lv.Columns.Clear();
            AddColumn(lv, "col_name", ResolveFileColumnWidth("col_name", 350), HorizontalAlignment.Left);
            AddColumn(lv, "col_location", ResolveFileColumnWidth("col_location", 200), HorizontalAlignment.Left);
            AddColumn(lv, "col_size", ResolveFileColumnWidth("col_size", 80), HorizontalAlignment.Right);
            AddColumn(lv, "col_date_modified", ResolveFileColumnWidth("col_date_modified", 140), HorizontalAlignment.Left);
            AddColumn(lv, "col_date_created", ResolveFileColumnWidth("col_date_created", 140), HorizontalAlignment.Left);
            AddColumn(lv, "col_type", ResolveFileColumnWidth("col_type", 80), HorizontalAlignment.Left);
            AddColumn(lv, "col_tags", ResolveFileColumnWidth("col_tags", 150), HorizontalAlignment.Left);
            lv.EndUpdate();
            if (lv == _owner._listView) EnsureHeaderTail();
        }

        public void SetupDriveColumns(ListView lv)
        {
            lv.BeginUpdate();
            lv.Columns.Clear();
            AddColumn(lv, "col_number", ResolveDriveColumnWidth("col_number", 48), HorizontalAlignment.Right);
            AddColumn(lv, "col_name", ResolveDriveColumnWidth("col_name", 250), HorizontalAlignment.Left);
            AddColumn(lv, "col_type", ResolveDriveColumnWidth("col_type", 100), HorizontalAlignment.Left);
            AddColumn(lv, "col_format", ResolveDriveColumnWidth("col_format", 80), HorizontalAlignment.Left);
            AddColumn(lv, "col_size", ResolveDriveColumnWidth("col_size", 100), HorizontalAlignment.Right);
            AddColumn(lv, "col_capacity", ResolveDriveColumnWidth("col_capacity", 200), HorizontalAlignment.Left);
            AddColumn(lv, "col_free_space", ResolveDriveColumnWidth("col_free_space", 120), HorizontalAlignment.Right);
            lv.EndUpdate();
            if (lv == _owner._listView) EnsureHeaderTail();
        }

        public void RescaleListViewColumns()
        {
            if (_owner._listView == null) return;

            _owner._suppressColumnMetaUpdate = true;
            _owner._listView.BeginUpdate();
            try
            {
                foreach (ColumnHeader col in _owner._listView.Columns)
                {
                    if (col.Tag is ColumnMeta meta && meta.BaseWidth > 0)
                    {
                        col.Text = Localization.T(meta.Key);
                        col.Width = Math.Max(50, _owner.Scale(meta.BaseWidth));
                    }
                }
            }
            finally
            {
                _owner._listView.EndUpdate();
                _owner._suppressColumnMetaUpdate = false;
            }
        }

        public ListView CreateListView()
        {
            var lv = new PolishedListView
            {
                View = View.Details,
                VirtualMode = true,
                VirtualListSize = 0,
                BackColor = ListBackColor,
                ForeColor = ForeColor_Dark,
                BorderStyle = BorderStyle.None,
                FullRowSelect = true,
                Font = new Font("Segoe UI", 10),
                SmallImageList = _owner._smallIcons,
                LargeImageList = _owner._largeIcons,
                OwnerDraw = true
            };

            SetupFileColumns(lv);
            _owner._dragDropController.AttachToListView(lv);

            lv.RetrieveVirtualItem += _owner.ListView_RetrieveVirtualItem;
            lv.ColumnClick += _owner.ListView_ColumnClick;
            lv.MouseDoubleClick += _owner.ListView_MouseDoubleClick;
            lv.MouseDown += _owner.ListView_MouseDown;
            lv.MouseMove += _owner.ListView_MouseMove;
            lv.MouseLeave += (s, e) =>
            {
                if (_owner._hoveredIndex != -1)
                {
                    int oldHover = _owner._hoveredIndex;
                    _owner._hoveredIndex = -1;
                    _owner.InvalidateListItem(oldHover);
                }
            };
            lv.MouseWheel += _owner._iconZoomController.HandleMouseWheel;
            lv.MouseWheel += _owner._listViewInteractionController.MouseWheel;
            lv.KeyDown += _owner.ListView_KeyDown;
            lv.KeyUp += _owner.ListView_KeyUp;
            lv.LostFocus += (s, e) => _owner.HideQuickLook();
            lv.DrawColumnHeader += _owner.ListView_DrawColumnHeader;
            lv.DrawItem += _owner.ListView_DrawItem;
            lv.DrawSubItem += _owner.ListView_DrawSubItem;
            lv.ContextMenuStrip = _owner._contextMenu;

            lv.ColumnWidthChanging += (s, e) =>
            {
                if (e.NewWidth < 50)
                {
                    e.NewWidth = 50;
                    e.Cancel = true;
                    lv.Columns[e.ColumnIndex].Width = 50;
                }
            };

            lv.ColumnWidthChanged += (s, e) =>
            {
                if (_owner._suppressColumnMetaUpdate) return;
                try
                {
                    var col = lv.Columns[e.ColumnIndex];
                    if (col.Tag is ColumnMeta meta && meta.BaseWidth > 0)
                    {
                        int baseWidth = _owner.Unscale(col.Width);
                        meta.BaseWidth = baseWidth;

                        bool isDriveView = _owner._currentPath == ThisPcPath && !_owner.IsSearchMode;
                        var target = isDriveView ? AppSettings.Current.DriveColumnWidths : AppSettings.Current.FileColumnWidths;
                        target[meta.Key] = baseWidth;
                        AppSettings.Current.Save();
                    }
                }
                catch { }
            };

            lv.HandleCreated += (s, e) =>
            {
                SetWindowTheme(lv.Handle, "DarkMode_Explorer", null);

                void ApplyListViewExStyles()
                {
                    const int LVM_SETEXTENDEDLISTVIEWSTYLE = 0x1036;
                    const int LVM_GETEXTENDEDLISTVIEWSTYLE = 0x1037;
                    const int LVS_EX_GRIDLINES = 0x1;
                    const int LVS_EX_MARQUEESELECTION = 0x8;
                    const int LVS_EX_DOUBLEBUFFER = 0x10000;

                    try { lv.GridLines = false; } catch { }

                    int ex = SendMessage(lv.Handle, LVM_GETEXTENDEDLISTVIEWSTYLE, 0, 0);
                    ex &= ~LVS_EX_GRIDLINES;
                    ex |= LVS_EX_MARQUEESELECTION;
                    ex |= LVS_EX_DOUBLEBUFFER;
                    SendMessage(lv.Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, 0, ex);
                }

                ApplyListViewExStyles();
                _owner.BeginInvoke((Action)(() =>
                {
                    if (!lv.IsHandleCreated) return;
                    ApplyListViewExStyles();
                }));

                int darkMode = 1;
                DwmSetWindowAttribute(lv.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                var headerHandle = SendMessagePtr(lv.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (headerHandle != IntPtr.Zero)
                {
                    SetWindowTheme(headerHandle, "DarkMode_Explorer", null);
                    _owner._headerHandle = headerHandle;
                    _owner._headerTailController.Attach(headerHandle);
                    _owner._headerTailController.Invalidate();
                }
            };

            return lv;
        }

        public void StretchTagsColumn()
        {
            // Kept for compatibility with existing call sites. Auto-stretch behavior removed.
        }

        public void EnsureHeaderTail()
        {
            if (_owner._listView == null || !_owner._listView.IsHandleCreated) return;

            var headerHandle = SendMessagePtr(_owner._listView.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            if (headerHandle == IntPtr.Zero) return;

            _owner._headerHandle = headerHandle;
            try { SetWindowTheme(headerHandle, "DarkMode_Explorer", null); } catch { }
            _owner._headerTailController.Attach(headerHandle);
            _owner._headerTailController.Invalidate();
        }

        private void AddColumn(ListView lv, string key, int baseWidth, HorizontalAlignment align)
        {
            var col = new ColumnHeader
            {
                Text = Localization.T(key),
                Width = _owner.Scale(baseWidth),
                TextAlign = align,
                Tag = new ColumnMeta { Key = key, BaseWidth = baseWidth }
            };
            lv.Columns.Add(col);
        }

        private static int ResolveColumnWidth(System.Collections.Generic.Dictionary<string, int>? map, string key, int fallback)
        {
            if (map != null && map.TryGetValue(key, out int value))
                return Math.Max(50, value);
            return Math.Max(50, fallback);
        }

        private static int ResolveFileColumnWidth(string key, int fallback)
            => ResolveColumnWidth(AppSettings.Current.FileColumnWidths, key, fallback);

        private static int ResolveDriveColumnWidth(string key, int fallback)
            => ResolveColumnWidth(AppSettings.Current.DriveColumnWidths, key, fallback);
    }
}
