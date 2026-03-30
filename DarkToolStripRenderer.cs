using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

// Dark theme renderer for toolbar
public class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    public DarkToolStripRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Color.White;
        base.OnRenderItemText(e);
    }
}

public class DarkColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(60, 60, 60);
    public override Color MenuItemSelected => Color.FromArgb(60, 60, 60);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 60);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 60);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(80, 80, 80);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(80, 80, 80);
    public override Color ImageMarginGradientBegin => Color.FromArgb(30, 30, 30);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(30, 30, 30);
    public override Color ImageMarginGradientEnd => Color.FromArgb(30, 30, 30);
    public override Color SeparatorDark => Color.FromArgb(60, 60, 60);
    public override Color SeparatorLight => Color.Transparent;
    public override Color ToolStripDropDownBackground => Color.FromArgb(30, 30, 30);
}

public class LightToolStripRenderer : ToolStripProfessionalRenderer
{
    public LightToolStripRenderer() : base(new LightColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Color.FromArgb(32, 32, 32);
        base.OnRenderItemText(e);
    }
}

public class LightColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(198, 200, 206);
    public override Color MenuItemSelected => Color.FromArgb(229, 236, 245);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(229, 236, 245);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(229, 236, 245);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(214, 222, 232);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(214, 222, 232);
    public override Color ImageMarginGradientBegin => Color.FromArgb(244, 244, 247);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(244, 244, 247);
    public override Color ImageMarginGradientEnd => Color.FromArgb(244, 244, 247);
    public override Color SeparatorDark => Color.FromArgb(198, 200, 206);
    public override Color SeparatorLight => Color.Transparent;
    public override Color ToolStripDropDownBackground => Color.FromArgb(250, 250, 252);
}
