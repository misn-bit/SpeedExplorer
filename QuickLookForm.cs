using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace SpeedExplorer;

public class QuickLookForm : Form
{
    private PictureBox _pictureBox;
    private RichTextBox _richTextBox;
    private Label _infoLabel;

    public QuickLookForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.BackColor = Color.FromArgb(30, 30, 30);
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.TopMost = true;
        this.Padding = new Padding(10);

        _pictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            Visible = false
        };

        _richTextBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(40, 40, 40),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.None,
            ReadOnly = true, DetectUrls = false, TabStop = false, Cursor = Cursors.Default,
            Font = new Font("Consolas", 12),
            Visible = false
        };

        _infoLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 10),
            TextAlign = ContentAlignment.MiddleCenter
        };

        Controls.Add(_pictureBox);
        Controls.Add(_richTextBox);
        Controls.Add(_infoLabel);
    }

    protected override bool ShowWithoutActivation => true;

    public void ShowPreview(FileItem item)
    {
        _pictureBox.Visible = false;
        _richTextBox.Visible = false;
        _infoLabel.Text = item.Name + " (" + FileItem.FormatSize(item.Size) + ")";

        string ext = item.Extension.ToLowerInvariant();
        bool isImage = FileSystemService.IsImageFile(item.FullPath);
        bool isText = ext == ".txt" || ext == ".cs" || ext == ".py" || ext == ".js" || ext == ".ts" || ext == ".json" || ext == ".xml" || ext == ".md" || ext == ".yaml" || ext == ".yml" || ext == ".log";

        if (isImage)
        {
            try
            {
                // Load without locking the file
                _pictureBox.Image?.Dispose();
                using (var stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (var img = Image.FromStream(stream))
                    {
                        _pictureBox.Image = new Bitmap(img);
                    }
                }
                _pictureBox.Visible = true;
                
                int maxW = 1280;
                int maxH = 720;
                float ratio = (float)_pictureBox.Image.Width / _pictureBox.Image.Height;
                int w, h;
                if (ratio > 1) { w = maxW; h = (int)(maxW / ratio); }
                else { h = maxH; w = (int)(maxH * ratio); }
                this.Size = new Size(w + 20, h + 50);
            }
            catch { this.Hide(); return; }
        }
        else if (isText)
        {
            try
            {
                var lines = File.ReadLines(item.FullPath).Take(100);
                _richTextBox.Text = string.Join(Environment.NewLine, lines);
                _richTextBox.Visible = true;
                this.Size = new Size(1600, 900);
            }
            catch { this.Hide(); return; }
        }
        else { this.Hide(); return; }

        if (Owner != null)
        {
            this.Left = Owner.Left + (Owner.Width - this.Width) / 2;
            this.Top = Owner.Top + (Owner.Height - this.Height) / 2;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
            return;
        }
        _pictureBox.Image?.Dispose();
        base.OnFormClosing(e);
    }
}
