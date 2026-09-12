using System.Runtime.InteropServices;

namespace MarketPos.Services;

/// <summary>
/// Sends a keystroke to whatever the shop is typing into, as though a real keyboard had been
/// pressed.
///
/// The alternative was to find the focused TextBox and write into its Text property, and that
/// is wrong in a way that only shows up later. Half the boxes in this app filter what may be
/// typed into them — a price box refuses letters, a quantity box refuses a second decimal
/// point, the till watches the gaps between keystrokes to tell a barcode scanner from a
/// cashier. All of that hangs off the input events. Poking Text goes underneath every one of
/// them, so the on-screen keyboard would be the one way into the app where a price could be
/// "abc".
///
/// Going through the operating system instead means the keys arrive where a real keyboard's
/// would, in the same order, through the same handlers. Nothing else in the app has to know
/// this exists.
///
/// <para>
/// Characters are sent as Unicode rather than as key codes, which is what lets one panel of
/// keys type Arabic, French and English without the shop machine's keyboard layout having
/// anything to do with it.
/// </para>
/// </summary>
public static class KeyStrokes
{
    public const ushort Backspace = 0x08;
    public const ushort Tab = 0x09;
    public const ushort Enter = 0x0D;
    public const ushort Escape = 0x1B;

    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;

    /// <summary>Types a string, one character at a time.</summary>
    public static void Type(string text)
    {
        foreach (var ch in text) Send(new[] { Character(ch, up: false), Character(ch, up: true) });
    }

    /// <summary>Presses and releases a key by its virtual-key code — Backspace, Enter, Tab.</summary>
    public static void Press(ushort key) =>
        Send(new[] { Virtual(key, up: false), Virtual(key, up: true) });

    /// <summary>
    /// Presses Enter at whatever the shop is filling in.
    ///
    /// Enter is the one key here that is not about the text in the box: it means "done with
    /// this", and every screen in the app answers it in its own KeyDown handler — commit the
    /// quantity and go back to scanning, save the product, sign in. Sent through the operating
    /// system like the letters are, it went to the window with the keyboard focus, which on a
    /// till driven entirely by fingers is not reliably the window the shop is looking at, and
    /// the press did nothing at all.
    ///
    /// So this one is raised where it is meant to land: on the element with the caret, as the
    /// same pair of routed events a real Enter would arrive as. The handlers cannot tell the
    /// difference — they read the key and nothing else — and there is no text being filtered
    /// here for the operating-system route to protect. If nothing has the caret there is
    /// nothing to finish, and it falls back to the plain keystroke.
    /// </summary>
    public static void PressEnter()
    {
        if (System.Windows.Input.Keyboard.FocusedElement is not System.Windows.DependencyObject caret
            || System.Windows.PresentationSource.FromDependencyObject(caret) is not { } source)
        {
            Press(Enter);
            return;
        }

        var pressed = new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice, source, 0,
            System.Windows.Input.Key.Enter)
        {
            RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent,
        };

        var target = (System.Windows.UIElement?)(caret as System.Windows.UIElement);
        if (target is null)
        {
            Press(Enter);
            return;
        }

        target.RaiseEvent(pressed);

        // The tunnelling pass is where a window says "not for the box, for me" — a dialog that
        // saves on Enter, say. Only if nobody claimed it does the bubbling pass follow, exactly
        // as WPF does it for a key off the counter.
        if (pressed.Handled) return;

        pressed.RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent;
        target.RaiseEvent(pressed);
    }

    /// <summary>
    /// Empties the box being typed into: select everything in it, then delete the selection.
    ///
    /// Ctrl+A and a backspace, sent as real keys like everything else here, so a box that
    /// filters what may be typed into it sees the deletion the same way it would from a
    /// keyboard on the counter. Holding backspace down instead is a press per character and
    /// no way to be sure the box is actually empty.
    /// </summary>
    public static void ClearField()
    {
        Send(new[]
        {
            Virtual(Control, up: false), Virtual(SelectAll, up: false),
            Virtual(SelectAll, up: true), Virtual(Control, up: true),
        });

        Press(Backspace);
    }

    private const ushort Control = 0x11;
    private const ushort SelectAll = 0x41;   // 'A'


    private static void Send(Input[] keys)
    {
        // Nothing to be done if it fails: the window that had focus went away between the tap
        // and here, which is the shop closing a dialog with the keyboard still up.
        _ = SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>());
    }

    private static Input Character(char ch, bool up) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                // Virtual key 0 and the character in the scan-code field is how a Unicode
                // keystroke is spelled; the layout on the machine never enters into it.
                VirtualKey = 0,
                ScanCode = ch,
                Flags = Unicode | (up ? KeyUp : 0),
            },
        },
    };

    private static Input Virtual(ushort key, bool up) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput { VirtualKey = key, Flags = up ? KeyUp : 0 },
        },
    };

    // ============================== What Windows expects ==============================
    //
    // INPUT is a tagged union of three shapes and is sized by the largest of them, so the
    // mouse and hardware members have to be declared even though nothing here sends either.
    // Leave them out and the struct is too small, SendInput rejects the call, and no key ever
    // arrives — with no error anywhere to say why.

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public IntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParamL, ParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
