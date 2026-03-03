using System.Drawing;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class ThemeController
    {
        private readonly MainForm _owner;

        public ThemeController(MainForm owner)
        {
            _owner = owner;
        }

        public Color BackColorDark => Color.FromArgb(30, 30, 30);
        public Color ForeColorDark => Color.FromArgb(240, 240, 240);
        public Color AccentColor => Color.FromArgb(0, 120, 212);
        public Color SidebarColor => Color.FromArgb(37, 37, 38);
        public Color ListBackColor => Color.FromArgb(25, 25, 25);
        public Color TitleBarColor => Color.FromArgb(32, 32, 32);
        public Color TagColor => Color.FromArgb(60, 60, 60);
        public Color TagForeColor => Color.FromArgb(200, 200, 200);

        public void ApplyMainWindowTheme()
        {
            _owner.BackColor = Color.FromArgb(45, 45, 48);
            _owner.Padding = _owner.Scale(new Padding(2));
            _owner.ForeColor = ForeColorDark;
            _owner.DoubleBuffered = true;
        }
    }
}
