using System.Windows.Forms;

namespace SpeedExplorer;

internal interface ISettingsLauncherHost
{
    Form OwnerWindow { get; }
    void ApplySettings(bool refreshCurrent = true, bool applyTheme = true);
    void ReloadHotkeys();
}
