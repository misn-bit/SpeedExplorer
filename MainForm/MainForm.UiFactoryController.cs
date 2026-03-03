using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private Panel CreateTitleBar()
        => _windowChromeController.CreateTitleBar();

    private Button CreateNavButton(string text, string tooltip)
        => _layoutController.CreateNavButton(text, tooltip);

    private Button CreateWindowButton(string text, string tooltip)
        => _windowChromeController.CreateWindowButton(text, tooltip);

    private void ApplyNavButtonTextOffset(Button btn, int offsetY)
        => _layoutController.ApplyNavButtonTextOffset(btn, offsetY);

    private Panel CreateSearchControl()
        => _layoutController.CreateSearchControl();

    private void InitializeContextMenu()
    {
        _contextMenuController = new ContextMenuController(this);
        _contextMenu = _contextMenuController.Menu;
    }

    private ListView CreateListView()
        => _listViewController.CreateListView();

    private StatusStrip CreateStatusBar()
        => _layoutController.CreateStatusBar();
}
