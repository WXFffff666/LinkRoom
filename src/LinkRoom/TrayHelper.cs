using System.Runtime.InteropServices;
using System.Windows;

namespace LinkRoom;

public static class TrayHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);
    [DllImport("shell32.dll")]
    static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);

    const uint NIM_ADD = 0, NIM_DELETE = 2;
    const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    const uint WM_LBUTTONDBLCLK = 0x0203;

    static NOTIFYICONDATA _data;
    static IntPtr _iconHandle;
    static bool _visible;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    public static void Show(Window window)
    {
        if (_visible) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        _iconHandle = ExtractIcon(IntPtr.Zero, Environment.ProcessPath!, 0);
        _data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd, uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_LBUTTONDBLCLK,
            hIcon = _iconHandle, szTip = "LinkRoom"
        };
        Shell_NotifyIcon(NIM_ADD, ref _data);
        _visible = true;
    }

    public static void Hide()
    {
        if (!_visible) return;
        Shell_NotifyIcon(NIM_DELETE, ref _data);
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
        _visible = false;
    }
}