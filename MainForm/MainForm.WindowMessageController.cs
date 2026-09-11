using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    // Resize support for borderless window
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        const int WM_DEVICECHANGE = 0x219;
        const int WM_NCCALCSIZE = 0x83;
        const int WM_WINDOWPOSCHANGED = 0x0047;

        var previousWindowState = m.Msg == WM_WINDOWPOSCHANGED ? WindowState : FormWindowState.Normal;

        if (ShellContextMenuService.HandleMenuMessage(ref m))
            return;

        if (m.Msg == WM_DEVICECHANGE)
        {
            BeginInvoke(() => {
                _sidebarController.PopulateSidebar();
                if (State.CurrentPath == ThisPcPath) LoadDrives();
            });
        }

        if (m.Msg == WM_NCCALCSIZE)
            if (_windowChromeController.HandleNcCalcSize(ref m))
                return;

        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            _windowChromeController.ApplyNcHitTest(ref m);
            return;
        }

        // Prevent Windows from drawing the non-client area (the white border)
        if (m.Msg == 0x0085 /* WM_NCPAINT */ || m.Msg == 0x0086 /* WM_NCACTIVATE */)
        {
            if (_windowChromeController.HandleNcPaintLike(ref m))
                return;
            return;
        }

        base.WndProc(ref m);
        if (m.Msg == WM_WINDOWPOSCHANGED && WindowState != previousWindowState)
            SaveWindowStateForCurrentState();
    }
}
