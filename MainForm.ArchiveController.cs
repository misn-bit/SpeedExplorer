using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    private sealed class ArchiveController
    {
        private readonly MainForm _owner;

        public ArchiveController(MainForm owner)
        {
            _owner = owner;
        }

        public void CreateNewWinRarArchive()
        {
            if (string.IsNullOrEmpty(_owner._currentPath) || _owner._currentPath == ThisPcPath) return;

            try
            {
                string name = _owner.GenerateUniqueName(_owner._currentPath, "New WinRAR Archive", ".rar");
                string fullPath = Path.Combine(_owner._currentPath, name);
                File.WriteAllBytes(fullPath, Array.Empty<byte>());
                _owner.StartRenameAfterCreation(fullPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create archive: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ExtractZipHere()
        {
            var paths = _owner.GetSelectedPaths();
            if (paths.Length != 1) return;
            string zipPath = paths[0];
            if (!IsZipFilePath(zipPath)) return;
            if (string.IsNullOrEmpty(_owner._currentPath) || _owner._currentPath == ThisPcPath) return;

            if (MessageBox.Show("Files may be overwritten. Continue?", "Extract Here", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            try
            {
                ZipFile.ExtractToDirectory(zipPath, _owner._currentPath, overwriteFiles: true);
                _ = _owner.RefreshCurrentAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to extract archive: {ex.Message}", "Extract Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ExtractZipToFolder()
        {
            var paths = _owner.GetSelectedPaths();
            if (paths.Length != 1) return;
            string zipPath = paths[0];
            if (!IsZipFilePath(zipPath)) return;
            if (string.IsNullOrEmpty(_owner._currentPath) || _owner._currentPath == ThisPcPath) return;

            using var fbd = new FolderBrowserDialog
            {
                Description = "Select destination folder",
                UseDescriptionForTitle = true,
                SelectedPath = _owner._currentPath,
                ShowNewFolderButton = true
            };
            if (fbd.ShowDialog(_owner) != DialogResult.OK) return;
            string destination = fbd.SelectedPath;

            try
            {
                ExtractZipWithProgress(zipPath, destination);
                _ = _owner.RefreshCurrentAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to extract archive: {ex.Message}", "Extract Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void CreateZipFromSelection()
        {
            var paths = _owner.GetSelectedPaths();
            if (paths.Length == 0) return;
            if (string.IsNullOrEmpty(_owner._currentPath) || _owner._currentPath == ThisPcPath) return;

            string archiveName = paths.Length == 1 ? Path.GetFileNameWithoutExtension(paths[0]) : Path.GetFileName(_owner._currentPath);
            if (string.IsNullOrEmpty(archiveName) || archiveName == ":") archiveName = "archive";

            using var sfd = new SaveFileDialog
            {
                Title = "Create ZIP Archive",
                Filter = "ZIP Archive (*.zip)|*.zip",
                DefaultExt = "zip",
                AddExtension = true,
                FileName = $"{archiveName}.zip",
                InitialDirectory = _owner._currentPath,
                OverwritePrompt = true
            };

            if (sfd.ShowDialog(_owner) != DialogResult.OK) return;

            try
            {
                CreateZipWithProgress(paths, sfd.FileName, CompressionLevel.Optimal);
                _ = _owner.RefreshCurrentAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create archive: {ex.Message}", "Archive Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ExtractZipWithProgress(string zipPath, string destination)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            int total = zip.Entries.Count;
            int index = 0;

            foreach (var entry in zip.Entries)
            {
                index++;
                _owner._statusLabel.Text = string.Format(Localization.T("status_extracting"), index, total, entry.FullName);
                if (index % 8 == 0)
                {
                    try { _owner._statusBar?.Invalidate(); _owner._statusBar?.Update(); } catch { }
                }

                string targetPath = Path.Combine(destination, entry.FullName);
                string? targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                entry.ExtractToFile(targetPath, overwrite: true);
            }
        }

        public void CreateZipWithProgress(string[] paths, string outputZipPath, CompressionLevel level)
        {
            var allFiles = new List<(string FilePath, string EntryName)>();

            foreach (var p in paths)
            {
                if (File.Exists(p))
                {
                    allFiles.Add((p, Path.GetFileName(p)));
                }
                else if (Directory.Exists(p))
                {
                    string rootName = Path.GetFileName(p);
                    foreach (var file in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                    {
                        string relativePath = Path.GetRelativePath(p, file);
                        string entryName = Path.Combine(rootName, relativePath).Replace('\\', '/');
                        allFiles.Add((file, entryName));
                    }
                }
            }

            using var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);
            int total = allFiles.Count;
            int index = 0;
            foreach (var (filePath, entryName) in allFiles)
            {
                index++;
                _owner._statusLabel.Text = string.Format(Localization.T("status_compressing"), index, total, entryName);
                if (index % 8 == 0)
                {
                    try { _owner._statusBar?.Invalidate(); _owner._statusBar?.Update(); } catch { }
                }
                zip.CreateEntryFromFile(filePath, entryName, level);
            }
        }

        public string GetWinRarPath()
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinRAR.exe"))
            {
                if (key != null)
                {
                    var val = key.GetValue("") as string;
                    if (!string.IsNullOrEmpty(val) && File.Exists(val)) return val;
                }
            }

            using (var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"WinRAR\shell\open\command"))
            {
                if (key != null)
                {
                    var val = key.GetValue("") as string;
                    if (!string.IsNullOrEmpty(val))
                    {
                        int firstQuote = val.IndexOf('"');
                        int secondQuote = val.IndexOf('"', firstQuote + 1);
                        if (firstQuote >= 0 && secondQuote > firstQuote)
                        {
                            var path = val.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                            if (File.Exists(path)) return path;
                        }
                    }
                }
            }

            string fallback = @"C:\Program Files\WinRAR\WinRAR.exe";
            if (File.Exists(fallback)) return fallback;

            return "";
        }

        public void ExecuteWinRarAddPrompt(string[] paths)
        {
            if (paths.Length == 0) return;
            if (string.IsNullOrEmpty(_owner._currentPath) || _owner._currentPath == ThisPcPath) return;

            string defaultName = paths.Length == 1
                ? Path.GetFileNameWithoutExtension(paths[0])
                : Path.GetFileName(_owner._currentPath);
            if (string.IsNullOrEmpty(defaultName) || defaultName == ":")
                defaultName = "archive";

            string? archiveBaseName = PromptArchiveBaseName(defaultName);
            if (string.IsNullOrEmpty(archiveBaseName)) return;

            ExecuteWinRarAdd(paths, archiveBaseName);
        }

        public void ExecuteWinRarExtractHere()
        {
            var paths = _owner.GetSelectedPaths();
            if (paths.Length != 1) return;
            string archivePath = paths[0];
            if (!IsArchiveFilePath(archivePath)) return;
            if (string.IsNullOrEmpty(_owner._currentPath) || _owner._currentPath == ThisPcPath) return;

            string winRarPath = GetWinRarPath();
            if (string.IsNullOrEmpty(winRarPath))
            {
                MessageBox.Show("WinRAR not found on this system.", "WinRAR Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string baseName = Path.GetFileNameWithoutExtension(archivePath);
            string destination = Path.Combine(_owner._currentPath, baseName);
            if (Directory.Exists(destination))
            {
                var result = MessageBox.Show(
                    "Destination folder already exists.\n\nYes: Extract into existing folder\nNo: Create a new folder\nCancel: Abort",
                    "Extract Here",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel) return;
                if (result == DialogResult.No)
                {
                    string uniqueName = _owner.GenerateUniqueName(_owner._currentPath, baseName, "");
                    destination = Path.Combine(_owner._currentPath, uniqueName);
                }
            }

            Directory.CreateDirectory(destination);

            try
            {
                string args = $"x \"{archivePath}\" \"{destination}\\\"";
                Process.Start(new ProcessStartInfo(winRarPath, args)
                {
                    WorkingDirectory = _owner._currentPath,
                    UseShellExecute = true
                });
                ScheduleRefresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to extract archive: {ex.Message}", "WinRAR Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ExecuteWinRarExtractTo()
        {
            var paths = _owner.GetSelectedPaths();
            if (paths.Length != 1) return;
            string archivePath = paths[0];
            if (!IsArchiveFilePath(archivePath)) return;

            using var fbd = new FolderBrowserDialog
            {
                Description = "Select destination folder",
                UseDescriptionForTitle = true,
                SelectedPath = _owner._currentPath,
                ShowNewFolderButton = true
            };
            if (fbd.ShowDialog(_owner) != DialogResult.OK) return;
            string destination = fbd.SelectedPath;

            string winRarPath = GetWinRarPath();
            if (string.IsNullOrEmpty(winRarPath))
            {
                MessageBox.Show("WinRAR not found on this system.", "WinRAR Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                string args = $"x \"{archivePath}\" \"{destination}\\\"";
                Process.Start(new ProcessStartInfo(winRarPath, args)
                {
                    WorkingDirectory = destination,
                    UseShellExecute = true
                });
                ScheduleRefresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to extract archive: {ex.Message}", "WinRAR Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExecuteWinRarAdd(string[] paths, string archiveBaseName)
        {
            if (paths.Length == 0) return;
            string winRarPath = GetWinRarPath();
            if (string.IsNullOrEmpty(winRarPath))
            {
                MessageBox.Show("WinRAR not found on this system.", "WinRAR Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                string archivePath = Path.Combine(_owner._currentPath, $"{archiveBaseName}.rar");
                if (File.Exists(archivePath))
                {
                    var overwriteResult = MessageBox.Show(
                        Localization.T("archive_overwrite_prompt"),
                        Localization.T("add_to_archive"),
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Warning);
                    if (overwriteResult == DialogResult.Cancel) return;
                    if (overwriteResult == DialogResult.No)
                    {
                        string uniqueName = _owner.GenerateUniqueName(_owner._currentPath, archiveBaseName, ".rar");
                        archivePath = Path.Combine(_owner._currentPath, uniqueName);
                    }
                }

                string args = $"a -ep1 -r \"{archivePath}\"";
                foreach (var p in paths)
                    args += $" \"{p}\"";

                Process.Start(new ProcessStartInfo(winRarPath, args)
                {
                    WorkingDirectory = _owner._currentPath,
                    UseShellExecute = true
                });

                ScheduleRefresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to execute WinRAR: {ex.Message}", "WinRAR Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string? PromptArchiveBaseName(string defaultBaseName)
        {
            using var dialog = new Form
            {
                Text = Localization.T("add_to_archive"),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new System.Drawing.Size(_owner.Scale(420), _owner.Scale(130)),
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White
            };

            var promptLabel = new Label
            {
                Text = Localization.T("archive_name_prompt"),
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = _owner.Scale(24),
                Padding = new Padding(_owner.Scale(12), _owner.Scale(10), _owner.Scale(12), 0)
            };

            var input = new TextBox
            {
                Dock = DockStyle.Top,
                Margin = new Padding(_owner.Scale(12)),
                Text = $"{defaultBaseName}.rar",
                BorderStyle = BorderStyle.FixedSingle
            };
            input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                }
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = _owner.Scale(44),
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(_owner.Scale(8))
            };

            var ok = new Button { Text = Localization.T("ok"), DialogResult = DialogResult.OK, Width = _owner.Scale(96) };
            var cancel = new Button { Text = Localization.T("cancel"), DialogResult = DialogResult.Cancel, Width = _owner.Scale(96) };
            buttonPanel.Controls.Add(ok);
            buttonPanel.Controls.Add(cancel);

            dialog.Controls.Add(buttonPanel);
            dialog.Controls.Add(input);
            dialog.Controls.Add(promptLabel);
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;

            if (dialog.ShowDialog(_owner) != DialogResult.OK) return null;

            string typed = input.Text.Trim();
            if (string.IsNullOrEmpty(typed)) return defaultBaseName;

            string fileNameOnly = Path.GetFileName(typed);
            if (fileNameOnly.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                fileNameOnly = fileNameOnly.Substring(0, fileNameOnly.Length - 4);
            if (string.IsNullOrWhiteSpace(fileNameOnly))
                fileNameOnly = defaultBaseName;

            foreach (var c in Path.GetInvalidFileNameChars())
                fileNameOnly = fileNameOnly.Replace(c, '_');

            return fileNameOnly;
        }

        private void ScheduleRefresh()
        {
            _ = Task.Delay(1500).ContinueWith(_ =>
                _owner.BeginInvoke(() => _ = _owner.RefreshCurrentAsync()));
        }
    }
}
