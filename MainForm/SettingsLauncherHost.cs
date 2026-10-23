using System.Drawing;
using System.Windows.Forms;

namespace SpeedExplorer;

internal interface ISettingsLauncherHost
{
    Form OwnerWindow { get; }
    bool IsSettingsPersistentChromePoint(Point screenPoint);
    Rectangle GetSettingsDimOverlayBounds(Rectangle ownerClientBounds);
    Rectangle GetSettingsTabAreaBounds();
    void ApplySettings(bool refreshCurrent = true, bool applyTheme = true);
    void ReloadHotkeys();
}
