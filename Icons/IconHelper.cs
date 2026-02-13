using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SpeedExplorer;

public static class IconHelper
{
    private static readonly object IconApiLock = new();
    // Constants for SHGetFileInfo
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010; // Use caching by extension
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_LARGEICON = 0x000000000;         // Actually 0
    private const uint SHGFI_TYPENAME = 0x000000400;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int iImageList, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IImageList ppv);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetObject(IntPtr h, int c, out BITMAP pv);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, [Out] byte[] bits, ref BITMAPINFO bmi, uint usage);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IShellItemImageFactory ppv);

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        void GetImage(
            [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
            [In] SIIGBF flags,
            out IntPtr phbm);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
        [PreserveSig] int GetImageInfo(int i, IntPtr pImageInfo);
        [PreserveSig] int Copy(int iDst, IImageList punkSrc, int iSrc, int uFlags);
        [PreserveSig] int Merge(int i1, IImageList punk2, int i2, int dx, int dy, ref Guid riid, ref IntPtr ppv);
        [PreserveSig] int Clone(ref Guid riid, ref IntPtr ppv);
        [PreserveSig] int GetImageRect(int i, ref Rectangle prc);
        [PreserveSig] int GetIconSize(ref int cx, ref int cy);
        [PreserveSig] int SetIconSize(int cx, int cy);
        [PreserveSig] int GetImageCount(ref int pi);
        [PreserveSig] int SetImageCount(int uNewCount);
        [PreserveSig] int SetBkColor(int clrBk, ref int pclr);
        [PreserveSig] int GetBkColor(ref int pclr);
        [PreserveSig] int BeginDrag(int iTrack, int dxHotspot, int dyHotspot);
        [PreserveSig] int EndDrag();
        [PreserveSig] int DragEnter(IntPtr hwndLock, int x, int y);
        [PreserveSig] int DragLeave(IntPtr hwndLock);
        [PreserveSig] int DragMove(int x, int y);
        [PreserveSig] int SetDragCursorImage(IImageList punk, int iDrag, int dxHotspot, int dyHotspot);
        [PreserveSig] int DragShowNolock(int fShow);
        [PreserveSig] int GetDragImage(ref Point ppt, ref Point pptHotspot, ref Guid riid, ref IntPtr ppv);
        [PreserveSig] int GetItemFlags(int i, ref int dwFlags);
        [PreserveSig] int GetOverlayImage(int iOverlay, ref int piIndex);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
    }

    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10,
    }

    private static readonly Guid IShellItemImageFactoryGuid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private static readonly Guid IImageListGuid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;
    private const int ILD_TRANSPARENT = 0x00000001;
    private const int SHIL_LARGE = 0x0;
    private const int SHIL_SMALL = 0x1;
    private const int SHIL_EXTRALARGE = 0x2;
    private const int SHIL_JUMBO = 0x4;

    public static Bitmap? GetIconSized(string path, bool isDirectory, int size, bool forceUnique, bool grayscale = false)
    {
        lock (IconApiLock)
        {
            bool looksLikePath = LooksLikePath(path);
            bool exists = false;
            if (looksLikePath)
            {
                try { exists = isDirectory ? System.IO.Directory.Exists(path) : System.IO.File.Exists(path); } catch { }
            }

            // Prefer high-quality shell icons for real filesystem paths.
            // Keep 40-56px in "request 64px then downscale" mode for better sharpness.
            if (looksLikePath)
            {
                if (exists && size > 32)
                {
                    int shellRequestSize = (size >= 40 && size <= 56) ? 64 : size;
                    if (TryGetShellImage(path, shellRequestSize, out var shellBmp) && shellBmp != null)
                    {
                        Bitmap result = RenderBitmapToRequestedSize(shellBmp, size);
                        if (grayscale)
                            return ToGrayscale(result);
                        return result;
                    }
                }
            }

            // Fallback path: SHGetFileInfo + (for larger sizes) system image list when available.

            bool large = size > 16;
            var shinfo = new SHFILEINFO();
            uint flags = SHGFI_ICON;

            if (large)
                flags |= SHGFI_LARGEICON;
            else
                flags |= SHGFI_SMALLICON;

            if (!forceUnique)
                flags |= SHGFI_USEFILEATTRIBUTES;

            uint attr = 0;
            if (isDirectory) attr = 0x00000010; // FILE_ATTRIBUTE_DIRECTORY
            else attr = 0x00000080; // FILE_ATTRIBUTE_NORMAL

            // For generic/extension-based icons (non-existing paths), pull from system image lists.
            // This also makes 40-56px use the higher-quality source.
            string lookupPath = NormalizeLookupPathForFileAttributes(path, isDirectory);
            if ((!looksLikePath || !exists) && size > 32)
            {
                if (TryGetSystemImageListIcon(lookupPath, attr, forceUnique, size, out var listBmp) && listBmp != null)
                {
                    if (grayscale)
                        return ToGrayscale(listBmp);
                    return listBmp;
                }
            }

            IntPtr res = SHGetFileInfo(lookupPath, attr, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);

            if (res == IntPtr.Zero) return null;
            if (shinfo.hIcon == IntPtr.Zero) return null;

            try
            {
                using var icon = Icon.FromHandle(shinfo.hIcon);
                using var cloned = (Icon)icon.Clone();
                var baseBmp = cloned.ToBitmap();
                var sized = size == baseBmp.Width ? baseBmp : ResizeBitmap(baseBmp, size);

                if (grayscale)
                    return ToGrayscale(sized);

                return sized;
            }
            catch
            {
                return null;
            }
            finally
            {
                DestroyIcon(shinfo.hIcon);
            }
        }
    }

    public static Bitmap? GetIcon(string path, bool isDirectory, bool large, bool forceUnique, bool grayscale = false)
    {
        int size = large ? 32 : 16;
        return GetIconSized(path, isDirectory, size, forceUnique, grayscale);
    }

    private static bool LooksLikePath(string path)
    {
        return path.Contains('\\') || path.Contains('/') || path.Contains(":");
    }

    private static string NormalizeLookupPathForFileAttributes(string path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return isDirectory ? "C:\\" : "dummy.bin";

        string p = path.Trim();
        if (LooksLikePath(p))
            return p;

        if (isDirectory)
            return "C:\\DummyFolder";

        if (p.Equals("file", StringComparison.OrdinalIgnoreCase))
            return "dummy.bin";

        if (p.StartsWith("."))
            return "dummy" + p;

        if (!p.Contains('.'))
            return "dummy." + p;

        return p;
    }

    private static bool TryGetShellImage(string path, int size, out Bitmap? bmp)
    {
        bmp = null;
        IShellItemImageFactory? factory = null;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, IShellItemImageFactoryGuid, out factory);
            if (factory != null)
            {
                factory.GetImage(
                    new SIZE(size, size),
                    SIIGBF.SIIGBF_RESIZETOFIT | SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_ICONONLY,
                    out var hBitmap);
                if (hBitmap != IntPtr.Zero)
                {
                    try
                    {
                        Bitmap? hbmp = CreateBitmapFromHBitmapWithAlpha(hBitmap);
                        if (hbmp == null)
                        {
                            using var fallback = Image.FromHbitmap(hBitmap);
                            hbmp = new Bitmap(fallback);
                        }
                        using (hbmp)
                        {
                        bmp = new Bitmap(size, size);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Transparent);
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            float scale = Math.Min((float)size / hbmp.Width, (float)size / hbmp.Height);
                            if (scale > 1f) scale = 1f; // avoid upscaling small source icons
                            int w = (int)(hbmp.Width * scale);
                            int h = (int)(hbmp.Height * scale);
                            int x = (size - w) / 2;
                            int y = (size - h) / 2;
                            g.DrawImage(hbmp, x, y, w, h);
                        }
                        }

                        // Some shell providers (notably some .exe/.lnk) return icon bitmaps
                        // with an opaque backdrop at specific sizes.
                        if (size > 32 && !HasAnyTransparency(bmp) &&
                            (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                             path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                             path.EndsWith(".url", StringComparison.OrdinalIgnoreCase)))
                        {
                            TryNormalizeOpaqueBackdrop(bmp);
                        }

                        return true;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
        }
        catch { }
        finally
        {
            if (factory != null)
            {
                try { Marshal.ReleaseComObject(factory); } catch { }
            }
        }
        return false;
    }

    private static bool TryGetSystemImageListIcon(string path, uint attr, bool forceUnique, int size, out Bitmap? bmp)
    {
        bmp = null;
        IImageList? imageList = null;
        IntPtr hIcon = IntPtr.Zero;

        try
        {
            var shinfo = new SHFILEINFO();
            uint flags = SHGFI_SYSICONINDEX;
            if (!forceUnique)
                flags |= SHGFI_USEFILEATTRIBUTES;

            IntPtr res = SHGetFileInfo(path, attr, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
            if (res == IntPtr.Zero || shinfo.iIcon < 0)
                return false;

            int imageListKind = size <= 16
                ? SHIL_SMALL
                : size <= 32
                    ? SHIL_LARGE
                    : size < 40
                        ? SHIL_EXTRALARGE
                        : SHIL_JUMBO;

            int hr = SHGetImageList(imageListKind, IImageListGuid, out imageList!);
            if (hr != 0 || imageList == null)
                return false;

            hr = imageList.GetIcon(shinfo.iIcon, ILD_TRANSPARENT, ref hIcon);
            if (hr != 0 || hIcon == IntPtr.Zero)
                return false;

            using var icon = Icon.FromHandle(hIcon);
            var baseBmp = icon.ToBitmap();
            bmp = RenderBitmapToRequestedSize(baseBmp, size);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hIcon != IntPtr.Zero)
            {
                try { DestroyIcon(hIcon); } catch { }
            }
            if (imageList != null)
            {
                try { Marshal.ReleaseComObject(imageList); } catch { }
            }
        }
    }

    private static Bitmap? CreateBitmapFromHBitmapWithAlpha(IntPtr hBitmap)
    {
        try
        {
            if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), out var gdiBitmap) == 0)
                return null;

            int width = gdiBitmap.bmWidth;
            int height = Math.Abs(gdiBitmap.bmHeight);
            if (width <= 0 || height <= 0)
                return null;

            int srcStride = width * 4;
            var pixels = new byte[srcStride * height];
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // top-down DIB
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                    biSizeImage = (uint)pixels.Length
                },
                bmiColors = 0
            };

            IntPtr screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
                return null;

            try
            {
                int scanlines = GetDIBits(screenDc, hBitmap, 0, (uint)height, pixels, ref bmi, DIB_RGB_COLORS);
                if (scanlines == 0)
                    return null;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }

            bool allAlphaZero = true;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    allAlphaZero = false;
                    break;
                }
            }

            if (allAlphaZero)
            {
                for (int i = 3; i < pixels.Length; i += 4)
                    pixels[i] = 255;
            }

            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var data = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int dstStride = data.Stride;
                if (dstStride == srcStride)
                {
                    Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                }
                else
                {
                    int rowBytes = Math.Min(Math.Abs(dstStride), srcStride);
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr dstRow = IntPtr.Add(data.Scan0, y * dstStride);
                        Marshal.Copy(pixels, y * srcStride, dstRow, rowBytes);
                    }
                }
            }
            finally
            {
                result.UnlockBits(data);
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static void TryNormalizeOpaqueBackdrop(Bitmap bmp)
    {
        try
        {
            if (bmp.Width <= 0 || bmp.Height <= 0)
                return;
            if (HasAnyTransparency(bmp))
                return;

            var seed = bmp.GetPixel(0, 0);
            if (seed.A < 250)
                return;
            if (!BorderMostlyMatches(bmp, seed, tolerance: 16))
                return;

            int w = bmp.Width;
            int h = bmp.Height;
            var visited = new bool[w, h];
            var q = new System.Collections.Generic.Queue<(int X, int Y)>();

            void EnqueueIfMatch(int x, int y)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return;
                if (visited[x, y]) return;
                var c = bmp.GetPixel(x, y);
                if (!ColorClose(c, seed, 20)) return;
                visited[x, y] = true;
                q.Enqueue((x, y));
            }

            for (int x = 0; x < w; x++)
            {
                EnqueueIfMatch(x, 0);
                EnqueueIfMatch(x, h - 1);
            }
            for (int y = 0; y < h; y++)
            {
                EnqueueIfMatch(0, y);
                EnqueueIfMatch(w - 1, y);
            }

            while (q.Count > 0)
            {
                var p = q.Dequeue();
                var c = bmp.GetPixel(p.X, p.Y);
                bmp.SetPixel(p.X, p.Y, Color.FromArgb(0, c.R, c.G, c.B));
                EnqueueIfMatch(p.X - 1, p.Y);
                EnqueueIfMatch(p.X + 1, p.Y);
                EnqueueIfMatch(p.X, p.Y - 1);
                EnqueueIfMatch(p.X, p.Y + 1);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private static bool HasAnyTransparency(Bitmap bmp)
    {
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y).A < 250)
                    return true;
            }
        }
        return false;
    }

    private static bool BorderMostlyMatches(Bitmap bmp, Color seed, int tolerance)
    {
        int total = 0;
        int matches = 0;
        int w = bmp.Width;
        int h = bmp.Height;

        for (int x = 0; x < w; x++)
        {
            total += 2;
            if (ColorClose(bmp.GetPixel(x, 0), seed, tolerance)) matches++;
            if (ColorClose(bmp.GetPixel(x, h - 1), seed, tolerance)) matches++;
        }
        for (int y = 1; y < h - 1; y++)
        {
            total += 2;
            if (ColorClose(bmp.GetPixel(0, y), seed, tolerance)) matches++;
            if (ColorClose(bmp.GetPixel(w - 1, y), seed, tolerance)) matches++;
        }

        return total > 0 && matches >= (int)(total * 0.93);
    }

    private static bool ColorClose(Color a, Color b, int tolerance)
    {
        return Math.Abs(a.R - b.R) <= tolerance &&
               Math.Abs(a.G - b.G) <= tolerance &&
               Math.Abs(a.B - b.B) <= tolerance;
    }

    private static Bitmap ResizeBitmap(Bitmap source, int size)
    {
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.Transparent);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, size, size);
        source.Dispose();
        return result;
    }

    private static Bitmap RenderBitmapToRequestedSize(Bitmap source, int size)
    {
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(result);
            g.Clear(Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            Rectangle srcRect = GetAlphaContentBounds(source);
            if (srcRect.Width <= 0 || srcRect.Height <= 0)
                srcRect = new Rectangle(0, 0, source.Width, source.Height);

            float scale = Math.Min((float)size / srcRect.Width, (float)size / srcRect.Height);
            if (scale > 1f) scale = 1f; // keep small native icons crisp
            int w = Math.Max(1, (int)Math.Round(srcRect.Width * scale));
            int h = Math.Max(1, (int)Math.Round(srcRect.Height * scale));
            int x = (size - w) / 2;
            int y = (size - h) / 2;

            g.DrawImage(source, new Rectangle(x, y, w, h), srcRect, GraphicsUnit.Pixel);
            return result;
        }
        finally
        {
            source.Dispose();
        }
    }

    private static Rectangle GetAlphaContentBounds(Bitmap bmp)
    {
        int minX = bmp.Width;
        int minY = bmp.Height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y).A > 10)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < minX || maxY < minY)
            return Rectangle.Empty;

        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static Bitmap ToGrayscale(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(result);

        var colorMatrix = new ColorMatrix(new float[][]
        {
            new float[] { 0.3f, 0.3f, 0.3f, 0, 0 },
            new float[] { 0.59f, 0.59f, 0.59f, 0, 0 },
            new float[] { 0.11f, 0.11f, 0.11f, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 0, 0, 1 }
        });

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(colorMatrix);

        g.DrawImage(source,
            new Rectangle(0, 0, source.Width, source.Height),
            0, 0, source.Width, source.Height,
            GraphicsUnit.Pixel,
            attributes);

        source.Dispose();
        return result;
    }

    public static Bitmap? GetThumbnail(string path, int size)
    {
        lock (IconApiLock)
        {
            IShellItemImageFactory? factory = null;
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, IShellItemImageFactoryGuid, out factory);
                if (factory != null)
                {
                    factory.GetImage(new SIZE(size, size), SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_THUMBNAILONLY, out var hBitmap);
                    if (hBitmap != IntPtr.Zero)
                    {
                        try
                        {
                            using var hbmp = Image.FromHbitmap(hBitmap);
                            var result = new Bitmap(size, size);
                            using (var g = Graphics.FromImage(result))
                            {
                                g.Clear(Color.Transparent);
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                                float scale = Math.Min((float)size / hbmp.Width, (float)size / hbmp.Height);
                                int w = (int)(hbmp.Width * scale);
                                int h = (int)(hbmp.Height * scale);
                                int x = (size - w) / 2;
                                int y = (size - h) / 2;

                                g.DrawImage(hbmp, x, y, w, h);
                            }
                            return result;
                        }
                        finally
                        {
                            DeleteObject(hBitmap);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (factory != null)
                {
                    try { Marshal.ReleaseComObject(factory); } catch { }
                }
            }
            return null;
        }
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}
