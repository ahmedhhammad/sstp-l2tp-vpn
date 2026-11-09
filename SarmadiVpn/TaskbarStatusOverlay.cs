using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SarmadiVpn.Core;

namespace SarmadiVpn;

internal sealed class TaskbarStatusOverlay : IDisposable
{
    private static readonly Guid TaskbarListClassId = new("56FDF344-FD6D-11D0-958A-006097C9A090");
    private readonly ITaskbarList3 _taskbar;
    private IntPtr _currentIcon;
    private bool _disposed;

    public TaskbarStatusOverlay()
    {
        _taskbar = CreateTaskbarList();
    }

    internal static void VerifyTaskbarInterfaceCanBeQueried()
    {
        var taskbarType = Type.GetTypeFromCLSID(TaskbarListClassId, throwOnError: true)!;
        var taskbar = (ITaskbarList3)Activator.CreateInstance(taskbarType)!;
        if (Marshal.IsComObject(taskbar)) Marshal.ReleaseComObject(taskbar);
    }

    private static ITaskbarList3 CreateTaskbarList()
    {
        var taskbarType = Type.GetTypeFromCLSID(TaskbarListClassId, throwOnError: true)!;
        var taskbar = (ITaskbarList3)Activator.CreateInstance(taskbarType)!;
        try
        {
            Marshal.ThrowExceptionForHR(taskbar.HrInit());
            return taskbar;
        }
        catch
        {
            if (Marshal.IsComObject(taskbar)) Marshal.ReleaseComObject(taskbar);
            throw;
        }
    }

    public void Update(IntPtr windowHandle, VpnState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (windowHandle == IntPtr.Zero) throw new ArgumentException("The window handle is not available.", nameof(windowHandle));

        var color = state switch
        {
            VpnState.Connected => Color.FromRgb(34, 197, 94),
            VpnState.Connecting or VpnState.Disconnecting => Color.FromRgb(240, 180, 20),
            _ => Color.FromRgb(220, 53, 69)
        };

        var icon = CreateStatusIcon(color);
        try
        {
            Marshal.ThrowExceptionForHR(_taskbar.SetOverlayIcon(windowHandle, icon, $"VPN {state}"));
        }
        catch
        {
            DestroyIcon(icon);
            throw;
        }

        var oldIcon = _currentIcon;
        _currentIcon = icon;
        if (oldIcon != IntPtr.Zero) DestroyIcon(oldIcon);
    }

    public void Clear(IntPtr windowHandle)
    {
        if (_disposed) return;
        if (windowHandle != IntPtr.Zero)
            Marshal.ThrowExceptionForHR(_taskbar.SetOverlayIcon(windowHandle, IntPtr.Zero, string.Empty));
        if (_currentIcon != IntPtr.Zero)
        {
            DestroyIcon(_currentIcon);
            _currentIcon = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_currentIcon != IntPtr.Zero)
        {
            DestroyIcon(_currentIcon);
            _currentIcon = IntPtr.Zero;
        }
        if (Marshal.IsComObject(_taskbar)) Marshal.ReleaseComObject(_taskbar);
    }

    private static IntPtr CreateStatusIcon(Color color)
    {
        const int size = 16;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawEllipse(Brushes.White, null, new Point(size / 2d, size / 2d), 7.5, 7.5);
            drawing.DrawEllipse(new SolidColorBrush(color), null, new Point(size / 2d, size / 2d), 5.5, 5.5);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);

        var bitmapInfo = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = size,
                Height = -size,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
                SizeImage = (uint)pixels.Length
            }
        };
        var colorBitmap = CreateDIBSection(IntPtr.Zero, ref bitmapInfo, 0, out var pixelBuffer, IntPtr.Zero, 0);
        if (colorBitmap == IntPtr.Zero || pixelBuffer == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the VPN status icon bitmap.");

        var maskBitmap = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
        if (maskBitmap == IntPtr.Zero)
        {
            DeleteObject(colorBitmap);
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the VPN status icon mask.");
        }

        try
        {
            Marshal.Copy(pixels, 0, pixelBuffer, pixels.Length);
            var iconInfo = new IconInfo { IsIcon = true, MaskBitmap = maskBitmap, ColorBitmap = colorBitmap };
            var icon = CreateIconIndirect(ref iconInfo);
            if (icon == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create the VPN status overlay icon.");
            return icon;
        }
        finally
        {
            DeleteObject(maskBitmap);
            DeleteObject(colorBitmap);
        }
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr windowHandle);
        [PreserveSig] int DeleteTab(IntPtr windowHandle);
        [PreserveSig] int ActivateTab(IntPtr windowHandle);
        [PreserveSig] int SetActiveAlt(IntPtr windowHandle);
        [PreserveSig] int MarkFullscreenWindow(IntPtr windowHandle, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        [PreserveSig] int SetProgressValue(IntPtr windowHandle, ulong completed, ulong total);
        [PreserveSig] int SetProgressState(IntPtr windowHandle, uint state);
        [PreserveSig] int RegisterTab(IntPtr tabHandle, IntPtr mdiHandle);
        [PreserveSig] int UnregisterTab(IntPtr tabHandle);
        [PreserveSig] int SetTabOrder(IntPtr tabHandle, IntPtr insertBeforeHandle);
        [PreserveSig] int SetTabActive(IntPtr tabHandle, IntPtr mdiHandle, uint reserved);
        [PreserveSig] int ThumbBarAddButtons(IntPtr windowHandle, uint buttonCount, IntPtr buttons);
        [PreserveSig] int ThumbBarUpdateButtons(IntPtr windowHandle, uint buttonCount, IntPtr buttons);
        [PreserveSig] int ThumbBarSetImageList(IntPtr windowHandle, IntPtr imageList);
        [PreserveSig] int SetOverlayIcon(IntPtr windowHandle, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
        [PreserveSig] int SetThumbnailTooltip(IntPtr windowHandle, [MarshalAs(UnmanagedType.LPWStr)] string tooltip);
        [PreserveSig] int SetThumbnailClip(IntPtr windowHandle, IntPtr rectangle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr deviceContext, ref BitmapInfo bitmapInfo, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, IntPtr bits);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr objectHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref IconInfo iconInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
