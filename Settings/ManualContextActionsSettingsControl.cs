using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SpeedExplorer.Design;

namespace SpeedExplorer;

/// <summary>
/// Manual context actions page: a list of user-defined context menu entries
/// (command + args + applies-to + optional extensions / working dir).
/// </summary>
internal sealed class ManualContextActionsSettingsControl : SettingsPage
{
    private readonly ListBox _list;
    private readonly TextField _nameBox, _commandBox, _argsBox, _extensionsBox, _workingDirBox;
    private readonly Dropdown _appliesToBox;
    private readonly ToggleSwitch _allowMultipleChk, _visibleChk;
    private readonly List<ManualContextAction> _actions = new();
    private bool _updating = false;

    public ManualContextActionsSettingsControl() : base(Localization.T("context_menu_tab"))
    {
        Font = new Font("Segoe UI", 10);

        var group = new SettingsGroup();

        _list = new ListBox { Dock = DockStyle.Fill, Height = 120 };
        _list.SelectedIndexChanged += (s, e) => LoadSelected();
        group.AddRow(Localization.T("actions_list"), string.Empty, _list, stacked: true);

        _nameBox = group.AddText(Localization.T("action_name"), string.Empty, string.Empty, string.Empty);
        _commandBox = group.AddText(Localization.T("action_command"), string.Empty, string.Empty, string.Empty);
        _argsBox = group.AddText(Localization.T("action_args"), string.Empty, string.Empty, string.Empty);

        _appliesToBox = group.AddDropdown(Localization.T("action_applies"), string.Empty, "Both", string.Empty, string.Empty, _ => { });
        _appliesToBox.SetItems(new[] {
            Localization.T("applies_files"),
            Localization.T("applies_folders"),
            Localization.T("applies_both")
        });

        _extensionsBox = group.AddText(Localization.T("action_ext"), string.Empty, string.Empty, string.Empty);
        _workingDirBox = group.AddText(Localization.T("action_workdir"), string.Empty, string.Empty, string.Empty);
        _allowMultipleChk = group.AddToggle(Localization.T("action_allow_multi"), string.Empty, false, _ => { });
        _visibleChk = group.AddToggle(Localization.T("action_visible"), string.Empty, false, _ => { });

        // New / Delete buttons on one row
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        var addBtn = new FlatButton { Text = Localization.T("action_new"), Kind = ButtonKind.Secondary };
        var deleteBtn = new FlatButton { Text = Localization.T("action_delete"), Kind = ButtonKind.Secondary };
        addBtn.Margin = new Padding(0, 0, 8, 0);
        addBtn.Click += (s, e) => AddNew();
        deleteBtn.Click += (s, e) => DeleteSelected();
        buttons.Controls.Add(addBtn);
        buttons.Controls.Add(deleteBtn);
        group.AddRow(string.Empty, Localization.T("action_help"), buttons, stacked: true);

        Controls.Add(group);

        _nameBox.ValueChanged += (s, e) => ApplyEdits();
        _commandBox.ValueChanged += (s, e) => ApplyEdits();
        _argsBox.ValueChanged += (s, e) => ApplyEdits();
        _extensionsBox.ValueChanged += (s, e) => ApplyEdits();
        _workingDirBox.ValueChanged += (s, e) => ApplyEdits();
        _allowMultipleChk.CheckedChanged += (s, e) => ApplyEdits();
        _visibleChk.CheckedChanged += (s, e) => ApplyEdits();
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
        _nameBox.Value = a.Name;
        _commandBox.Value = a.Command;
        _argsBox.Value = a.Args;
        _appliesToBox.Value = a.AppliesTo.Equals("Files", StringComparison.OrdinalIgnoreCase)
            ? Localization.T("applies_files")
            : a.AppliesTo.Equals("Folders", StringComparison.OrdinalIgnoreCase)
                ? Localization.T("applies_folders")
                : Localization.T("applies_both");
        _extensionsBox.Value = a.Extensions;
        _allowMultipleChk.Checked = a.AllowMultiple;
        _workingDirBox.Value = a.WorkingDir;
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
        a.Name = _nameBox.Value;
        a.Command = _commandBox.Value;
        a.Args = _argsBox.Value;
        a.AppliesTo = _appliesToBox.Value == Localization.T("applies_files") ? "Files"
                     : _appliesToBox.Value == Localization.T("applies_folders") ? "Folders"
                     : "Both";
        a.Extensions = _extensionsBox.Value;
        a.AllowMultiple = _allowMultipleChk.Checked;
        a.WorkingDir = _workingDirBox.Value;
        a.VisibleInShell = _visibleChk.Checked;

        string display = string.IsNullOrWhiteSpace(a.Name) ? "Unnamed" : a.Name;
        if (_list.Items[_list.SelectedIndex]?.ToString() != display)
            _list.Items[_list.SelectedIndex] = display;
    }
}
