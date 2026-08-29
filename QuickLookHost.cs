using System.Windows.Forms;

namespace SpeedExplorer;

internal interface IQuickLookHost
{
    BrowserState BrowserState { get; }
    Form OwnerWindow { get; }
    string GetSelectedPath();
    bool TryGetQuickLookBinding(out Keys keys);
}
