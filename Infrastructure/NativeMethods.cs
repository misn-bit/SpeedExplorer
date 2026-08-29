using System.Runtime.InteropServices;

namespace SpeedExplorer;

internal static class NativeMethods
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    internal static extern int SHMultiFileProperties(System.Runtime.InteropServices.ComTypes.IDataObject pdtobj, int dwFlags);
}
