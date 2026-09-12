using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MarketPos.Services;

/// <summary>
/// A window that floats above the app without ever taking anything from it.
///
/// The on-screen keyboard and its button both sit on top of every other window, including the
/// modal dialogs, because that is where most of the typing happens. A window like that is
/// normally a menace: click it and Windows makes it the active window, which takes focus off
/// the box being typed into — so the first key press would land nowhere and the caret would
/// be gone.
///
/// <c>WS_EX_NOACTIVATE</c> is the whole answer. The window can be clicked and its buttons
/// still fire, but Windows never activates it, so the till or the dialog underneath stays
/// exactly as it was with its cursor still blinking in the same field.
///
/// <c>WS_EX_TOOLWINDOW</c> comes along for the ride: it keeps a floating panel out of Alt-Tab
/// and off the taskbar, where a keyboard listed as an open application would be nonsense.
/// </summary>
public static class Floating
{
    private const int ExtendedStyle = -20;
    private const int NoActivate = 0x08000000;
    private const int ToolWindow = 0x00000080;

    /// <summary>
    /// Call from <see cref="Window.OnSourceInitialized"/> — earlier there is no window handle
    /// to change, and later the window has already had its chance to steal focus.
    /// </summary>
    public static void KeepFocusElsewhere(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var style = GetWindowLong(handle, ExtendedStyle);
        SetWindowLong(handle, ExtendedStyle, style | NoActivate | ToolWindow);

        // And the half that the extended style does not cover.
        //
        // WS_EX_NOACTIVATE keeps the window out of the way of Alt-Tab and of being activated
        // in the ordinary course of things. It does not stop a click on it from activating it
        // — Windows asks first, with WM_MOUSEACTIVATE, and the default answer is yes. Which is
        // why pressing a key on the on-screen keyboard took focus off the box being typed
        // into: the field lost its caret on the way down, and by the time the character was
        // sent there was nothing left to send it to.
        //
        // Answering MA_NOACTIVATE means the press still arrives, the button still fires, and
        // the window stays exactly as unfocused as it was.
        HwndSource.FromHwnd(handle)?.AddHook(StayOutOfTheWay);
    }

    private const int MouseActivate = 0x0021;
    private const int DoNotActivate = 3;

    private static IntPtr StayOutOfTheWay(IntPtr window, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message != MouseActivate) return IntPtr.Zero;

        handled = true;
        return new IntPtr(DoNotActivate);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr window, int index, int value);

    // The 32-bit and 64-bit entry points are different functions, not one with a wider
    // argument, and calling the wrong one silently does nothing.
    private static int GetWindowLong(IntPtr window, int index) =>
        IntPtr.Size == 8 ? (int)GetWindowLongPtr(window, index) : GetWindowLong32(window, index);

    private static void SetWindowLong(IntPtr window, int index, int value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr(window, index, new IntPtr(value));
        else SetWindowLong32(window, index, value);
    }
}
