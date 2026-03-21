using System;
using System.Linq;

namespace SpeedExplorer;

public partial class MainForm
{
    private void BatchProcess_Click(object? sender, EventArgs e)
    {
        var selectedPaths = GetSelectedPaths();
        if (selectedPaths.Length == 0) return;

        var form = new BatchProcessingForm(selectedPaths.ToList(), this.Handle);
        form.FormClosed += (_, _) => _ = RefreshCurrentAsync();
        form.Show(this);
    }

    private void ExecuteAction(string action) => _hotkeyController.ExecuteAction(action);

    private void OpenSettings()
        => _settingsLauncherController.OpenSettings();
}
