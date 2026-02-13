using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;

namespace SpeedExplorer;

public static class ShellContextMenuService
{
    private static IContextMenu? _currentMenu;
    private static IContextMenu2? _currentMenu2;
    private static IContextMenu3? _currentMenu3;

    public static bool HandleMenuMessage(ref Message m)
    {
        if (_currentMenu3 != null)
        {
            int res;
            if (_currentMenu3.HandleMenuMsg2(m.Msg, m.WParam, m.LParam, out res) == 0)
            {
                m.Result = (IntPtr)res;
                return true;
            }
        }
        else if (_currentMenu2 != null)
        {
            if (_currentMenu2.HandleMenuMsg(m.Msg, m.WParam, m.LParam) == 0)
                return true;
        }
        return false;
    }

    public static bool ShowShellMenu(IntPtr ownerHwnd, string[] paths, int x, int y)
    {
        if (paths == null || paths.Length == 0) return false;
        if (!AllSameParent(paths)) return false;

        IntPtr menu = IntPtr.Zero;
        IShellFolder? parentFolder = null;
        var pidls = new List<IntPtr>();
        try
        {
            TryEnableDarkMenuTheme();
            foreach (var p in paths)
            {
                IntPtr pidl;
                uint attrs = 0;
                if (SHParseDisplayName(p, IntPtr.Zero, out pidl, 0, ref attrs) != 0 || pidl == IntPtr.Zero)
                    return false;
                pidls.Add(pidl);
            }

            IntPtr parentPidl;
            IntPtr childPidl;
            Guid iidIShellFolder = typeof(IShellFolder).GUID;
            if (SHBindToParent(pidls[0], ref iidIShellFolder, out parentFolder, out parentPidl) != 0)
                return false;

            var apidl = new IntPtr[pidls.Count];
            for (int i = 0; i < pidls.Count; i++)
            {
                SHBindToParent(pidls[i], ref iidIShellFolder, out _, out childPidl);
                apidl[i] = childPidl;
            }

            var iidIContextMenu = typeof(IContextMenu).GUID;
            parentFolder.GetUIObjectOf(ownerHwnd, (uint)apidl.Length, apidl, ref iidIContextMenu, IntPtr.Zero, out var menuObjPtr);
            var menuObj = Marshal.GetObjectForIUnknown(menuObjPtr);
            _currentMenu = (IContextMenu)menuObj;
            _currentMenu2 = menuObj as IContextMenu2;
            _currentMenu3 = menuObj as IContextMenu3;

            menu = CreatePopupMenu();
            _currentMenu.QueryContextMenu(menu, 0, 1, 0x7FFF, CMF.EXPLORE);

            int cmd = TrackPopupMenuEx(menu, TPM.RETURNCMD | TPM.RIGHTBUTTON, x, y, ownerHwnd, IntPtr.Zero);
            if (cmd > 0)
            {
                var invoke = new CMINVOKECOMMANDINFOEX();
                invoke.cbSize = Marshal.SizeOf(invoke);
                invoke.fMask = CMIC.UNICODE;
                invoke.hwnd = ownerHwnd;
                invoke.lpVerb = (IntPtr)(cmd - 1);
                invoke.nShow = SW.SHOWNORMAL;
                _currentMenu.InvokeCommand(ref invoke);
            }
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            foreach (var p in pidls)
                if (p != IntPtr.Zero) ILFree(p);
            if (parentFolder != null) Marshal.ReleaseComObject(parentFolder);
            if (_currentMenu != null) Marshal.ReleaseComObject(_currentMenu);
            _currentMenu = null;
            _currentMenu2 = null;
            _currentMenu3 = null;
        }
    }

    private static void TryEnableDarkMenuTheme()
    {
        try
        {
            IntPtr hUx = LoadLibrary("uxtheme.dll");
            if (hUx == IntPtr.Zero) return;
            try
            {
                IntPtr pSet = GetProcAddress(hUx, (IntPtr)135);
                IntPtr pFlush = GetProcAddress(hUx, (IntPtr)136);
                if (pSet != IntPtr.Zero)
                {
                    var set = Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeDelegate>(pSet);
                    set(PreferredAppMode.AllowDark);
                }
                if (pFlush != IntPtr.Zero)
                {
                    var flush = Marshal.GetDelegateForFunctionPointer<FlushMenuThemesDelegate>(pFlush);
                    flush();
                }
            }
            finally
            {
                FreeLibrary(hUx);
            }
        }
        catch { }
    }

    private static bool AllSameParent(string[] paths)
    {
        string? parent = null;
        foreach (var p in paths)
        {
            string dir = Directory.Exists(p) ? Path.GetDirectoryName(p.TrimEnd(Path.DirectorySeparatorChar)) ?? p : Path.GetDirectoryName(p) ?? "";
            if (parent == null) parent = dir;
            else if (!string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, ref uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IShellFolder ppv, out IntPtr ppidlLast);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hmenu, TPM uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, IntPtr procName);

    private enum PreferredAppMode
    {
        Default,
        AllowDark,
        ForceDark,
        ForceLight,
        Max
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate PreferredAppMode SetPreferredAppModeDelegate(PreferredAppMode appMode);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void FlushMenuThemesDelegate();

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        void EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);
        void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl, ref uint rgfInOut);
        void GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint uFlags, out STRRET pName);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STRRET
    {
        public uint uType;
        public IntPtr pOleStr;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, CMF uFlags);
        void InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        void GetCommandString(int idcmd, uint uflags, int reserved, IntPtr commandstring, int cch);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F4-0000-0000-C000-000000000046")]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, CMF uFlags);
        void InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        void GetCommandString(int idcmd, uint uflags, int reserved, IntPtr commandstring, int cch);
        [PreserveSig] int HandleMenuMsg(int uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, CMF uFlags);
        void InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        void GetCommandString(int idcmd, uint uflags, int reserved, IntPtr commandstring, int cch);
        [PreserveSig] int HandleMenuMsg(int uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(int uMsg, IntPtr wParam, IntPtr lParam, out int plResult);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public CMIC fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [Flags]
    private enum CMF : uint
    {
        NORMAL = 0x00000000,
        EXPLORE = 0x00000004
    }

    [Flags]
    private enum CMIC : uint
    {
        UNICODE = 0x00004000
    }

    [Flags]
    private enum TPM : uint
    {
        RETURNCMD = 0x0100,
        RIGHTBUTTON = 0x0002
    }

    private static class SW
    {
        public const int SHOWNORMAL = 1;
    }
}
