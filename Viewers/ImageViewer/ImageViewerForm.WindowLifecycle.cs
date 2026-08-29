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
            WindowState = FormWindowState.Normal;
        }
        else
        {
            // Respect taskbar
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
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
                FitToWindowBySmallerDimension();
            else
                FitToWindow();
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
        Padding = _isFullscreen ? Padding.Empty : WindowFramePadding;
        _controlPanel.Visible = !_isFullscreen;
        _titleBar.Visible = !_isFullscreen;
        Invalidate();
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
        FitToWindow(allowUpscale: false);
    }

    private void ApplySavedWindowState()
    {
        if (_settings.ImageViewerMaximized)
        {
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
    }

    private void SaveWindowState()
    {
        _settings.ImageViewerMaximized = WindowState == FormWindowState.Maximized;
        if (WindowState == FormWindowState.Normal)
        {
            _settings.ImageViewerWidth = Width;
            _settings.ImageViewerHeight = Height;
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

        _animationFrameIndex = (_animationFrameIndex + 1) % _currentAnimation.FrameCount;
        _currentImage = _currentAnimation.GetFrame(_animationFrameIndex);
        _animationTimer.Interval = _currentAnimation.GetFrameDelayMs(_animationFrameIndex);
        _pictureBox.Invalidate();
    }

    private void StartAnimationIfNeeded()
    {
        if (_currentAnimation == null || !_currentAnimation.IsAnimated)
        {
            _animationTimer.Stop();
            return;
        }

        _animationTimer.Interval = _currentAnimation.GetFrameDelayMs(_animationFrameIndex);
        _animationTimer.Start();
    }

    private void ClearAnimationState()
    {
        _animationTimer.Stop();
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
