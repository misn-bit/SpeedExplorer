using System;
using System.Linq;
using System.Windows.Forms;

namespace SpeedExplorer;

public partial class MainForm
{
    BrowserState IQuickLookHost.BrowserState => State;
    Form IQuickLookHost.OwnerWindow => this;
    string IQuickLookHost.GetSelectedPath() => GetSelectedPath();
    bool IQuickLookHost.TryGetQuickLookBinding(out Keys keys)
        => _hotkeyController.TryGetBinding("QuickLook", out keys);

    private sealed class QuickLookController : IDisposable
    {
        private readonly IQuickLookHost _host;
        private BrowserState State => _host.BrowserState;
        private readonly System.Windows.Forms.Timer _timer;

        private QuickLookForm? _quickLook;
        private bool _isActive;

        public QuickLookController(IQuickLookHost host)
        {
            _host = host;

            _timer = new System.Windows.Forms.Timer { Interval = 50 };
            _timer.Tick += (s, e) =>
            {
                if (_isActive && _host.TryGetQuickLookBinding(out var k))
                {
                    int vk = (int)(k & Keys.KeyCode);
                    if ((GetAsyncKeyState(vk) & 0x8000) == 0) Hide();
                    return;
                }

                if (!_isActive) _timer.Stop();
            };
        }

        public void Show()
        {
            if (_isActive) return;

            var path = _host.GetSelectedPath();
            if (string.IsNullOrEmpty(path)) return;

            var item = State.Items.FirstOrDefault(i => i.FullPath == path);
            if (item == null || !IsPreviewable(item)) return;

            try
            {
                if (_quickLook != null && !_quickLook.IsDisposed)
                {
                    _quickLook.Close();
                    _quickLook.Dispose();
                }

                _quickLook = new QuickLookForm { Owner = _host.OwnerWindow };
                _quickLook.ShowPreview(item);
                _quickLook.Show();
                _isActive = true;
                _timer.Start();
            }
            catch
            {
                _isActive = false;
            }
        }

        public void Hide()
        {
            if (!_isActive) return;
            _isActive = false;
            _timer.Stop();

            if (_quickLook != null && !_quickLook.IsDisposed)
            {
                try { _quickLook.Hide(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
            }
        }

        public void Dispose()
        {
            try { Hide(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
            try { _timer.Dispose(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

            try
            {
                if (_quickLook != null && !_quickLook.IsDisposed)
                {
                    _quickLook.Close();
                    _quickLook.Dispose();
                }
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

            _quickLook = null;
        }

        private static bool IsPreviewable(FileItem item)
        {
            if (item.IsDirectory) return false;
            if (item.IsShellItem) return false;
            if (FileSystemService.IsImageFile(item.FullPath)) return true;
            return FileSystemService.IsLikelyTextFile(item.FullPath);
        }
    }
}
