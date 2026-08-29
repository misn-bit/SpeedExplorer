using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public class EditTagsForm : Form
{
    public string TagsResult { get; private set; } = "";
    public bool ClearAllRequested { get; private set; } = false;

    private readonly Label _promptLabel;
    private readonly TextBox _tagsInput;
    private readonly Label _knownLabel;
    private readonly ListBox _recentTagsList;
    private readonly Button _btnCancel;
    private readonly Button _btnClear;
    private readonly Button _btnOk;

    private int Scale(int pixels) => (int)(pixels * (this.DeviceDpi / 96.0));
    private int Unscale(int pixels) => (int)Math.Round(pixels * (96.0 / this.DeviceDpi));
    private Padding Scale(Padding p) => new Padding(Scale(p.Left), Scale(p.Top), Scale(p.Right), Scale(p.Bottom));

    public EditTagsForm(string currentTags)
    {
        Text = Localization.T("edit_tags_title");
        var s = AppSettings.Current;
        var initWidth = s.EditTagsWidth > 0 ? s.EditTagsWidth : 400;
        var initHeight = s.EditTagsHeight > 0 ? s.EditTagsHeight : 310;
        Size = new Size(Scale(initWidth), Scale(initHeight));
        MinimumSize = new Size(Scale(300), Scale(250));
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        if (s.EditTagsMaximized)
            WindowState = FormWindowState.Maximized;

        // Dark Theme
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.FromArgb(240, 240, 240);

        _promptLabel = new Label
        {
            Text = Localization.T("edit_tags_prompt"),
            Location = new Point(Scale(10), Scale(10)),
            AutoSize = true,
            ForeColor = Color.Gray,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        Controls.Add(_promptLabel);

        _tagsInput = new TextBox
        {
            Text = currentTags,
            Location = new Point(Scale(10), Scale(35)),
            Width = Scale(360),
            BackColor = Color.FromArgb(40, 40, 40),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(_tagsInput);

        _knownLabel = new Label
        {
            Text = Localization.T("edit_tags_known"),
            Location = new Point(Scale(10), Scale(70)),
            AutoSize = true,
            ForeColor = Color.Gray,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        Controls.Add(_knownLabel);

        _recentTagsList = new ListBox
        {
            Location = new Point(Scale(10), Scale(95)),
            Width = Scale(360),
            Height = Scale(120),
            BackColor = Color.FromArgb(40, 40, 40),
            ForeColor = Color.LightGray,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        // Populate with known tags
        var allTags = TagManager.Instance.GetAllKnownTags();
        _recentTagsList.Items.AddRange(allTags.ToArray());
        _recentTagsList.MouseDoubleClick += (s, e) => AddSelectedTag();
        Controls.Add(_recentTagsList);

        _btnCancel = new Button
        {
            Text = Localization.T("cancel"),
            DialogResult = DialogResult.Cancel,
            Size = new Size(Scale(75), Scale(30)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _btnCancel.FlatAppearance.BorderSize = 0;
        Controls.Add(_btnCancel);

        _btnClear = new Button
        {
            Text = Localization.T("edit_tags_clear"),
            Size = new Size(Scale(110), Scale(30)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(180, 50, 50),
            ForeColor = Color.White,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _btnClear.FlatAppearance.BorderSize = 0;
        _btnClear.Click += (s, e) => 
        { 
            ClearAllRequested = true; 
            _tagsInput.Text = ""; 
            _btnClear.Text = Localization.T("edit_tags_clear_pending");
            _btnClear.BackColor = Color.FromArgb(60, 60, 60);
            _btnClear.Enabled = false;
        };
        Controls.Add(_btnClear);

        _btnOk = new Button
        {
            Text = Localization.T("ok"),
            DialogResult = DialogResult.OK,
            Size = new Size(Scale(75), Scale(30)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 120, 212),
            ForeColor = Color.White,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _btnOk.FlatAppearance.BorderSize = 0;
        _btnOk.Click += (s, e) => { TagsResult = _tagsInput.Text; Close(); };
        Controls.Add(_btnOk);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        FormClosing += (sender, args) =>
        {
            s.EditTagsMaximized = WindowState == FormWindowState.Maximized;
            if (WindowState == FormWindowState.Normal)
            {
                s.EditTagsWidth = Unscale(Width);
                s.EditTagsHeight = Unscale(Height);
            }
            s.Save();
        };

        ApplyLayout();
        Resize += (sender, args) => ApplyLayout();
    }

    private void ApplyLayout()
    {
        int padding = Scale(10);
        int gap = Scale(10);
        int buttonHeight = Scale(30);

        _tagsInput.Width = ClientSize.Width - (padding * 2);
        _recentTagsList.Width = ClientSize.Width - (padding * 2);

        int buttonsTop = ClientSize.Height - padding - buttonHeight;
        _btnClear.Location = new Point(padding, buttonsTop);
        _btnCancel.Location = new Point(ClientSize.Width - padding - _btnCancel.Width, buttonsTop);
        _btnOk.Location = new Point(_btnCancel.Left - Scale(10) - _btnOk.Width, buttonsTop);

        int listTop = _recentTagsList.Top;
        int listHeight = Math.Max(Scale(60), buttonsTop - gap - listTop);
        _recentTagsList.Height = listHeight;
    }

    private void AddSelectedTag()
    {
        if (_recentTagsList.SelectedItem is string tag)
        {
            var current = _tagsInput.Text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                         .Select(t => t.Trim())
                                         .ToList();
            
            if (!current.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                current.Add(tag);
                _tagsInput.Text = string.Join(", ", current);
            }
        }
    }
}
