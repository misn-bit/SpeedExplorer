using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

public class QuickLookForm : Form
{
    private const int BasePadding = 10;
    private const int BaseInfoHeight = 30;
    private const int BaseImageMaxWidth = 1280;
    private const int BaseImageMaxHeight = 720;
    private const int BaseTextWidth = 1600;
    private const int BaseTextHeight = 900;
    private const int BaseMinWidth = 280;
    private const int BaseMinHeight = 200;
    private const double MaxWorkingAreaUsage = 0.90;

    private PictureBox _pictureBox;
    private RichTextBox _richTextBox;
    private Label _infoLabel;
    private AnimatedImageSequence? _imageSequence;
    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly System.Diagnostics.Stopwatch _animationClock = new();
    private int _animationFrameIndex;
    private long _animationFrameDeadlineMs;
    private CancellationTokenSource? _imageLoadCts;
    private int _imageLoadRequestId;

    private int EffectiveDpi => IsHandleCreated ? DeviceDpi : (Owner?.DeviceDpi ?? 96);
    private int Scale(int pixels) => (int)Math.Round(pixels * (EffectiveDpi / 96.0));

    public QuickLookForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        this.FormBorderStyle = FormBorderStyle.None;
        this.BackColor = Color.FromArgb(30, 30, 30);
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.TopMost = true;
        this.Padding = new Padding(BasePadding);

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

