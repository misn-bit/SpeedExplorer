using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class ListViewRenderController
    {
        private readonly MainForm _owner;

        public ListViewRenderController(MainForm owner)
        {
            _owner = owner;
        }

        public void DrawTags(Graphics g, Rectangle bounds, string? tagText, Color rowBackColor, bool isSelected)
        {
            if (string.IsNullOrEmpty(tagText))
                return;

            var tags = tagText.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
            var x = (float)bounds.X + 4;

            // Use a lighter gray for pills if the row is already highlighted with the standard gray.
            Color pillColor = TagColor;
            if (rowBackColor.R == 60 && rowBackColor.G == 60 && rowBackColor.B == 60)
            {
                pillColor = Color.FromArgb(90, 90, 90);
            }

            using var bgBrush = new SolidBrush(pillColor);
            using var textBrush = new SolidBrush(TagForeColor);
            using var selectedTextBrush = new SolidBrush(Color.White);
            using var font = new Font(_owner._listView.Font.FontFamily, 8f);

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
            using var brush = new SolidBrush(Color.FromArgb(45, 45, 45));
            e.Graphics.FillRectangle(brush, e.Bounds);

            // Draw separator on the right (header only).
            using var pen = new Pen(Color.FromArgb(80, 80, 80));
            e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);

            var text = e.Header?.Text ?? "";
            var colIndex = e.ColumnIndex;
            var sortIndicator = "";

            bool isDriveView = _owner._currentPath == ThisPcPath && !_owner.IsSearchMode;
            bool isMatch;

            if (isDriveView)
            {
                isMatch = (colIndex == 0 && _owner._sortColumn == SortColumn.DriveNumber) ||
                          (colIndex == 1 && _owner._sortColumn == SortColumn.Name) ||
                          (colIndex == 2 && _owner._sortColumn == SortColumn.Type) ||
                          (colIndex == 3 && _owner._sortColumn == SortColumn.Format) ||
                          (colIndex == 4 && _owner._sortColumn == SortColumn.Size) ||
                          (colIndex == 5 && _owner._sortColumn == SortColumn.Size) ||
                          (colIndex == 6 && _owner._sortColumn == SortColumn.FreeSpace);
            }
            else
            {
                isMatch = (colIndex == 0 && _owner._sortColumn == SortColumn.Name) ||
                          (colIndex == 1 && _owner._sortColumn == SortColumn.Location) ||
                          (colIndex == 2 && _owner._sortColumn == SortColumn.Size) ||
                          (colIndex == 3 && _owner._sortColumn == SortColumn.DateModified) ||
                          (colIndex == 4 && _owner._sortColumn == SortColumn.DateCreated) ||
                          (colIndex == 5 && _owner._sortColumn == SortColumn.Type) ||
                          (colIndex == 6 && _owner._sortColumn == SortColumn.Tags);
            }

            if (isMatch)
            {
                sortIndicator = _owner._sortDirection == SortDirection.Ascending ? " ▲" : " ▼";
            }

            var align = e.Header?.TextAlign ?? HorizontalAlignment.Left;
            using var sf = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            switch (align)
            {
                case HorizontalAlignment.Right:
                    sf.Alignment = StringAlignment.Far;
                    break;
                case HorizontalAlignment.Center:
                    sf.Alignment = StringAlignment.Center;
                    break;
                default:
                    sf.Alignment = StringAlignment.Near;
                    break;
            }

            var textBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            using var textBrush = new SolidBrush(ForeColor_Dark);
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
            Color rowBackColor = ListBackColor;
            bool isDriveView = _owner._currentPath == ThisPcPath && !_owner.IsSearchMode;

            var drawItem = e.Item;
            if (drawItem == null)
                return;

            bool isProgressRow = ReferenceEquals(drawItem.Tag, SearchProgressRowTag);

            if (isProgressRow)
            {
                rowBackColor = ListBackColor;
            }
            else if (drawItem.Selected)
            {
                rowBackColor = _owner._listView.Focused
                    ? Color.FromArgb(0, 120, 212)
                    : Color.FromArgb(60, 60, 60);
            }
            else if (e.ItemIndex == _owner._dragDropController.HoverIndex)
            {
                rowBackColor = Color.FromArgb(0, 90, 160);
            }
            else if (e.ItemIndex == _owner._hoveredIndex)
            {
                rowBackColor = Color.FromArgb(60, 60, 60);
            }

            var fillRect = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width + 1, e.Bounds.Height);

            // Name column logic (+ icon). In drive view, name is column 1 (column 0 is "№").
            bool isNameColumn = (!isDriveView && e.ColumnIndex == 0) || (isDriveView && e.ColumnIndex == 1);
            if (isNameColumn)
            {
                using var brush = new SolidBrush(rowBackColor);
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
                                bool isCut = drawItem.Tag is FileItem fi && _owner._cutPaths.Contains(fi.FullPath);
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

                using var textBrush = new SolidBrush(
                    isProgressRow
                        ? Color.FromArgb(180, 180, 180)
                        : drawItem.Tag is FileItem fs && _owner._cutPaths.Contains(fs.FullPath)
                        ? Color.FromArgb(120, ForeColor_Dark)
                        : ForeColor_Dark);

                var textX = x + iconWidth;
                var textRect = new Rectangle(textX, e.Bounds.Y, e.Bounds.Width - (textX - e.Bounds.X), e.Bounds.Height);
                string displayText = isDriveView ? (e.SubItem?.Text ?? "") : drawItem.Text;

                using var sf = new StringFormat
                {
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };
                Font? tempFont = null;
                var drawFont = _owner._listView.Font;
                if (isProgressRow)
                {
                    tempFont = new Font(_owner._listView.Font, FontStyle.Italic);
                    drawFont = tempFont;
                }
                e.Graphics.DrawString(displayText, drawFont, textBrush, textRect, sf);
                tempFont?.Dispose();
            }
            else
            {
                using var b = new SolidBrush(rowBackColor);
                e.Graphics.FillRectangle(b, fillRect);

                if (_owner._currentPath == ThisPcPath &&
                    e.ColumnIndex == ColumnIndex_DriveCapacity &&
                    drawItem.Tag is FileItem fi &&
                    (fi.Extension == ".drive" || fi.Extension == ".usb"))
                {
                    var barRect = new Rectangle(e.Bounds.X + 5, e.Bounds.Y + 4, e.Bounds.Width - 10, e.Bounds.Height - 8);
                    using (var bgBrush = new SolidBrush(Color.FromArgb(60, 60, 60)))
                        e.Graphics.FillRectangle(bgBrush, barRect);

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

                        using var fillBrush = new SolidBrush(barColor);
                        e.Graphics.FillRectangle(fillBrush, usageFillRect);
                    }
                    using (var pen = new Pen(Color.FromArgb(100, 100, 100)))
                        e.Graphics.DrawRectangle(pen, barRect);
                }
                else if (_owner._currentPath != ThisPcPath && e.ColumnIndex == ColumnIndex_Tags)
                {
                    DrawTags(e.Graphics, e.Bounds, e.SubItem?.Text, rowBackColor, drawItem.Selected);
                }
                else
                {
                    var text = e.SubItem?.Text ?? "";
                    var align = e.Header?.TextAlign ?? HorizontalAlignment.Left;
                    using var sf = new StringFormat
                    {
                        LineAlignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter,
                        FormatFlags = StringFormatFlags.NoWrap
                    };
                    switch (align)
                    {
                        case HorizontalAlignment.Right:
                            sf.Alignment = StringAlignment.Far;
                            break;
                        case HorizontalAlignment.Center:
                            sf.Alignment = StringAlignment.Center;
                            break;
                        default:
                            sf.Alignment = StringAlignment.Near;
                            break;
                    }

                    using var textBrush = new SolidBrush(ForeColor_Dark);
                    var textBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                    e.Graphics.DrawString(text, _owner._listView.Font, textBrush, textBounds, sf);
                }
            }
        }
    }
}
