using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (IsTextInputFocused())
            return base.ProcessCmdKey(ref msg, keyData);

        // Handle specific keys and combinations
        if (IsAnyHotkeyPressed(keyData, "ImageViewerPrevious", "ImageViewerPreviousAlt"))
        {
            ShowPrevious();
            return true;
        }
        if (IsAnyHotkeyPressed(keyData, "ImageViewerNext", "ImageViewerNextAlt", "ImageViewerNextSpace"))
        {
            ShowNext();
            return true;
        }
        if (IsHotkeyPressed("ImageViewerClose", keyData))
        {
            if (_isFullscreen) ToggleFullscreen(); else Close();
            return true;
        }
        if (IsAnyHotkeyPressed(keyData, "ImageViewerToggleFullscreen", "ImageViewerToggleFullscreenAlt"))
        {
            ToggleFullscreen();
            return true;
        }
        if (IsAnyHotkeyPressed(keyData, "ImageViewerZoomIn", "ImageViewerZoomInAlt"))
        {
            AdjustZoom(0.1f);
            return true;
        }
        if (IsAnyHotkeyPressed(keyData, "ImageViewerZoomOut", "ImageViewerZoomOutAlt"))
        {
            AdjustZoom(-0.1f);
            return true;
        }

        if (IsHotkeyPressed("ToggleOcrBoxes", keyData))
        {
            ToggleOverlayBoxes();
            return true;
        }
        if (IsHotkeyPressed("ToggleSavedTranslation", keyData))
        {
            ToggleSavedTranslation();
            return true;
        }
        if (IsHotkeyPressed("FitSmallDimension", keyData))
        {
            FitToWindowBySmallerDimension();
            return true;
        }
        if (IsHotkeyPressed("ImageViewerToggleAI", keyData))
        {
            ToggleAiPanel();
            return true;
        }
        if (IsHotkeyPressed("ImageViewerStartTranslation", keyData))
        {
            _ = RunViewerOcrAsync(true);
            return true;
        }
        if (IsHotkeyPressed("ImageViewerStartOcr", keyData))
        {
            _ = RunViewerOcrAsync(false);
            return true;
        }
        if (IsHotkeyPressed("ImageViewerTag", keyData))
        {
            _ = RunViewerTaggingAsync();
            return true;
        }
        if (IsHotkeyPressed("EditTags", keyData))
        {
            EditCurrentImageTags();
            return true;
        }
        if (IsHotkeyPressed("ImageViewerRotate", keyData))
        {
            RotateImageClockwise();
            return true;
        }

        if (IsAnyHotkeyPressed(keyData, "ImageViewerFitWindow", "ImageViewerFitWindowNumpad", "ImageViewerFitWindowPlain", "ImageViewerFitWindowPlainNumpad"))
        {
            FitToWindow();
            return true;
        }
        if (IsAnyHotkeyPressed(keyData, "ImageViewerActualSize", "ImageViewerActualSizeNumpad", "ImageViewerActualSizePlain", "ImageViewerActualSizePlainNumpad"))
        {
            ActualSize();
            return true;
        }
        if (IsAnyHotkeyPressed(keyData, "ImageViewerFitSmallDimensionControl", "ImageViewerFitSmallDimensionControlNumpad", "FitSmallDimension", "ImageViewerFitSmallDimensionNumpad"))
        {
            FitToWindowBySmallerDimension();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool IsTextInputFocused()
    {
        if (_aiOutputBox.Focused || ActiveControl == _aiOutputBox)
            return false;

        if (_targetLanguageBox.Focused ||
            _sourceLanguageHintBox.Focused ||
            _ocrHintBox.Focused ||
            _translationContextHintBox.Focused)
        {
            return true;
        }

        return ActiveControl is TextBoxBase or ComboBox or RichTextBox;
    }

    private void FocusViewerForHotkeys()
    {
        if (!IsTextInputFocused() && !_aiOutputBox.Focused)
            return;

        ActiveControl = null;
        Focus();
    }

    private Button CreateWindowButton(string text, string tooltip)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(Scale(46), TitleBarHeight),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = ForeColor_Dark,
            Font = new Font("Segoe UI", 9),
            Cursor = Cursors.Hand,
            Margin = new Padding(0)
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);

        var tt = new ToolTip();
        tt.SetToolTip(btn, tooltip);

        return btn;
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        const int HTCLIENT = 1;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;

        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HTCLIENT)
            {
                Point screenPoint = new Point(m.LParam.ToInt32());
                Point clientPoint = this.PointToClient(screenPoint);
                if (IsPointInCornerCloseHitZone(clientPoint))
                {
                    SetCornerCloseHover(true);
                    m.Result = (IntPtr)HTCLIENT;
                    return;
                }
                SetCornerCloseHover(false);

                if (this.WindowState != FormWindowState.Normal)
                    return;

                int resizeBorder = Scale(15);
                if (clientPoint.Y <= resizeBorder)
                {
                    if (clientPoint.X <= resizeBorder) m.Result = (IntPtr)HTTOPLEFT;
                    else if (clientPoint.X >= (this.Size.Width - resizeBorder)) m.Result = (IntPtr)HTTOPRIGHT;
                    else m.Result = (IntPtr)HTTOP;
                }
                else if (clientPoint.Y >= (this.Size.Height - resizeBorder))
                {
                    if (clientPoint.X <= resizeBorder) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (clientPoint.X >= (this.Size.Width - resizeBorder)) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else m.Result = (IntPtr)HTBOTTOM;
                }
                else
                {
                    if (clientPoint.X <= resizeBorder) m.Result = (IntPtr)HTLEFT;
                    else if (clientPoint.X >= (this.Size.Width - resizeBorder)) m.Result = (IntPtr)HTRIGHT;
                }
            }
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetCornerCloseHover(IsPointInCornerCloseHitZone(e.Location));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetCornerCloseHover(false);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && IsPointInCornerCloseHitZone(e.Location))
            Close();
    }

    private void SetCornerCloseHover(bool hover)
    {
        if (_cornerCloseHover == hover)
            return;

        _cornerCloseHover = hover;
        _closeBtn.BackColor = hover ? Color.FromArgb(232, 17, 35) : Color.Transparent;
        _closeBtn.ForeColor = ForeColor_Dark;
    }

    private bool IsPointInCornerCloseHitZone(Point clientPoint)
    {
        if (_isFullscreen)
            return false;

        int closeWidth = _closeBtn.Width > 0 ? _closeBtn.Width : Scale(46);
        int closeHeight = Math.Max(TitleBarHeight, _closeBtn.Height);
        return clientPoint.X >= Width - closeWidth - WindowFramePadding.Right &&
            clientPoint.Y <= closeHeight + WindowFramePadding.Top;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_autoFitEnabled && !_isFullscreen)
        {
            if (_autoFitBySmallerDimension)
                FitToWindowBySmallerDimension(allowUpscale: false);
            else
                FitToWindow(allowUpscale: false);
        }
    }

}
