using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private bool IsHotkeyPressed(string action, Keys keyData)
    {
        if (!_settings.Hotkeys.TryGetValue(action, out var bindingText) || string.IsNullOrWhiteSpace(bindingText))
            return false;

        try
        {
            var converted = new KeysConverter().ConvertFromString(bindingText);
            if (converted is not Keys parsed)
                return false;

            return NormalizeHotkeyKeyData(keyData) == NormalizeHotkeyBinding(parsed);
        }
        catch
        {
            return false;
        }
    }

    private static Keys NormalizeHotkeyKeyData(Keys keyData)
    {
        var code = keyData & Keys.KeyCode;
        var mods = Control.ModifierKeys & (Keys.Control | Keys.Shift | Keys.Alt);
        return code | mods;
    }

    private static Keys NormalizeHotkeyBinding(Keys binding)
    {
        var code = binding & Keys.KeyCode;
        var mods = binding & (Keys.Control | Keys.Shift | Keys.Alt);
        return code | mods;
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            Padding = WindowFramePadding;
            WindowState = FormWindowState.Normal;
        }
        else
        {
            // Respect taskbar
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
            Padding = Padding.Empty;
            WindowState = FormWindowState.Maximized;
        }
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            if (_previousWindowState == FormWindowState.Maximized)
            {
                // Force state reset to apply MaximizedBounds
                WindowState = FormWindowState.Normal;
                MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                WindowState = _previousWindowState;
            }

            _isFullscreen = false;
        }
        else
        {
            _previousWindowState = WindowState;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            // Fullscreen should cover taskbar, so clear bounds
            MaximizedBounds = Rectangle.Empty;
            WindowState = FormWindowState.Maximized;
            _isFullscreen = true;
        }

        ApplyChromeVisibility();
        if (_autoFitEnabled)
        {
            if (_autoFitBySmallerDimension)
                FitToWindowBySmallerDimension(allowUpscale: false);
            else
                FitToWindow(allowUpscale: false);
        }
    }

    private bool IsAnyHotkeyPressed(Keys keyData, params string[] actions)
    {
        foreach (var action in actions)
        {
            if (IsHotkeyPressed(action, keyData))
                return true;
        }
        return false;
    }

    private void ApplyChromeVisibility()
    {
        Padding = (WindowState == FormWindowState.Maximized || _isFullscreen) ? Padding.Empty : WindowFramePadding;
        _controlPanel.Visible = !_isFullscreen;
        _titleBar.Visible = !_isFullscreen;
        Invalidate();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (WindowState == FormWindowState.Maximized && !_isFullscreen)
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
        UpdateLayoutForDpi();
        ClampNormalBoundsToCurrentWorkingArea();
    }

    private void ClampNormalBoundsToCurrentWorkingArea()
    {
        if (WindowState != FormWindowState.Normal || !IsHandleCreated)
            return;

        var workArea = Screen.FromHandle(Handle).WorkingArea;
        int width = Math.Min(Width, workArea.Width);
        int height = Math.Min(Height, workArea.Height);
        int left = Math.Clamp(Left, workArea.Left, workArea.Right - width);
        int top = Math.Clamp(Top, workArea.Top, workArea.Bottom - height);
        var bounds = new Rectangle(left, top, width, height);
        if (Bounds != bounds)
            Bounds = bounds;
    }

    private void QueueDisplayMetricsRefresh()
    {
        if (!IsHandleCreated || IsDisposed || Disposing)
            return;

        try
        {
            BeginInvoke((Action)(() =>
            {
                if (!IsHandleCreated || IsDisposed || Disposing)
                    return;

                if (WindowState == FormWindowState.Maximized)
                {
                    MaximizedBounds = _isFullscreen
                        ? Rectangle.Empty
                        : Screen.FromHandle(Handle).WorkingArea;
                    WindowState = FormWindowState.Normal;
                    WindowState = FormWindowState.Maximized;
                }
                else
                {
                    ClampNormalBoundsToCurrentWorkingArea();
                }

                UpdateLayoutForDpi();
            }));
        }
        catch (Exception __ex)
        {
            System.Diagnostics.Debug.WriteLine(__ex);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveImageViewerAiSettings();
        SaveWindowState();
        base.OnFormClosing(e);
    }

    private void SaveImageViewerAiSettings()
    {
        _settings.ImageViewerAiPanelVisible = _aiPanel.Visible;
        _settings.ImageViewerTargetLanguage = _targetLanguageBox.Text;
        _settings.ImageViewerSourceLanguageHint = _sourceLanguageHintBox.Text;
        _settings.ImageViewerOcrHint = _ocrHintBox.Text;
        _settings.ImageViewerTranslationContextHint = _translationContextHintBox.Text;
        _settings.ImageViewerManualMaxEffortTranslation = _manualMaxEffortCheck.Checked;
        _settings.ImageViewerOverlayBoxesVisible = _overlayToggle.Checked;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _imageLoadRequestId++;
        try { _imageLoadCts?.Cancel(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
        _imageLoadCts = null;
        ClearAnimationState();
        _imageFolderRefreshTimer.Stop();
        _imageFolderRefreshTimer.Dispose();
        _imageFolderWatcher?.Dispose();
        _imageFolderWatcher = null;
        _animationTimer.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (WindowState == FormWindowState.Normal)
        {
            var workArea = Screen.FromHandle(Handle).WorkingArea;
            int width = Math.Min(Scale(Math.Max(400, _settings.ImageViewerWidth)), workArea.Width);
            int height = Math.Min(Scale(Math.Max(300, _settings.ImageViewerHeight)), workArea.Height);
            int left = workArea.Left + (workArea.Width - width) / 2;
            int top = workArea.Top + (workArea.Height - height) / 2;
            Bounds = new Rectangle(left, top, width, height);
        }
        UpdateLayoutForDpi();
        LoadCurrentImage();
    }

    private void FormStateChanged(object? sender, EventArgs e)
    {
        Padding = (WindowState == FormWindowState.Maximized || _isFullscreen) ? Padding.Empty : WindowFramePadding;
        ApplyAutoFitIfEnabled();
    }

    private void ApplyAutoFitIfEnabled()
    {
        if (!_autoFitEnabled || _isFullscreen)
            return;

        if (_autoFitBySmallerDimension)
            FitToWindowBySmallerDimension(allowUpscale: false);
        else
            FitToWindow(allowUpscale: false);
    }

    private void ApplySavedWindowState()
    {
        if (_settings.ImageViewerMaximized)
        {
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
            Padding = Padding.Empty;
            WindowState = FormWindowState.Maximized;
        }
    }

    private void SaveWindowState()
    {
        if (WindowState == FormWindowState.Minimized)
            return;

        _settings.ImageViewerMaximized = WindowState == FormWindowState.Maximized;
        if (WindowState == FormWindowState.Normal)
        {
            _settings.ImageViewerWidth = Unscale(Width);
            _settings.ImageViewerHeight = Unscale(Height);
        }
        _settings.Save();
    }

    private void SetZoomSliderValue(int value)
    {
        _suppressZoomSliderEvent = true;
        _zoomSlider.Value = Math.Clamp(value, _zoomSlider.Minimum, _zoomSlider.Maximum);
        _zoomLabel.Text = $"{_zoomSlider.Value}%";
        _suppressZoomSliderEvent = false;
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentAnimation == null || !_currentAnimation.IsAnimated)
        {
            _animationTimer.Stop();
            return;
        }

        long elapsedMs = _animationClock.ElapsedMilliseconds;
        int advancedFrames = 0;
        while (elapsedMs >= _animationFrameDeadlineMs && advancedFrames < _currentAnimation.FrameCount)
        {
            _animationFrameIndex = (_animationFrameIndex + 1) % _currentAnimation.FrameCount;
            _animationFrameDeadlineMs += _currentAnimation.GetFrameDelayMs(_animationFrameIndex);
            advancedFrames++;
        }

        if (advancedFrames == 0)
        {
            _animationTimer.Interval = Math.Max(1, (int)(_animationFrameDeadlineMs - elapsedMs));
            return;
        }

        _currentImage = _currentAnimation.GetFrame(_animationFrameIndex);
        long remainingMs = Math.Max(1, _animationFrameDeadlineMs - _animationClock.ElapsedMilliseconds);
        _animationTimer.Interval = (int)Math.Min(int.MaxValue, remainingMs);
        _pictureBox.Invalidate();
    }

    private void StartAnimationIfNeeded()
    {
        if (_currentAnimation == null || !_currentAnimation.IsAnimated)
        {
            _animationTimer.Stop();
            return;
        }

        _animationClock.Restart();
        _animationFrameDeadlineMs = _currentAnimation.GetFrameDelayMs(_animationFrameIndex);
        _animationTimer.Interval = _currentAnimation.GetFrameDelayMs(_animationFrameIndex);
        _animationTimer.Start();
    }

    private void ClearAnimationState()
    {
        _animationTimer.Stop();
        _animationClock.Stop();
        _animationFrameDeadlineMs = 0;
        _animationFrameIndex = 0;
        if (_currentAnimation != null)
        {
            _currentAnimation.Dispose();
        }
        else
        {
            _currentImage?.Dispose();
        }

        _currentImage = null;
        _currentAnimation = null;
    }
}
