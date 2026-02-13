using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace SpeedExplorer;

internal sealed class IconLoadService : IDisposable
{
    private readonly Control _ui;
    private readonly ImageList _smallIcons;
    private readonly ImageList _largeIcons;
    private readonly Action _requestRepaint;
    private readonly Action<string>? _iconApplied;
    private readonly Func<bool>? _shouldLoadLargeIcons;

    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<IconLoadRequest> _highQueue = new();
    private readonly ConcurrentQueue<IconLoadRequest> _lowQueue = new();
    private readonly ConcurrentQueue<ReadyIcon> _ready = new();
    private readonly AutoResetEvent _signal = new(false);

    private volatile bool _stop;
    private int _generation;
    private int _flushScheduled;
    private Thread? _worker;

    private struct IconLoadRequest
    {
        public int Generation;
        public string KeyOrPath;
        public string? LookupPath;
        public bool IsDirectory;
        public bool Colored;
        public bool LowPriority;
    }

    private struct ReadyIcon
    {
        public int Generation;
        public string KeyOrPath;
        public Bitmap? SmallIcon;
        public Bitmap? LargeIcon;
    }

    public IconLoadService(
        Control ui,
        ImageList smallIcons,
        ImageList largeIcons,
        Action requestRepaint,
        Action<string>? iconApplied = null,
        Func<bool>? shouldLoadLargeIcons = null)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _smallIcons = smallIcons ?? throw new ArgumentNullException(nameof(smallIcons));
        _largeIcons = largeIcons ?? throw new ArgumentNullException(nameof(largeIcons));
        _requestRepaint = requestRepaint ?? throw new ArgumentNullException(nameof(requestRepaint));
        _iconApplied = iconApplied;
        _shouldLoadLargeIcons = shouldLoadLargeIcons;
    }

    public void Start()
    {
        if (_worker != null) return;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "SpeedExplorer.IconLoadService"
        };
        try { _worker.SetApartmentState(ApartmentState.STA); } catch { }
        _worker.Start();
    }

    public void Stop()
    {
        _stop = true;
        _signal.Set();
    }

    public void Dispose()
    {
        Stop();
        try { _worker?.Join(500); } catch { }
        DrainReadyQueue();
        _signal.Dispose();
    }

    public void CancelPending()
    {
        Interlocked.Increment(ref _generation);
        lock (_pending) { _pending.Clear(); }
        while (_highQueue.TryDequeue(out _)) { }
        while (_lowQueue.TryDequeue(out _)) { }
        DrainReadyQueue();
        _signal.Set();
    }

    public void EnsureGenericIcon(string key, string extension, bool isDirectory, bool colored)
    {
        if (_smallIcons.Images.ContainsKey(key))
            return;
        string lookupPath = isDirectory ? "C:\\DummyFolder" : extension;
        QueueIconLoad(key, isDirectory, colored, lowPriority: false, lookupPath: lookupPath);
    }

    public void QueueIconLoad(string keyOrPath, bool isDirectory, bool colored, bool lowPriority = false, string? lookupPath = null)
    {
        if (string.IsNullOrWhiteSpace(keyOrPath))
            return;

        bool effectiveLowPriority = lowPriority;
        if (!effectiveLowPriority &&
            (keyOrPath.Contains("\\") || keyOrPath.Contains("/")) &&
            AppSettings.Current.ShowThumbnails &&
            FileSystemService.IsImageFile(keyOrPath))
        {
            effectiveLowPriority = true;
        }

        lock (_pending)
        {
            if (_pending.Contains(keyOrPath))
                return;
            _pending.Add(keyOrPath);
        }

        var req = new IconLoadRequest
        {
            Generation = _generation,
            KeyOrPath = keyOrPath,
            LookupPath = lookupPath,
            IsDirectory = isDirectory,
            Colored = colored,
            LowPriority = effectiveLowPriority
        };

        if (effectiveLowPriority)
            _lowQueue.Enqueue(req);
        else
            _highQueue.Enqueue(req);

        _signal.Set();
    }

    public int PendingCount
    {
        get
        {
            lock (_pending)
            {
                return _pending.Count;
            }
        }
    }

    public int QueueCount => _highQueue.Count + _lowQueue.Count;

    private void WorkerLoop()
    {
        while (!_stop)
        {
            if (!_highQueue.TryDequeue(out var req) && !_lowQueue.TryDequeue(out req))
            {
                _signal.WaitOne(250);
                continue;
            }

            int currentGen = _generation;
            if (req.Generation != currentGen)
            {
                lock (_pending) { _pending.Remove(req.KeyOrPath); }
                continue;
            }

            try
            {
                string keyOrPath = req.KeyOrPath;
                bool isDirectory = req.IsDirectory;
                bool colored = req.Colored;

                bool isUnique = keyOrPath.Contains("\\") || keyOrPath.Contains("/");

                string lookupPath;
                bool isImage = false;

                if (!string.IsNullOrWhiteSpace(req.LookupPath))
                {
                    lookupPath = req.LookupPath!;
                    isImage = !isDirectory && FileSystemService.IsImageFile(lookupPath);
                }
                else if (isUnique)
                {
                    lookupPath = keyOrPath;
                    isImage = FileSystemService.IsImageFile(lookupPath);
                }
                else if (isDirectory || keyOrPath.Contains("folder", StringComparison.OrdinalIgnoreCase))
                {
                    lookupPath = "C:\\DummyFolder";
                }
                else
                {
                    if (keyOrPath.StartsWith("gray_", StringComparison.OrdinalIgnoreCase))
                        lookupPath = keyOrPath.Substring(5);
                    else if (keyOrPath.StartsWith("sys_", StringComparison.OrdinalIgnoreCase))
                        lookupPath = keyOrPath.Substring(4);
                    else
                        lookupPath = keyOrPath;
                }

                Bitmap? smallIcon = null;
                Bitmap? largeIcon = null;
                bool needLarge =
                    (_shouldLoadLargeIcons?.Invoke() ?? true) ||
                    _largeIcons.ImageSize.Width > 48 ||
                    _smallIcons.ImageSize.Width > 48;

                if (isImage && AppSettings.Current.ShowThumbnails)
                {
                    if (req.Generation != _generation) continue;
                    smallIcon = IconHelper.GetThumbnail(lookupPath, _smallIcons.ImageSize.Width);
                    if (req.Generation != _generation)
                    {
                        try { smallIcon?.Dispose(); } catch { }
                        continue;
                    }
                    if (needLarge)
                        largeIcon = IconHelper.GetThumbnail(lookupPath, _largeIcons.ImageSize.Width);
                }

                if (smallIcon == null)
                {
                    smallIcon = IconHelper.GetIconSized(lookupPath, isDirectory, _smallIcons.ImageSize.Width, isUnique, grayscale: !colored);
                    smallIcon ??= GetFallbackIconBitmap(lookupPath, isDirectory, _smallIcons.ImageSize.Width);
                }
                if (largeIcon == null)
                {
                    if (needLarge)
                    {
                        largeIcon = IconHelper.GetIconSized(lookupPath, isDirectory, _largeIcons.ImageSize.Width, isUnique, grayscale: !colored);
                        largeIcon ??= GetFallbackIconBitmap(lookupPath, isDirectory, _largeIcons.ImageSize.Width);
                    }
                    else if (smallIcon != null)
                        largeIcon ??= ResizeBitmapNoDispose(smallIcon, _largeIcons.ImageSize.Width);
                }

                if (smallIcon == null || largeIcon == null)
                {
                    try { smallIcon?.Dispose(); } catch { }
                    try { largeIcon?.Dispose(); } catch { }
                    continue;
                }

                if (!_ui.IsHandleCreated || _ui.IsDisposed)
                {
                    smallIcon.Dispose();
                    largeIcon.Dispose();
                    continue;
                }

                _ready.Enqueue(new ReadyIcon
                {
                    Generation = req.Generation,
                    KeyOrPath = keyOrPath,
                    SmallIcon = smallIcon,
                    LargeIcon = largeIcon
                });
                RequestFlushReady();
                smallIcon = null;
                largeIcon = null;
            }
            catch (Exception ex) { Debug.WriteLine($"IconLoadService.WorkerLoop error for '{req.KeyOrPath}': {ex.Message}"); }
            finally
            {
                lock (_pending) { _pending.Remove(req.KeyOrPath); }
            }
        }
    }

    private void RequestFlushReady()
    {
        if (_ui.IsDisposed || !_ui.IsHandleCreated)
            return;
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 1)
            return;

        try
        {
            _ui.BeginInvoke(new Action(FlushReadyQueueOnUi));
        }
        catch
        {
            Interlocked.Exchange(ref _flushScheduled, 0);
        }
    }

    private void FlushReadyQueueOnUi()
    {
        Interlocked.Exchange(ref _flushScheduled, 0);

        if (_ui.IsDisposed || !_ui.IsHandleCreated)
        {
            DrainReadyQueue();
            return;
        }

        bool anyAdded = false;
        int processed = 0;
        List<string>? appliedKeys = null;

        while (processed < 64 && _ready.TryDequeue(out var ready))
        {
            processed++;
            try
            {
                if (ready.Generation != _generation)
                    continue;
                if (ready.SmallIcon == null || ready.LargeIcon == null)
                    continue;

                bool isUnique = ready.KeyOrPath.Contains("\\") || ready.KeyOrPath.Contains("/");
                if (_smallIcons.Images.ContainsKey(ready.KeyOrPath))
                {
                    if (!isUnique)
                        continue;

                    try
                    {
                        _smallIcons.Images.RemoveByKey(ready.KeyOrPath);
                        _largeIcons.Images.RemoveByKey(ready.KeyOrPath);
                    }
                    catch
                    {
                        continue;
                    }
                }

                _smallIcons.Images.Add(ready.KeyOrPath, ready.SmallIcon);
                _largeIcons.Images.Add(ready.KeyOrPath, ready.LargeIcon);
                ready.SmallIcon = null;
                ready.LargeIcon = null;
                anyAdded = true;
                if (_iconApplied != null)
                {
                    appliedKeys ??= new List<string>(8);
                    appliedKeys.Add(ready.KeyOrPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IconLoadService.FlushReadyQueueOnUi error for '{ready.KeyOrPath}': {ex.Message}");
            }
            finally
            {
                try { ready.SmallIcon?.Dispose(); } catch { }
                try { ready.LargeIcon?.Dispose(); } catch { }
            }
        }

        if (anyAdded)
        {
            _requestRepaint();
            if (_iconApplied != null && appliedKeys != null)
            {
                for (int i = 0; i < appliedKeys.Count; i++)
                {
                    try { _iconApplied(appliedKeys[i]); } catch { }
                }
            }
        }

        if (!_ready.IsEmpty)
            RequestFlushReady();
    }

    private void DrainReadyQueue()
    {
        while (_ready.TryDequeue(out var ready))
        {
            try { ready.SmallIcon?.Dispose(); } catch { }
            try { ready.LargeIcon?.Dispose(); } catch { }
        }
    }

    private static Bitmap ResizeBitmapNoDispose(Bitmap source, int size)
    {
        var result = new Bitmap(size, size);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, size, size);
        return result;
    }

    private Bitmap? GetFallbackIconBitmap(string lookupPath, bool isDirectory, int targetSize)
    {
        string key = isDirectory
            ? "folder"
            : (FileSystemService.IsImageFile(lookupPath) ? "image" : "file");

        try
        {
            Image? img = null;
            if (_smallIcons.Images.ContainsKey(key))
                img = _smallIcons.Images[key];
            if ((img == null || img.Width <= 0 || img.Height <= 0) && _largeIcons.Images.ContainsKey(key))
                img = _largeIcons.Images[key];
            if (img == null)
                return null;

            var bmp = new Bitmap(targetSize, targetSize);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, 0, 0, targetSize, targetSize);
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
