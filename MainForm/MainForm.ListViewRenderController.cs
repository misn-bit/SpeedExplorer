using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class ListViewRenderController
    {
        private readonly MainForm _owner;
        private readonly GdiCache _gdi = new();
        private BrowserState State => _owner.State;

        // Shared, immutable text layout specs (no color/font dependency, live for app lifetime).
        private static readonly StringFormat SfNear = CreateSf(StringAlignment.Near);
        private static readonly StringFormat SfCenter = CreateSf(StringAlignment.Center);
        private static readonly StringFormat SfFar = CreateSf(StringAlignment.Far);

        private static StringFormat CreateSf(StringAlignment alignment) => new()
        {
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
            Alignment = alignment
        };

        private static StringFormat GetSf(HorizontalAlignment align) => align switch
        {
            HorizontalAlignment.Right => SfFar,
            HorizontalAlignment.Center => SfCenter,
            _ => SfNear
        };

        public ListViewRenderController(MainForm owner)
        {
            _owner = owner;
        }

        /// <summary>
        /// Disposes all cached GDI+ objects. Call after theme changes so stale
        /// colors/fonts can't linger; the cache repopulates lazily on next paint.
        /// </summary>
        public void InvalidateThemeCaches() => _gdi.Reset();

        /// <summary>
        /// Caches GDI+ brushes/pens/fonts so owner-draw handlers don't allocate
        /// them per cell per frame. GDI+ object creation is one of the main
        /// per-frame costs when scrolling.
        /// </summary>
        private sealed class GdiCache : IDisposable
        {
            private readonly Dictionary<int, SolidBrush> _brushes = new();
            private readonly Dictionary<int, Pen> _pens = new();
            private Font? _tagFont;
            private string? _tagFontKey;
            private Font? _italicFont;
            private string? _italicFontKey;

            public SolidBrush Brush(Color color)
            {
                int key = color.ToArgb();
                if (!_brushes.TryGetValue(key, out var brush))
                {
                    brush = new SolidBrush(color);
                    _brushes[key] = brush;
                }
                return brush;
            }

            public Pen Pen(Color color)
            {
                int key = color.ToArgb();
                if (!_pens.TryGetValue(key, out var pen))
                {
                    pen = new Pen(color);
                    _pens[key] = pen;
                }
                return pen;
            }

            public Font TagFont(Font baseFont)
            {
                string key = baseFont.FontFamily.Name;
                if (_tagFont == null || _tagFontKey != key)
                {
                    _tagFont?.Dispose();
                    _tagFont = new Font(baseFont.FontFamily, 8f);
                    _tagFontKey = key;
                }
                return _tagFont;
            }

            public Font ItalicFont(Font baseFont)
            {
                string key = $"{baseFont.FontFamily.Name}|{baseFont.Size}|{baseFont.Unit}";
                if (_italicFont == null || _italicFontKey != key)
                {
                    _italicFont?.Dispose();
                    _italicFont = new Font(baseFont, FontStyle.Italic);
                    _italicFontKey = key;
                }
                return _italicFont;
            }

            public void Reset()
            {
                foreach (var b in _brushes.Values) b.Dispose();
                _brushes.Clear();
                foreach (var p in _pens.Values) p.Dispose();
                _pens.Clear();
                _tagFont?.Dispose(); _tagFont = null; _tagFontKey = null;
                _italicFont?.Dispose(); _italicFont = null; _italicFontKey = null;
            }

            public void Dispose() => Reset();
        }

        public void DrawTags(Graphics g, Rectangle bounds, string? tagText, Color rowBackColor, bool isSelected)
        {
            if (string.IsNullOrEmpty(tagText))
                return;

            var tags = tagText.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
            var x = (float)bounds.X + 4;

            // Use a lighter gray for pills if the row is already highlighted with the standard gray.
            Color pillColor = _owner.TagColor;
            if (rowBackColor.ToArgb() == _owner.SelectionUnfocusedColor.ToArgb())
            {
                pillColor = _owner.TagSelectedColor;
            }

            var bgBrush = _gdi.Brush(pillColor);
            var textBrush = _gdi.Brush(_owner.TagForeColor);
            var selectedTextBrush = _gdi.Brush(Color.White);
            var font = _gdi.TagFont(_owner._listView.Font);

            // Save graphics state and set clip to column bounds.
            var state = g.Save();
            g.SetClip(bounds);

            foreach (var tag in tags)
            {
                var size = g.MeasureString(tag, font);
                var rect = new RectangleF(
                    x,
                    bounds.Y + (bounds.Height - size.Height) / 2.0f - 1,
                    size.Width + _owner.Scale(6),
                    size.Height + _owner.Scale(2));

                // If the start of the tag is already outside, we can stop.
                if (x >= bounds.Right)
                    break;

                g.FillRectangle(bgBrush, rect);
                g.DrawString(tag, font, isSelected ? selectedTextBrush : textBrush, rect.X + 3, rect.Y + 1);
                x += rect.Width + 4;
            }

            g.Restore(state);
        }

        public void DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
        {
            var brush = _gdi.Brush(_owner.HeaderBackColor);
            e.Graphics.FillRectangle(brush, e.Bounds);

            // Draw separator on the right (header only).
            var pen = _gdi.Pen(_owner.BorderStrongColor);
            e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);

            var text = e.Header?.Text ?? "";
            var colIndex = e.ColumnIndex;
            var sortIndicator = "";

            bool isDriveView = State.CurrentPath == ThisPcPath && !_owner.IsSearchMode;
            bool isMatch;

            if (isDriveView)
            {
                isMatch = (colIndex == 0 && State.SortColumn == SortColumn.DriveNumber) ||
                          (colIndex == 1 && State.SortColumn == SortColumn.Name) ||
                          (colIndex == 2 && State.SortColumn == SortColumn.Type) ||
                          (colIndex == 3 && State.SortColumn == SortColumn.Format) ||
                          (colIndex == 4 && State.SortColumn == SortColumn.Size) ||
                          (colIndex == 5 && State.SortColumn == SortColumn.Size) ||
                          (colIndex == 6 && State.SortColumn == SortColumn.FreeSpace);
            }
            else
            {
                isMatch = (colIndex == 0 && State.SortColumn == SortColumn.Name) ||
                          (colIndex == 1 && State.SortColumn == SortColumn.Location) ||
                          (colIndex == 2 && State.SortColumn == SortColumn.Size) ||
                          (colIndex == 3 && State.SortColumn == SortColumn.DateModified) ||
                          (colIndex == 4 && State.SortColumn == SortColumn.DateCreated) ||
                          (colIndex == 5 && State.SortColumn == SortColumn.Type) ||
                          (colIndex == 6 && State.SortColumn == SortColumn.Tags);
            }

            if (isMatch)
            {
                sortIndicator = State.SortDirection == SortDirection.Ascending ? " ▲" : " ▼";
            }

            var align = e.Header?.TextAlign ?? HorizontalAlignment.Left;
            var sf = GetSf(align);

            var textBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            var textBrush = _gdi.Brush(_owner.ForeColor_Dark);
            var headerFont = e.Font ?? _owner._listView.Font;
            e.Graphics.DrawString(text + sortIndicator, headerFont, textBrush, textBounds, sf);
        }

        public void DrawItem(object? sender, DrawListViewItemEventArgs e)
        {
            // Rendering is handled in DrawSubItem.
            _ = sender;
            _ = e;
        }

        public void DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
        {
            _ = sender;
            Color rowBackColor = _owner.ListBackColor;
            bool isDriveView = State.CurrentPath == ThisPcPath && !_owner.IsSearchMode;

            var drawItem = e.Item;
            if (drawItem == null)
                return;

            bool isProgressRow = ReferenceEquals(drawItem.Tag, SearchProgressRowTag);

            if (isProgressRow)
            {
                rowBackColor = _owner.ListBackColor;
            }
            else if (drawItem.Selected)
            {
                rowBackColor = _owner._listView.Focused
                    ? _owner.SelectionFocusedColor
                    : _owner.SelectionUnfocusedColor;
            }
            else if (e.ItemIndex == _owner._dragDropController.HoverIndex)
            {
                rowBackColor = _owner.DropTargetBackColor;
            }
            else if (e.ItemIndex == _owner._hoveredIndex)
            {
                rowBackColor = _owner.HoverBackColor;
            }

            var fillRect = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width + 1, e.Bounds.Height);

            // Name column logic (+ icon). In drive view, name is column 1 (column 0 is "№").
            bool isNameColumn = (!isDriveView && e.ColumnIndex == 0) || (isDriveView && e.ColumnIndex == 1);
            if (isNameColumn)
            {
                var brush = _gdi.Brush(rowBackColor);
                e.Graphics.FillRectangle(brush, fillRect);

                var s = AppSettings.Current;
                var gap = 4;
                var x = e.Bounds.X + gap;
                var iconWidth = 0;

                if (s.ShowIcons && !s.UseEmojiIcons && drawItem.ImageKey != "_emoji_")
                {
                    var iconSize = _owner._listView.SmallImageList?.ImageSize.Width ?? 16;
                    var y = e.Bounds.Y + (e.Bounds.Height - iconSize) / 2;
                    iconWidth = iconSize + 4;

                    if (!string.IsNullOrEmpty(drawItem.ImageKey) && _owner._smallIcons.Images.ContainsKey(drawItem.ImageKey))
                    {
                        try
                        {
                            var image = _owner._smallIcons.Images[drawItem.ImageKey];
                            if (image != null)
                            {
                                bool isCut = drawItem.Tag is FileItem fi && State.CutPaths.Contains(fi.FullPath);
                                if (isCut)
                                {
                                    var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.5f };
                                    using var attr = new System.Drawing.Imaging.ImageAttributes();
                                    attr.SetColorMatrix(cm);
                                    e.Graphics.DrawImage(
                                        image,
                                        new Rectangle(x, y, iconSize, iconSize),
                                        0,
                                        0,
                                        image.Width,
                                        image.Height,
                                        GraphicsUnit.Pixel,
                                        attr);
                                }
                                else
                                {
                                    e.Graphics.DrawImage(image, x, y, iconSize, iconSize);
                                }
                            }
                        }
                        catch
                        {
                            // Ignore icon draw errors.
                        }
                    }
                }

                var textBrush = _gdi.Brush(
                    isProgressRow
                        ? _owner.MutedForeColor
                        : drawItem.Tag is FileItem fs && State.CutPaths.Contains(fs.FullPath)
                        ? Color.FromArgb(120, _owner.ForeColor_Dark)
                        : _owner.ForeColor_Dark);

                var textX = x + iconWidth;
                var textRect = new Rectangle(textX, e.Bounds.Y, e.Bounds.Width - (textX - e.Bounds.X), e.Bounds.Height);
                string displayText = isDriveView ? (e.SubItem?.Text ?? "") : drawItem.Text;

                var sf = SfNear;
                var drawFont = _owner._listView.Font;
                if (isProgressRow)
                    drawFont = _gdi.ItalicFont(_owner._listView.Font);
                e.Graphics.DrawString(displayText, drawFont, textBrush, textRect, sf);
            }
            else
            {
                var b = _gdi.Brush(rowBackColor);
                e.Graphics.FillRectangle(b, fillRect);

                if (State.CurrentPath == ThisPcPath &&
                    e.ColumnIndex == ColumnIndex_DriveCapacity &&
                    drawItem.Tag is FileItem fi &&
                    (fi.Extension == ".drive" || fi.Extension == ".usb"))
                {
                    var barRect = new Rectangle(e.Bounds.X + 5, e.Bounds.Y + 4, e.Bounds.Width - 10, e.Bounds.Height - 8);
                    var barBgBrush = _gdi.Brush(_owner.HoverBackColor);
                    e.Graphics.FillRectangle(barBgBrush, barRect);

                    if (fi.Size > 0)
                    {
                        double used = (double)(fi.Size - fi.FreeSpace);
                        double total = fi.Size;
                        double ratio = used / total;
                        if (ratio > 1.0)
                            ratio = 1.0;

                        int fillWidth = (int)(barRect.Width * ratio);
                        if (fillWidth < 1 && ratio > 0)
                            fillWidth = 1;

                        var usageFillRect = new Rectangle(barRect.X, barRect.Y, fillWidth, barRect.Height);

                        Color barColor = Color.LimeGreen;
                        if (ratio > 0.90)
                            barColor = Color.Red;
                        else if (ratio > 0.75)
                            barColor = Color.Yellow;

                        var fillBrush = _gdi.Brush(barColor);
                        e.Graphics.FillRectangle(fillBrush, usageFillRect);
                    }
                    var barPen = _gdi.Pen(_owner.BorderSoftColor);
                    e.Graphics.DrawRectangle(barPen, barRect);
                }
                else if (State.CurrentPath != ThisPcPath && e.ColumnIndex == ColumnIndex_Tags)
                {
                    DrawTags(e.Graphics, e.Bounds, e.SubItem?.Text, rowBackColor, drawItem.Selected);
                }
                else
                {
                    var text = e.SubItem?.Text ?? "";
                    var align = e.Header?.TextAlign ?? HorizontalAlignment.Left;
                    var sf = GetSf(align);

                    var textBrush = _gdi.Brush(_owner.ForeColor_Dark);
                    var textBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                    e.Graphics.DrawString(text, _owner._listView.Font, textBrush, textBounds, sf);
                }
            }
        }
    }
}
