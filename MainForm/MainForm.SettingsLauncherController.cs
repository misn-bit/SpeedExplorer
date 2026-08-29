using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    Form ISettingsLauncherHost.OwnerWindow => this;
    void ISettingsLauncherHost.ApplySettings() => ApplySettings();
    void ISettingsLauncherHost.ReloadHotkeys() => _hotkeyController.Reload();

    private sealed class SettingsLauncherController
    {
        private readonly ISettingsLauncherHost _owner;

        public SettingsLauncherController(ISettingsLauncherHost owner)
        {
            _owner = owner;
        }

        public void OpenSettings()
        {
            using var form = new SettingsForm();
            if (form.ShowDialog(_owner.OwnerWindow) == DialogResult.OK)
            {
                _owner.ApplySettings();
                _owner.ReloadHotkeys();
            }
        }
    }
}
