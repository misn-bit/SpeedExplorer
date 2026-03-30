using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class ThemeController
    {
        internal sealed class Palette
        {
            public required Color WindowBackground { get; init; }
            public required Color WindowBorder { get; init; }
            public required Color PanelBackground { get; init; }
            public required Color TitleBarBackground { get; init; }
            public required Color ControlBackground { get; init; }
            public required Color ControlHoverBackground { get; init; }
            public required Color SidebarBackground { get; init; }
            public required Color ListBackground { get; init; }
            public required Color ActiveTabBackground { get; init; }
            public required Color InactiveTabBackground { get; init; }
            public required Color Foreground { get; init; }
            public required Color ForegroundSecondary { get; init; }
            public required Color ForegroundMuted { get; init; }
            public required Color Accent { get; init; }
            public required Color SelectionFocused { get; init; }
            public required Color SelectionUnfocused { get; init; }
            public required Color HoverBackground { get; init; }
            public required Color DropTargetBackground { get; init; }
            public required Color HeaderBackground { get; init; }
            public required Color BorderStrong { get; init; }
            public required Color BorderSoft { get; init; }
            public required Color TagBackground { get; init; }
            public required Color TagBackgroundSelected { get; init; }
            public required Color TagForeground { get; init; }
            public required Color SidebarGhostOverlay { get; init; }
        }

        private static readonly Palette DarkPalette = new()
        {
            WindowBackground = Color.FromArgb(45, 45, 48),
            WindowBorder = Color.FromArgb(45, 45, 48),
            PanelBackground = Color.FromArgb(30, 30, 30),
            TitleBarBackground = Color.FromArgb(32, 32, 32),
            ControlBackground = Color.FromArgb(45, 45, 45),
            ControlHoverBackground = Color.FromArgb(60, 60, 60),
            SidebarBackground = Color.FromArgb(37, 37, 38),
            ListBackground = Color.FromArgb(25, 25, 25),
            ActiveTabBackground = Color.FromArgb(55, 55, 55),
            InactiveTabBackground = Color.FromArgb(40, 40, 40),
            Foreground = Color.FromArgb(240, 240, 240),
            ForegroundSecondary = Color.FromArgb(220, 220, 220),
            ForegroundMuted = Color.FromArgb(180, 180, 180),
            Accent = Color.FromArgb(0, 120, 212),
            SelectionFocused = Color.FromArgb(0, 120, 212),
            SelectionUnfocused = Color.FromArgb(60, 60, 60),
            HoverBackground = Color.FromArgb(60, 60, 60),
            DropTargetBackground = Color.FromArgb(0, 90, 160),
            HeaderBackground = Color.FromArgb(45, 45, 45),
            BorderStrong = Color.FromArgb(80, 80, 80),
            BorderSoft = Color.FromArgb(140, 140, 140),
            TagBackground = Color.FromArgb(60, 60, 60),
            TagBackgroundSelected = Color.FromArgb(90, 90, 90),
            TagForeground = Color.FromArgb(200, 200, 200),
            SidebarGhostOverlay = Color.FromArgb(80, 255, 255, 255)
        };

        private static readonly Palette LightPalette = new()
        {
            WindowBackground = Color.FromArgb(244, 244, 247),
            WindowBorder = Color.FromArgb(214, 214, 220),
            PanelBackground = Color.FromArgb(238, 238, 242),
            TitleBarBackground = Color.FromArgb(232, 232, 236),
            ControlBackground = Color.FromArgb(250, 250, 252),
            ControlHoverBackground = Color.FromArgb(208, 218, 231),
            SidebarBackground = Color.FromArgb(240, 242, 246),
            ListBackground = Color.FromArgb(255, 255, 255),
            ActiveTabBackground = Color.FromArgb(255, 255, 255),
            InactiveTabBackground = Color.FromArgb(233, 235, 239),
            Foreground = Color.FromArgb(32, 32, 32),
            ForegroundSecondary = Color.FromArgb(60, 60, 60),
            ForegroundMuted = Color.FromArgb(110, 110, 110),
            Accent = Color.FromArgb(0, 120, 212),
            SelectionFocused = Color.FromArgb(92, 154, 222),
            SelectionUnfocused = Color.FromArgb(204, 216, 232),
            HoverBackground = Color.FromArgb(216, 227, 240),
            DropTargetBackground = Color.FromArgb(186, 212, 240),
            HeaderBackground = Color.FromArgb(238, 240, 244),
            BorderStrong = Color.FromArgb(198, 200, 206),
            BorderSoft = Color.FromArgb(166, 170, 178),
            TagBackground = Color.FromArgb(226, 231, 238),
            TagBackgroundSelected = Color.FromArgb(241, 246, 252),
            TagForeground = Color.FromArgb(72, 72, 72),
            SidebarGhostOverlay = Color.FromArgb(40, 0, 0, 0)
        };

        private readonly MainForm _owner;

        public ThemeController(MainForm owner)
        {
            _owner = owner;
        }

        public Palette CurrentPalette =>
            string.Equals(AppSettings.Current.MainThemePreset, "Light", StringComparison.OrdinalIgnoreCase)
                ? LightPalette
                : DarkPalette;

        public bool IsDarkTheme =>
            !string.Equals(AppSettings.Current.MainThemePreset, "Light", StringComparison.OrdinalIgnoreCase);

        public void ApplyMainWindowTheme()
        {
            var theme = CurrentPalette;

            _owner.BackColor = theme.WindowBackground;
            _owner.Padding = _owner.Scale(new Padding(2));
            _owner.ForeColor = theme.Foreground;
            _owner.DoubleBuffered = true;

            ApplyControlTheme(_owner._titleBar, theme.TitleBarBackground, theme.Foreground);
            ApplyControlTheme(_owner._tabStrip, theme.TitleBarBackground, theme.Foreground);
            ApplyControlTheme(_owner._windowButtonsPanel, theme.TitleBarBackground, theme.Foreground);
            ApplyControlTheme(_owner._navPanel, theme.PanelBackground, theme.Foreground);
            ApplyControlTheme(_owner._navButtonsPanel, theme.PanelBackground, theme.Foreground);
            ApplyControlTheme(_owner._searchControl, theme.ControlBackground, theme.Foreground);
            ApplyControlTheme(_owner._addressBar, theme.ControlBackground, theme.Foreground);
            ApplyControlTheme(_owner._sidebar, theme.SidebarBackground, theme.Foreground);
            ApplyControlTheme(_owner._listView, theme.ListBackground, theme.Foreground);
            ApplyControlTheme(_owner._statusBar, theme.PanelBackground, theme.Foreground);
            RecolorContainer(_owner._navPanel, theme.PanelBackground, theme.Foreground);
            RecolorContainer(_owner._navButtonsPanel, theme.PanelBackground, theme.Foreground);
            RecolorContainer(_owner._windowButtonsPanel, theme.TitleBarBackground, theme.Foreground);

            if (_owner._splitContainer != null)
            {
                _owner._splitContainer.BackColor = theme.PanelBackground;
                _owner._splitContainer.Panel1.BackColor = theme.SidebarBackground;
                _owner._splitContainer.Panel2.BackColor = theme.ListBackground;
            }

            if (_owner._pathLabel != null)
                _owner._pathLabel.ForeColor = theme.ForegroundSecondary;
            if (_owner._statusLabel != null)
                _owner._statusLabel.ForeColor = theme.ForegroundSecondary;
            if (_owner._viewToggleLabel != null)
            {
                _owner._viewToggleLabel.ForeColor = theme.ForegroundMuted;
                _owner._viewToggleLabel.BackColor = Color.Transparent;
            }

            ApplyButtonTheme(_owner._backBtn, theme, theme.PanelBackground);
            ApplyButtonTheme(_owner._fwdBtn, theme, theme.PanelBackground);
            ApplyButtonTheme(_owner._upBtn, theme, theme.PanelBackground);
            ApplyButtonTheme(_owner._refreshBtn, theme, theme.PanelBackground);
            ApplyButtonTheme(_owner._settingsBtn, theme, theme.PanelBackground);
            ApplyButtonTheme(_owner._windowMinButton, theme, theme.TitleBarBackground);
            ApplyButtonTheme(_owner._windowMaxButton, theme, theme.TitleBarBackground);
            ApplyButtonTheme(_owner._windowCloseButton, theme, theme.TitleBarBackground, preserveCloseHover: true);

            if (_owner._searchBox != null)
            {
                _owner._searchBox.BackColor = theme.ControlBackground;
                if (_owner._searchBox.Text == Localization.T("search_placeholder"))
                    _owner._searchBox.ForeColor = Color.Gray;
                else
                    _owner._searchBox.ForeColor = theme.Foreground;
            }

            if (_owner._searchTagToggleBtn != null)
            {
                _owner._searchTagToggleBtn.FlatAppearance.MouseOverBackColor = theme.ControlHoverBackground;
                _owner._searchTagToggleBtn.FlatAppearance.MouseDownBackColor = theme.ControlHoverBackground;
            }

            if (_owner._searchClearBtn != null)
            {
                _owner._searchClearBtn.BackColor = theme.ControlBackground;
                _owner._searchClearBtn.ForeColor = theme.ForegroundMuted;
                _owner._searchClearBtn.FlatAppearance.MouseOverBackColor = theme.ControlHoverBackground;
                _owner._searchClearBtn.FlatAppearance.MouseDownBackColor = theme.ControlHoverBackground;
            }

            if (_owner._addressTextBox != null)
            {
                _owner._addressTextBox.BackColor = theme.ControlBackground;
                _owner._addressTextBox.ForeColor = theme.Foreground;
            }

            if (_owner._breadcrumbPanel != null)
            {
                _owner._breadcrumbPanel.BackColor = theme.ControlBackground;
                foreach (Control control in _owner._breadcrumbPanel.Controls)
                    control.ForeColor = control.Text == "\u203A" ? theme.ForegroundMuted : theme.ForegroundSecondary;
            }

            RecolorContainer(_owner._searchControl, theme.ControlBackground, theme.Foreground);
            RecolorContainer(_owner._addressBar, theme.ControlBackground, theme.Foreground);

            if (_owner._titleBar != null)
            {
                foreach (Control control in _owner._titleBar.Controls)
                    control.BackColor = theme.TitleBarBackground;
            }

            _owner._contextMenuController?.ApplyTheme();
            if (_owner._listView != null && _owner._listView.IsHandleCreated)
                _owner._listViewController.EnsureHeaderTail();
            if (_owner._listView != null && _owner._listView.IsHandleCreated)
            {
                int darkMode = IsDarkTheme ? 1 : 0;
                SetWindowTheme(_owner._listView.Handle, IsDarkTheme ? "DarkMode_Explorer" : "Explorer", null);
                DwmSetWindowAttribute(_owner._listView.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
            if (_owner._sidebar != null && _owner._sidebar.IsHandleCreated)
            {
                int darkMode = IsDarkTheme ? 1 : 0;
                SetWindowTheme(_owner._sidebar.Handle, IsDarkTheme ? "DarkMode_Explorer" : "Explorer", null);
                DwmSetWindowAttribute(_owner._sidebar.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }

            _owner.UpdateSearchTagToggleButtonState();
            _owner._tabsController.UpdateTabStripVisuals();

            _owner._searchBox?.Invalidate();
            _owner._sidebar?.Invalidate();
            _owner._listView?.Invalidate();
            _owner._tabStrip?.Invalidate();
            _owner._titleBar?.Invalidate();
            _owner._statusBar?.Invalidate();
            _owner.RefreshFrame();
        }

        private static void ApplyControlTheme(Control? control, Color backColor, Color foreColor)
        {
            if (control == null || control.IsDisposed)
                return;

            control.BackColor = backColor;
            control.ForeColor = foreColor;
        }

        private static void ApplyButtonTheme(Button? button, Palette theme, Color backColor, bool preserveCloseHover = false)
        {
            if (button == null || button.IsDisposed)
                return;

            button.UseVisualStyleBackColor = false;
            button.BackColor = backColor;
            button.ForeColor = theme.Foreground;
            if (!preserveCloseHover)
                button.FlatAppearance.MouseOverBackColor = theme.ControlHoverBackground;
        }

        private static void RecolorContainer(Control? parent, Color backColor, Color foreColor)
        {
            if (parent == null || parent.IsDisposed)
                return;

            foreach (Control control in parent.Controls)
            {
                if (control is TextBox or FlowLayoutPanel or Panel or Label or Button)
                {
                    if (!(control is Label && control.BackColor == Color.Transparent))
                        control.BackColor = backColor;
                    control.ForeColor = foreColor;
                }
                RecolorContainer(control, backColor, foreColor);
            }
        }
    }
}
