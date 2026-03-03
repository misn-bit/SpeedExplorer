namespace SpeedExplorer;

public partial class MainForm
{
    private void TogglePinSelected()
        => _selectionOpenController.TogglePinSelected();

    private string GetSelectedPath()
        => _selectionOpenController.GetSelectedPath();

    private void OpenSelectedItem()
        => _selectionOpenController.OpenSelectedItem();

    private string? GetOpenInOtherTargetPath()
        => _openTargetController.GetOpenInOtherTargetPath();

    private string? NormalizeOpenDirectoryPath(string path)
        => _openTargetController.NormalizeOpenDirectoryPath(path);

    private void OpenInOtherTarget()
        => _openTargetController.OpenInOtherTarget();

    private void OpenPathInNewTab(string path, bool activate = true)
        => _openTargetController.OpenPathInNewTab(path, activate);

    private void StartRenameAfterCreation(string newPath)
        => _fileOperationsController.StartRenameAfterCreation(newPath);

    private void StartRename()
        => _fileOperationsController.StartRename();

    private void EndRename(bool commit)
        => _fileOperationsController.EndRename(commit);

    private void CopySelected()
        => _fileOperationsController.CopySelected();

    private void CutSelected()
        => _fileOperationsController.CutSelected();

    private void Paste()
        => _fileOperationsController.Paste();

    private void DeleteSelected(bool permanent)
        => _fileOperationsController.DeleteSelected(permanent);

    private void ShowStatusMessage(string msg)
        => _fileOperationsController.ShowStatusMessage(msg);

    private void PerformClipboardOperation(string[] paths, bool isCut)
        => _fileOperationsController.PerformClipboardOperation(paths, isCut);

    private bool IsClipboardFileContentPresent()
        => _fileOperationsController.IsClipboardFileContentPresent();

    private string[] GetSelectedPaths()
        => _selectionOpenController.GetSelectedPaths();
}
