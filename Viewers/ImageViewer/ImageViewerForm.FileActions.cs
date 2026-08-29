using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class ImageViewerForm
{
    private void UpdateTags(string path)
    {
        _tagsPanel.Controls.Clear();
        var tags = TagManager.Instance.GetTags(path);

        foreach (var tag in tags)
        {
            var tagLabel = new Label
            {
                Text = tag,
                AutoSize = true,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.LightGray,
                Font = new Font("Segoe UI", 7),
                Padding = Scale(new Padding(5, 1, 5, 1)),
                Margin = Scale(new Padding(0, 1, 4, 0))
            };

            tagLabel.Paint += (s, e) =>
            {
                using var p = new Pen(Color.FromArgb(80, 80, 80));
                e.Graphics.DrawRectangle(p, 0, 0, tagLabel.Width - 1, tagLabel.Height - 1);
            };

            _tagsPanel.Controls.Add(tagLabel);
        }
    }

    private string? GetCurrentImagePath()
    {
        if (_currentIndex < 0 || _currentIndex >= _imagePaths.Count)
            return null;
        return _imagePaths[_currentIndex];
    }

    private ContextMenuStrip BuildImageContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkToolStripRenderer(),
            ShowImageMargin = false,
            BackColor = Color.FromArgb(30, 30, 30)
        };
        _editOverlayBlockMenuItem = new ToolStripMenuItem("Edit OCR/Translation Box", null, (s, e) => EditContextOverlayBlock())
        {
            Enabled = false
        };
        menu.Items.Add(_editOverlayBlockMenuItem);
        menu.Items.Add("Set overlay defaults for this image", null, (s, e) => EditOverlayDefaults(perImage: true));
        menu.Items.Add("Set global overlay defaults", null, (s, e) => EditOverlayDefaults(perImage: false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Draw OCR Box", null, (s, e) => ToggleManualOcrDrawMode());
        menu.Items.Add("Clear Pending OCR Boxes", null, (s, e) => ClearPendingManualOcrRegions());
        menu.Items.Add("AI Tagging", null, async (s, e) => await RunViewerTaggingAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Localization.T("rotate_clockwise"), null, (s, e) => RotateImageClockwise());
        menu.Items.Add(Localization.T("edit_tags"), null, (s, e) => EditCurrentImageTags());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Copy Image File", null, (s, e) => CopyCurrentImageFileToClipboard());
        menu.Items.Add("Open File Location", null, (s, e) => OpenCurrentImageLocation());
        menu.Items.Add(Localization.T("properties"), null, (s, e) => ShowCurrentImageProperties());
        menu.Opening += (s, e) =>
        {
            _editOverlayBlockMenuItem.Enabled = _contextOverlayBlockIndex >= 0;
        };
        return menu;
    }

    private void EditCurrentImageTags()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        var currentTags = TagManager.Instance.GetTags(imagePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var dlg = new EditTagsForm(string.Join(", ", currentTags));
        dlg.Text = Localization.T("edit_tags_title");

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var finalTags = dlg.TagsResult
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (dlg.ClearAllRequested)
        {
            TagManager.Instance.SetTagsBatch(new[] { imagePath }, finalTags);
        }
        else
        {
            var toAdd = finalTags.Where(t => !currentTags.Contains(t)).ToList();
            var toRemove = currentTags.Where(t => !finalTags.Contains(t)).ToList();
            TagManager.Instance.UpdateTagsBatch(new[] { imagePath }, toAdd, toRemove);
        }

        UpdateTags(imagePath);
    }

    private void CopyCurrentImageFileToClipboard()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        try
        {
            var files = new StringCollection { imagePath };
            Clipboard.SetFileDropList(files);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CopyCurrentImageFileToClipboard failed: {ex.Message}");
        }
    }

    private void OpenCurrentImageLocation()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        string selectArg = $"/select,\"{imagePath}\"";
        try
        {
            var existingMain = Application.OpenForms
                .OfType<MainForm>()
                .LastOrDefault(f => !f.IsDisposed);
            if (existingMain != null)
            {
                if (existingMain.WindowState == FormWindowState.Minimized)
                    existingMain.WindowState = FormWindowState.Normal;
                existingMain.Show();
                existingMain.Activate();
                existingMain.BringToFront();
                existingMain.HandleExternalPathNoViewer(selectArg);
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenCurrentImageLocation via existing MainForm failed: {ex.Message}");
        }

        try
        {
            string directory = Path.GetDirectoryName(imagePath) ?? imagePath;
            string exePath = Application.ExecutablePath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenCurrentImageLocation via app launch failed: {ex.Message}");
        }
    }

    private void ShowCurrentImageProperties()
    {
        string? imagePath = GetCurrentImagePath();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = SEE_MASK_INVOKEIDLIST,
                hwnd = Handle,
                lpVerb = "properties",
                lpFile = imagePath,
                lpParameters = string.Empty,
                lpDirectory = string.Empty,
                nShow = 5
            };
            _ = ShellExecuteEx(ref info);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowCurrentImageProperties failed: {ex.Message}");
        }
    }

}
