using System.Windows.Forms;

namespace SpeedExplorer;

internal interface ISettingsLauncherHost
{
    Form OwnerWindow { get; }
    void ApplySettings();
    void ReloadHotkeys();
}
