using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class AddressBarController
    {
        private readonly MainForm _owner;

        public AddressBarController(MainForm owner)
        {
            _owner = owner;
        }

        public Control CreateAddressBar()
        {
            _owner._addressTextBox = new TextBox
            {
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = ForeColor_Dark,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 11f),
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Visible = false
            };

            _owner._addressTextBox.GotFocus += (s, e) => _owner._addressTextBox.SelectAll();
            _owner._addressTextBox.LostFocus += (s, e) =>
            {
                _owner.BeginInvoke(new Action(() =>
                {
                    if (!_owner._addressTextBox.Focused)
                    {
                        _owner._addressTextBox.Visible = false;
                        _owner._breadcrumbPanel.Visible = true;
                    }
                }));
            };

            _owner._addressTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    if (Directory.Exists(_owner._addressTextBox.Text) || _owner._addressTextBox.Text == ThisPcPath)
                    {
                        _owner.ObserveTask(_owner.NavigateTo(_owner._addressTextBox.Text), "AddressBar.EnterNavigate");
                        _owner._addressTextBox.Visible = false;
                        _owner._breadcrumbPanel.Visible = true;
                    }
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    _owner._addressTextBox.Visible = false;
                    _owner._breadcrumbPanel.Visible = true;
                    _owner._listView.Focus();
                }
            };

            _owner._breadcrumbPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 45),
                WrapContents = false,
                AutoScroll = false,
                Cursor = Cursors.IBeam,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            typeof(FlowLayoutPanel).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(_owner._breadcrumbPanel, true);
            _owner._breadcrumbPanel.Click += (s, e) => EnableAddressEdit();

            var iconLabel = new Label
            {
                Text = "\uD83D\uDCC2",
                Dock = DockStyle.Left,
                Width = _owner.Scale(32),
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(0, 0, 0, _owner.Scale(3)),
                Margin = new Padding(0),
                ForeColor = ForeColor_Dark,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 12)
            };
            iconLabel.Click += (s, e) => EnableAddressEdit();

            var contentWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = _owner.Scale(new Padding(0, 2, 0, 0)),
                BackColor = Color.FromArgb(45, 45, 45)
            };
            contentWrapper.Controls.Add(_owner._addressTextBox);
            contentWrapper.Controls.Add(_owner._breadcrumbPanel);
            contentWrapper.Click += (s, e) => EnableAddressEdit();

            var panel = new Panel
            {
                BackColor = Color.FromArgb(45, 45, 45),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = _owner.Scale(new Padding(2, 0, 2, 0))
            };

            panel.Controls.Add(contentWrapper);
            panel.Controls.Add(iconLabel);
            return panel;
        }

        public void EnableAddressEdit()
        {
            if (MainForm.IsShellPath(_owner._currentPath))
            {
                _owner._statusLabel.Text = Localization.T("status_address_unavailable");
                return;
            }

            _owner._breadcrumbPanel.Visible = false;
            _owner._addressTextBox.Visible = true;
            _owner._addressTextBox.Text = _owner._currentPath;
            _owner._addressTextBox.Focus();
            _owner._addressTextBox.SelectAll();
        }

        public void UpdateBreadcrumbs(string path)
        {
            _owner._breadcrumbPanel.SuspendLayout();
            _owner._breadcrumbPanel.Controls.Clear();

            if (MainForm.IsShellPath(path))
            {
                AddBreadcrumb(Localization.T("this_pc"), ThisPcPath);
                AddSeparator();
                AddBreadcrumb(_owner.GetShellDisplayName(path), path);
                _owner._breadcrumbPanel.ResumeLayout();
                return;
            }

            if (path == ThisPcPath)
            {
                AddBreadcrumb(Localization.T("this_pc"), ThisPcPath);
            }
            else
            {
                string root = Path.GetPathRoot(path) ?? "";
                string currentBuildingPath = "";

                if (!string.IsNullOrEmpty(root))
                {
                    AddBreadcrumb(root, root);
                    currentBuildingPath = root;
                }

                string relative = path.Substring(root.Length);
                string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts)
                {
                    AddSeparator();
                    currentBuildingPath = Path.Combine(currentBuildingPath, part);
                    AddBreadcrumb(part, currentBuildingPath);
                }
            }

            _owner._breadcrumbPanel.ResumeLayout();
        }

        public void AddBreadcrumb(string text, string targetPath)
        {
            var btn = new Label
            {
                Text = text,
                AutoSize = true,
                Padding = new Padding(_owner.Scale(4), 0, _owner.Scale(4), 0),
                Margin = new Padding(0),
                ForeColor = Color.FromArgb(220, 220, 220),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft,
                Height = _owner.Scale(30),
                Font = new Font("Segoe UI", 11f)
            };

            btn.MouseEnter += (s, e) => btn.ForeColor = AccentColor;
            btn.MouseLeave += (s, e) => btn.ForeColor = Color.FromArgb(220, 220, 220);
            btn.Click += (s, e) => _owner.ObserveTask(_owner.NavigateTo(targetPath), "AddressBar.BreadcrumbNavigate");

            _owner._breadcrumbPanel.Controls.Add(btn);
        }

        public void AddSeparator()
        {
            var sep = new BreadcrumbSeparator
            {
                Text = "\u203A",
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Segoe UI", 13f),
                Size = new Size(_owner.Scale(18), _owner.Scale(32)),
                Margin = new Padding(0),
                YOffset = -_owner.Scale(7)
            };
            _owner._breadcrumbPanel.Controls.Add(sep);
        }
    }
}
