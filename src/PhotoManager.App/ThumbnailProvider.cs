using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PhotoManager.App;

/// <summary>
/// Pobiera miniaturę pliku przez powłokę Windows (IShellItemImageFactory) — to samo źródło,
/// z którego korzysta Eksplorator. Szybkie i cache'owane przez system; dla RAW (ARW) działa,
/// jeśli zainstalowany jest odpowiedni kodek (np. Raw Image Extension). Zwraca null, gdy się nie uda.
/// </summary>
public static class ThumbnailProvider
{
    public static BitmapSource? GetThumbnail(string path, int size)
    {
        if (!File.Exists(path)) return null;

        IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (factory is null) return null;

            var sz = new SIZE { cx = size, cy = size };
            int hr = factory.GetImage(sz, SIIGBF.ResizeToFit, out IntPtr hbitmap);
            if (hr != 0 || hbitmap == IntPtr.Zero) return null;

            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(
                    hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze(); // pozwala użyć na wątku UI po utworzeniu w tle
                return src;
            }
            finally { DeleteObject(hbitmap); }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
    }
}
