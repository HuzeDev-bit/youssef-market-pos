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
