using System;
using System.Linq;

namespace SpeedExplorer;

public partial class MainForm
{
    private void BatchProcess_Click(object? sender, EventArgs e)
    {
        var selectedPaths = GetSelectedPaths();
        if (selectedPaths.Length == 0) return;

        using (var form = new BatchProcessingForm(selectedPaths.ToList(), this.Handle))
        {
            form.ShowDialog(this);
            // Refresh file list after batch processing to show new tags/files
            _ = RefreshCurrentAsync();
        }
    }

    private void ExecuteAction(string action) => _hotkeyController.ExecuteAction(action);

    private void OpenSettings()
        => _settingsLauncherController.OpenSettings();
}
