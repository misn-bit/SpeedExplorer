using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SpeedExplorer;

public partial class MainForm
{
    private static string TraceText(string? value)
        => (value ?? "").Replace("\r", "\\r").Replace("\n", "\\n");

    private static (int Gen0, int Gen1, int Gen2, long MemoryBytes) CaptureGcSnapshot()
        => (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalMemory(false));

    private static void LogGcDelta(long navTraceId, string scope, (int Gen0, int Gen1, int Gen2, long MemoryBytes)? startSnapshot)
    {
        if (!AppSettings.Current.DebugNavigationLogging || !AppSettings.Current.DebugNavigationGcStats || startSnapshot == null) return;
        var end = CaptureGcSnapshot();
        var start = startSnapshot.Value;
        NavigationDebugLogger.Log(
            $"{scope}#{navTraceId} GC gen0Delta={end.Gen0 - start.Gen0} gen1Delta={end.Gen1 - start.Gen1} gen2Delta={end.Gen2 - start.Gen2} memDelta={end.MemoryBytes - start.MemoryBytes} memNow={end.MemoryBytes}");
    }

    private void LogUiQueueDelayAsync(long navTraceId, string scope, string stage)
    {
        if (!AppSettings.Current.DebugNavigationLogging || !AppSettings.Current.DebugNavigationUiQueue) return;
        if (!IsHandleCreated || IsDisposed) return;

        try
        {
            var sw = Stopwatch.StartNew();
            BeginInvoke(new Action(() =>
            {
                NavigationDebugLogger.Log($"{scope}#{navTraceId} UIQ stage={stage} delayMs={sw.ElapsedMilliseconds}");
            }));
        }
        catch
        {
        }
    }

    private void StartPostBindProbe(long navTraceId, string scope)
    {
        if (!AppSettings.Current.DebugNavigationLogging || !AppSettings.Current.DebugNavigationPostBind || _iconLoadService == null) return;

        int pendingStart = _iconLoadService.PendingCount;
        int queueStart = _iconLoadService.QueueCount;
        if (pendingStart <= 0 && queueStart <= 0)
        {
            NavigationDebugLogger.Log($"{scope}#{navTraceId} POSTBIND idle pending=0 queue=0");
            return;
        }

        _ = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            const int timeoutMs = 10000;
            int stableZeroTicks = 0;

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                int pending = _iconLoadService.PendingCount;
                int queue = _iconLoadService.QueueCount;
                if (pending == 0 && queue == 0)
                {
                    stableZeroTicks++;
                    if (stableZeroTicks >= 2)
                    {
                        NavigationDebugLogger.Log($"{scope}#{navTraceId} POSTBIND done ms={sw.ElapsedMilliseconds} pending0={pendingStart} queue0={queueStart}");
                        return;
                    }
                }
                else
                {
                    stableZeroTicks = 0;
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            NavigationDebugLogger.Log(
                $"{scope}#{navTraceId} POSTBIND timeout ms={sw.ElapsedMilliseconds} pending0={pendingStart} queue0={queueStart} pending={_iconLoadService.PendingCount} queue={_iconLoadService.QueueCount}");
        });
    }

    private void LogListViewState(string scope, string stage)
    {
        if (!AppSettings.Current.DebugNavigationLogging || _listView == null)
            return;

        int top = -1;
        int perPage = -1;
        int firstSel = -1;
        bool handle = _listView.IsHandleCreated;

        try
        {
            if (handle)
            {
                const int LVM_GETTOPINDEX = 0x1027;
                const int LVM_GETCOUNTPERPAGE = 0x1028;
                top = SendMessage(_listView.Handle, LVM_GETTOPINDEX, 0, 0);
                perPage = SendMessage(_listView.Handle, LVM_GETCOUNTPERPAGE, 0, 0);
            }
        }
        catch { }

        try
        {
            if (_listView.SelectedIndices.Count > 0)
                firstSel = _listView.SelectedIndices[0];
        }
        catch { }

        NavigationDebugLogger.Log(
            $"{scope} LV stage={stage} view={_listView.View} ownerDraw={_listView.OwnerDraw} virtual={_listView.VirtualMode} " +
            $"vsize={_listView.VirtualListSize} items={_listView.Items.Count} top={top} perPage={perPage} " +
            $"selCount={_listView.SelectedIndices.Count} firstSel={firstSel} focused={_listView.Focused} " +
            $"search={IsSearchMode}/{_searchController.IsSearchInProgress} path=\"{TraceText(_currentPath)}\"");
    }

    internal void ObserveTask(Task task, string source)
    {
        if (task == null)
            return;

        task.ContinueWith(t =>
        {
            try
            {
                var ex = t.Exception?.GetBaseException();
                if (ex != null)
                    NavigationDebugLogger.Log($"{source} TASK_ERROR \"{TraceText(ex.Message)}\"");
            }
            catch
            {
            }
        }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }
}