        _animationTimer = new System.Windows.Forms.Timer();
        _animationTimer.Tick += AnimationTimer_Tick;
    }

    protected override bool ShowWithoutActivation => true;

    public void ShowPreview(FileItem item)
    {
        ApplyDpiLayout();
        Rectangle workingArea = GetWorkingArea();
        int minFormWidth = Scale(BaseMinWidth);
        int minFormHeight = Scale(BaseMinHeight);
        int maxFormWidth = Math.Max(minFormWidth, (int)(workingArea.Width * MaxWorkingAreaUsage));
        int maxFormHeight = Math.Max(minFormHeight, (int)(workingArea.Height * MaxWorkingAreaUsage));
        int maxContentWidth = Math.Max(Scale(120), maxFormWidth - Padding.Horizontal);
        int maxContentHeight = Math.Max(Scale(120), maxFormHeight - Padding.Vertical - _infoLabel.Height);

        _pictureBox.Visible = false;
        _richTextBox.Visible = false;
        _infoLabel.Text = item.Name + " (" + FileItem.FormatSize(item.Size) + ")";

        bool isImage = FileSystemService.IsImageFile(item.FullPath);
        bool isText = FileSystemService.IsLikelyTextFile(item.FullPath);

        if (isImage)
        {
            int maxW = Math.Min(Scale(BaseImageMaxWidth), maxContentWidth);
            int maxH = Math.Min(Scale(BaseImageMaxHeight), maxContentHeight);

            ClearImagePreview();
            this.Size = new Size(minFormWidth, minFormHeight);
            Rectangle previewWorkingArea = workingArea;
            var loadCts = new CancellationTokenSource();
            _imageLoadCts = loadCts;
            int requestId = ++_imageLoadRequestId;
            _ = LoadImagePreviewAsync(item.FullPath, maxW, maxH, minFormWidth, minFormHeight,
                maxFormWidth, maxFormHeight, previewWorkingArea, requestId, loadCts);
        }
        else if (isText)
        {
            try
            {
                ClearImagePreview();
                var lines = File.ReadLines(item.FullPath).Take(100);
                _richTextBox.Text = string.Join(Environment.NewLine, lines);
                _richTextBox.Visible = true;

                int targetWidth = Math.Min(Scale(BaseTextWidth), maxFormWidth);
                int targetHeight = Math.Min(Scale(BaseTextHeight), maxFormHeight);
                this.Size = new Size(
                    Math.Max(minFormWidth, targetWidth),
                    Math.Max(minFormHeight, targetHeight));
            }
            catch { this.Hide(); return; }
        }
        else
        {
            ClearImagePreview();
            this.Hide();
            return;
        }

        CenterAndClampToWorkingArea(workingArea);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _animationTimer.Stop();
            this.Hide();
            return;
        }
        ClearImagePreview();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearImagePreview();
            _animationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            StartAnimationIfNeeded();
        }
        else
        {
            _animationTimer.Stop();
            CancelImageLoad();
        }
    }

    private void ApplyDpiLayout()
    {
        Padding = new Padding(Scale(BasePadding));
        _infoLabel.Height = Scale(BaseInfoHeight);
    }

    private Rectangle GetWorkingArea()
    {
        if (Owner != null && !Owner.IsDisposed)
            return Screen.FromControl(Owner).WorkingArea;

        if (IsHandleCreated)
            return Screen.FromControl(this).WorkingArea;

        return Screen.FromPoint(Cursor.Position).WorkingArea;
    }

    private void CenterAndClampToWorkingArea(Rectangle workingArea)
    {
        int targetLeft;
        int targetTop;

        if (Owner != null && !Owner.IsDisposed)
        {
            targetLeft = Owner.Left + (Owner.Width - Width) / 2;
            targetTop = Owner.Top + (Owner.Height - Height) / 2;
        }
        else
        {
            targetLeft = workingArea.Left + (workingArea.Width - Width) / 2;
            targetTop = workingArea.Top + (workingArea.Height - Height) / 2;
        }

        Left = Math.Max(workingArea.Left, Math.Min(targetLeft, workingArea.Right - Width));
        Top = Math.Max(workingArea.Top, Math.Min(targetTop, workingArea.Bottom - Height));
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_imageSequence == null || !_imageSequence.IsAnimated)
        {
            _animationTimer.Stop();
            return;
        }

        long elapsedMs = _animationClock.ElapsedMilliseconds;
        int advancedFrames = 0;
        while (elapsedMs >= _animationFrameDeadlineMs && advancedFrames < _imageSequence.FrameCount)
        {
            _animationFrameIndex = (_animationFrameIndex + 1) % _imageSequence.FrameCount;
            _animationFrameDeadlineMs += _imageSequence.GetFrameDelayMs(_animationFrameIndex);
            advancedFrames++;
        }

        if (advancedFrames == 0)
        {
            _animationTimer.Interval = Math.Max(1, (int)(_animationFrameDeadlineMs - elapsedMs));
            return;
        }

        _pictureBox.Image = _imageSequence.GetFrame(_animationFrameIndex);
        long remainingMs = Math.Max(1, _animationFrameDeadlineMs - _animationClock.ElapsedMilliseconds);
        _animationTimer.Interval = (int)Math.Min(int.MaxValue, remainingMs);
    }

    private void StartAnimationIfNeeded()
    {
        if (_imageSequence == null || !_imageSequence.IsAnimated || !Visible)
        {
            _animationTimer.Stop();
            return;
        }

        _animationClock.Restart();
        _animationFrameDeadlineMs = _imageSequence.GetFrameDelayMs(_animationFrameIndex);
        _animationTimer.Interval = _imageSequence.GetFrameDelayMs(_animationFrameIndex);
        _animationTimer.Start();
    }

    private async Task LoadImagePreviewAsync(
        string path,
        int maxWidth,
        int maxHeight,
        int minFormWidth,
        int minFormHeight,
        int maxFormWidth,
        int maxFormHeight,
        Rectangle workingArea,
        int requestId,
        CancellationTokenSource loadCts)
    {
        AnimatedImageSequence? sequence = null;
        try
        {
            sequence = await Task.Run(
                () => ImageSharpViewerService.LoadAnimation(path, maxWidth, maxHeight, loadCts.Token),
                loadCts.Token);

            if (IsDisposed || Disposing || requestId != _imageLoadRequestId || loadCts.IsCancellationRequested)
                return;

            _imageSequence = sequence;
            _animationFrameIndex = 0;
            _pictureBox.Image = sequence.GetFrame(_animationFrameIndex);
            StartAnimationIfNeeded();
            _pictureBox.Visible = true;

            int targetWidth = sequence.Width + Padding.Horizontal;
            int targetHeight = sequence.Height + Padding.Vertical + _infoLabel.Height;
            Size = new Size(
                Math.Clamp(targetWidth, minFormWidth, maxFormWidth),
                Math.Clamp(targetHeight, minFormHeight, maxFormHeight));
            CenterAndClampToWorkingArea(workingArea);
            sequence = null;
        }
        catch (OperationCanceledException)
        {
            // A newer preview replaced this one, or the popup was hidden.
        }
        catch
        {
            if (!IsDisposed && requestId == _imageLoadRequestId)
                Hide();
        }
        finally
        {
            sequence?.Dispose();
            if (ReferenceEquals(_imageLoadCts, loadCts))
                _imageLoadCts = null;
            loadCts.Dispose();
        }
    }

    private void CancelImageLoad()
    {
        _imageLoadRequestId++;
        try { _imageLoadCts?.Cancel(); } catch (ObjectDisposedException) { }
        _imageLoadCts = null;
    }

    private void ClearImagePreview()
    {
        CancelImageLoad();
        _animationTimer.Stop();
        _animationClock.Stop();
        _animationFrameDeadlineMs = 0;
        _animationFrameIndex = 0;
        _pictureBox.Image = null;
        _imageSequence?.Dispose();
        _imageSequence = null;
    }
}
