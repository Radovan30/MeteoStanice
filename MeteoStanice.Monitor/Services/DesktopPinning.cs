using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MeteoStanice.Monitor.Services;

/// <summary>
/// Pripnuti okna na plochu Windows (rezim widgetu).
/// <list type="bullet">
/// <item>Vlastnikem okna se stane okno plochy (Progman) – okno pak neni na hlavnim panelu
/// a zustane viditelne i po Win+D (zobrazit plochu).</item>
/// <item>WS_EX_TOOLWINDOW – okno neni v prepinani Alt+Tab.</item>
/// <item>Pri kazde zmene poradi oken se okno vraci na spodek (HWND_BOTTOM), takze nikdy nezakryva
/// ostatni aplikace – chova se jako widget na plose.</item>
/// </list>
/// </summary>
public sealed class DesktopPinning
{
    private const int GwlExStyle = -20;
    private const int GwlpHwndParent = -8;
    private const long WsExToolWindow = 0x00000080;
    private const int WmWindowPosChanging = 0x0046;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndBottom = new(1);

    private readonly Window _window;
    private HwndSource? _source;
    private bool _pinned;

    /// <summary>
    /// Vytvori pripinani pro okno.
    /// </summary>
    public DesktopPinning(Window window) => _window = window;

    /// <summary>
    /// Okno je pripnute na plose.
    /// </summary>
    public bool IsPinned => _pinned;

    /// <summary>
    /// Pripne okno na plochu nebo ho vrati do normalniho rezimu. Volat az po SourceInitialized.
    /// </summary>
    public void SetPinned(bool pinned)
    {
        IntPtr hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero || pinned == _pinned)
        {
            return;
        }

        if (_source is null)
        {
            _source = HwndSource.FromHwnd(hwnd);
            _source?.AddHook(WndProc);
        }

        _pinned = pinned;

        // Zmena vlastnika a stylu se na hlavnim panelu projevi az po skryti a znovuzobrazeni okna.
        _window.Hide();

        IntPtr owner = pinned ? FindWindow("Progman", null) : IntPtr.Zero;
        SetWindowLongPtrCompat(hwnd, GwlpHwndParent, owner);

        long exStyle = GetWindowLongPtrCompat(hwnd, GwlExStyle).ToInt64();
        exStyle = pinned ? exStyle | WsExToolWindow : exStyle & ~WsExToolWindow;
        SetWindowLongPtrCompat(hwnd, GwlExStyle, new IntPtr(exStyle));

        _window.Show();

        if (pinned)
        {
            SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
        else
        {
            _window.Activate();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_pinned && msg == WmWindowPosChanging && lParam != IntPtr.Zero)
        {
            WindowPos position = Marshal.PtrToStructure<WindowPos>(lParam);
            if ((position.Flags & SwpNoZOrder) == 0)
            {
                // Kazdy pokus o presun okna dopredu (kliknuti, aktivace) se zmeni na presun na spodek.
                position.HwndInsertAfter = HwndBottom;
                Marshal.StructureToPtr(position, lParam, false);
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr GetWindowLongPtrCompat(IntPtr hwnd, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));

    private static void SetWindowLongPtrCompat(IntPtr hwnd, int index, IntPtr value)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(hwnd, index, value);
        }
        else
        {
            SetWindowLong32(hwnd, index, value.ToInt32());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public uint Flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
