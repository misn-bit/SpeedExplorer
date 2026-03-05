using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public sealed class ManualContextActionsSettingsControl : UserControl
{
    private sealed class ManualActionTextBox : TextBox
    {
        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Space || (keyData >= Keys.A && keyData <= Keys.Z) ||
                (keyData >= Keys.D0 && keyData <= Keys.D9))
            {
                return false;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private readonly ListBox _list;
    private readonly TextBox _nameBox;
    private readonly TextBox _commandBox;
    private readonly TextBox _argsBox;
    private readonly ComboBox _appliesToBox;
    private readonly TextBox _extensionsBox;
    private readonly CheckBox _allowMultipleChk;
    private readonly TextBox _workingDirBox;
    private readonly CheckBox _visibleChk;
    private readonly List<ManualContextAction> _actions = new();
    private bool _updating = false;

    private int Scale(int pixels) => (int)(pixels * (this.DeviceDpi / 96.0));

    public ManualContextActionsSettingsControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.Transparent;

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(Scale(12)),
            BackColor = Color.Transparent
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(220)));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(main);

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White
        };
        _list.SelectedIndexChanged += (s, e) => LoadSelected();

        var listPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        listPanel.Controls.Add(_list);

        var listButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Scale(40),
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent
        };
        var addBtn = new Button { Text = Localization.T("action_new"), Width = Scale(70) };
        var deleteBtn = new Button { Text = Localization.T("action_delete"), Width = Scale(70) };
        SettingsButtonStyle.ApplyNeutral(addBtn);
        SettingsButtonStyle.ApplyNeutral(deleteBtn);
        addBtn.Click += (s, e) => AddNew();
        deleteBtn.Click += (s, e) => DeleteSelected();
        listButtons.Controls.Add(addBtn);
        listButtons.Controls.Add(deleteBtn);
        listPanel.Controls.Add(listButtons);

        main.Controls.Add(listPanel, 0, 0);

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 9,
            ColumnCount = 2,
            BackColor = Color.Transparent
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(120)));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 8; i++)
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(30)));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.Controls.Add(editor, 1, 0);

        _nameBox = CreateTextBox();
        _commandBox = CreateTextBox();
        _argsBox = CreateTextBox();
        _extensionsBox = CreateTextBox();
        _workingDirBox = CreateTextBox();
        _allowMultipleChk = new CheckBox { Text = Localization.T("action_allow_multi"), ForeColor = Color.White, AutoSize = true, BackColor = Color.Transparent };
        _visibleChk = new CheckBox { Text = Localization.T("action_visible"), ForeColor = Color.White, AutoSize = true, BackColor = Color.Transparent };

        _appliesToBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        _appliesToBox.Items.AddRange(new object[]
        {
            new AppliesOption("Files", Localization.T("applies_files")),
            new AppliesOption("Folders", Localization.T("applies_folders")),
            new AppliesOption("Both", Localization.T("applies_both"))
        });

        _nameBox.TextChanged += (s, e) => ApplyEdits();
        _commandBox.TextChanged += (s, e) => ApplyEdits();
        _argsBox.TextChanged += (s, e) => ApplyEdits();
        _extensionsBox.TextChanged += (s, e) => ApplyEdits();
        _workingDirBox.TextChanged += (s, e) => ApplyEdits();
        _allowMultipleChk.CheckedChanged += (s, e) => ApplyEdits();
        _visibleChk.CheckedChanged += (s, e) => ApplyEdits();
        _appliesToBox.SelectedIndexChanged += (s, e) => ApplyEdits();

        AddRow(editor, 0, Localization.T("action_name"), _nameBox);
        AddRow(editor, 1, Localization.T("action_command"), _commandBox);
        AddRow(editor, 2, Localization.T("action_args"), _argsBox);
        AddRow(editor, 3, Localization.T("action_applies"), _appliesToBox);
        AddRow(editor, 4, Localization.T("action_ext"), _extensionsBox);
        AddRow(editor, 5, Localization.T("action_workdir"), _workingDirBox);

        editor.Controls.Add(_allowMultipleChk, 1, 6);
        editor.Controls.Add(_visibleChk, 1, 7);

        var help = new Label
        {
            Text = Localization.T("action_help"),
            ForeColor = Color.Gray,
            AutoSize = true,
            Margin = new Padding(3, 8, 3, 3),
            BackColor = Color.Transparent
        };
        editor.Controls.Add(help, 1, 8);
    }

    public void LoadFromSettings(AppSettings settings)
    {
        _actions.Clear();
        _actions.AddRange((settings.ManualContextActions ?? new List<ManualContextAction>()).Select(a => new ManualContextAction
        {
            Name = a.Name,
            Command = a.Command,
            Args = a.Args,
            AppliesTo = a.AppliesTo,
            Extensions = a.Extensions,
            AllowMultiple = a.AllowMultiple,
            WorkingDir = a.WorkingDir,
            VisibleInShell = a.VisibleInShell
        }));
        RefreshList();
        if (_actions.Count > 0)
            _list.SelectedIndex = 0;
    }

    public void ApplyToSettings(AppSettings settings)
    {
        ApplyEdits();
        settings.ManualContextActions = _actions.ToList();
    }

    private TextBox CreateTextBox()
    {
        return new ManualActionTextBox
        {
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill
        };
    }

    private void AddRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        var lbl = new Label
        {
            Text = label,
            ForeColor = Color.White,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(3, 6, 3, 3),
            BackColor = Color.Transparent
        };
        panel.Controls.Add(lbl, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (var a in _actions)
            _list.Items.Add(string.IsNullOrWhiteSpace(a.Name) ? "Unnamed" : a.Name);
    }

    private void LoadSelected()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _actions.Count) return;
        _updating = true;
        var a = _actions[_list.SelectedIndex];
        _nameBox.Text = a.Name;
        _commandBox.Text = a.Command;
        _argsBox.Text = a.Args;
        foreach (var item in _appliesToBox.Items)
        {
            if (item is AppliesOption opt && string.Equals(opt.Value, a.AppliesTo, StringComparison.OrdinalIgnoreCase))
            {
                _appliesToBox.SelectedItem = item;
                break;
            }
        }
        _extensionsBox.Text = a.Extensions;
        _allowMultipleChk.Checked = a.AllowMultiple;
        _workingDirBox.Text = a.WorkingDir;
        _visibleChk.Checked = a.VisibleInShell;
        _updating = false;
    }

    private void AddNew()
    {
        var action = new ManualContextAction();
        _actions.Add(action);
        RefreshList();
        _list.SelectedIndex = _actions.Count - 1;
    }

    private void DeleteSelected()
    {
        if (_list.SelectedIndex < 0) return;
        int old = _list.SelectedIndex;
        _actions.RemoveAt(old);
        RefreshList();
        if (_actions.Count > 0)
            _list.SelectedIndex = Math.Min(old, _actions.Count - 1);
    }

    private void ApplyEdits()
    {
        if (_updating) return;
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _actions.Count) return;

        var a = _actions[_list.SelectedIndex];
        a.Name = _nameBox.Text;
        a.Command = _commandBox.Text;
        a.Args = _argsBox.Text;
        if (_appliesToBox.SelectedItem is AppliesOption opt)
            a.AppliesTo = opt.Value;
        else
            a.AppliesTo = "Both";
        a.Extensions = _extensionsBox.Text;
        a.AllowMultiple = _allowMultipleChk.Checked;
        a.WorkingDir = _workingDirBox.Text;
        a.VisibleInShell = _visibleChk.Checked;

        string display = string.IsNullOrWhiteSpace(a.Name) ? "Unnamed" : a.Name;
        if (_list.Items[_list.SelectedIndex]?.ToString() != display)
            _list.Items[_list.SelectedIndex] = display;
    }

    private sealed class AppliesOption
    {
        public string Value { get; }
        public string Display { get; }

        public AppliesOption(string value, string display)
        {
            Value = value;
            Display = display;
        }

        public override string ToString() => Display;
    }
}
