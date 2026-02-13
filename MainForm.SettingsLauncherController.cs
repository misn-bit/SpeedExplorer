using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class SettingsLauncherController
    {
        private readonly MainForm _owner;

        public SettingsLauncherController(MainForm owner)
        {
            _owner = owner;
        }

        public void OpenSettings()
        {
            using var form = new SettingsForm();
            if (form.ShowDialog(_owner) == DialogResult.OK)
            {
                _owner.ApplySettings();
                _owner._hotkeyController.Reload();
            }
        }
    }
}
