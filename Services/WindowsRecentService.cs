using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeedExplorer;

internal static class WindowsRecentService
{
    private const int MaxRecentFoldersForJumpList = 12;
    private const int MaxRecentFoldersForSidebar = 12;

    public static List<string> GetRecentFolders(int maxCount = MaxRecentFoldersForSidebar)
    {
        var recentFolders = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var recentPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (string.IsNullOrEmpty(recentPath) || !Directory.Exists(recentPath))
                return recentFolders;

            var links = Directory.EnumerateFiles(recentPath, "*.lnk", SearchOption.TopDirectoryOnly)
                .Select(static p => new FileInfo(p))
                .OrderByDescending(static fi => fi.LastWriteTimeUtc);

            var wshType = Type.GetTypeFromProgID("WScript.Shell");
            if (wshType == null)
                return recentFolders;

            dynamic? wsh = Activator.CreateInstance(wshType);
            if (wsh == null)
                return recentFolders;

            foreach (var link in links)
            {
                try
                {
                    dynamic? shortcut = wsh.CreateShortcut(link.FullName);
                    string? target = shortcut?.TargetPath as string;
                    if (string.IsNullOrWhiteSpace(target)) continue;
                    if (!Directory.Exists(target)) continue;
                    if (!seen.Add(target)) continue;

                    recentFolders.Add(target);
                    if (recentFolders.Count >= maxCount)
                        break;
                }
                catch
                {
                    // Ignore malformed links.
                }
            }
        }
        catch
        {
            // Best-effort only.
        }

        return recentFolders;
    }

    public static void RefreshTaskbarJumpList()
    {
        try
        {
            var folders = GetRecentFolders(MaxRecentFoldersForJumpList);
            var destinationList = (ICustomDestinationList)new CDestinationList();
            Guid objectArrayGuid = typeof(IObjectArray).GUID;
            destinationList.BeginList(out _, ref objectArrayGuid, out _);

            if (folders.Count > 0)
            {
                var collection = (IObjectCollection)new CEnumerableObjectCollection();
                foreach (var folder in folders)
                {
                    collection.AddObject(CreateFolderShellLink(folder));
                }

                destinationList.AppendCategory(Localization.T("recent_folders"), (IObjectArray)collection);
            }

            destinationList.AppendKnownCategory(KnownDestinationCategory.Recent);
            destinationList.CommitList();
        }
        catch
        {
            // Jump list refresh is best-effort.
        }
    }

    private static IShellLinkW CreateFolderShellLink(string folderPath)
    {
        var link = (IShellLinkW)new CShellLink();
        link.SetPath(Application.ExecutablePath);
        link.SetArguments($"\"{folderPath}\"");
        link.SetDescription(folderPath);
        link.SetIconLocation(Application.ExecutablePath, 0);

        if (link is IPropertyStore propertyStore)
        {
            string title = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(title))
                title = folderPath;

            var titleKey = PropertyKeys.Title;
            var titleValue = new PropVariant(title);
            try
            {
                propertyStore.SetValue(ref titleKey, ref titleValue);
                propertyStore.Commit();
            }
            finally
            {
                titleValue.Dispose();
            }
        }

        return link;
    }

    private enum KnownDestinationCategory
    {
        Frequent = 1,
        Recent = 2
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;

        public PropertyKey(Guid format, uint id)
        {
            fmtid = format;
            pid = id;
        }
    }

    private static class PropertyKeys
    {
        public static readonly PropertyKey Title = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant : IDisposable
    {
        private ushort vt;
        private ushort reserved1;
        private ushort reserved2;
        private ushort reserved3;
        private IntPtr pointerValue;
        private int intValue;

        public PropVariant(string value)
        {
            vt = (ushort)VarEnum.VT_LPWSTR;
            reserved1 = 0;
            reserved2 = 0;
            reserved3 = 0;
            pointerValue = Marshal.StringToCoTaskMemUni(value);
            intValue = 0;
        }

        public void Dispose()
        {
            PropVariantClear(ref this);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [ComImport]
    [Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void BeginList(out uint cMinSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, [MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
        void AppendKnownCategory(KnownDestinationCategory category);
        void AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
        void CommitList();
        void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void AbortList();
    }

    [ComImport]
    [Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint cObjects);
        void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    [ComImport]
    [Guid("5632B1A4-E38A-400a-928A-D4CD63230295")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection : IObjectArray
    {
        new void GetCount(out uint cObjects);
        new void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void AddObject([MarshalAs(UnmanagedType.Interface)] object punk);
        void AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray poaSource);
        void RemoveObjectAt(uint uiIndex);
        void Clear();
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6")]
    private class CDestinationList
    {
    }

    [ComImport]
    [Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A")]
    private class CEnumerableObjectCollection
    {
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink
    {
    }
}
