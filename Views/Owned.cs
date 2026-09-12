using System.Windows;
using System.Windows.Interop;

namespace MarketPos.Views;

/// <summary>
/// Gives a dialog its owner, when there is one it can actually have.
///
/// <para>
/// WPF refuses to make a window an Owner until that window has been shown, and throws
/// <c>Cannot set Owner property to a Window that has not been shown previously</c> if you try.
/// Every dialog in this app is opened over the window the shop is looking at, so almost always
/// there is one and it is on screen — but not always: the diagnostics build a till without ever
/// showing it and then drive it, which is the whole point of them.
/// </para>
///
/// <para>
/// So the owner is set when it is usable and left alone when it is not. An ownerless dialog
/// still opens, still blocks and still returns an answer; it simply centres on the screen
/// rather than on its parent. That is a far better outcome than the till falling over in front
/// of a customer because of where a window happened to be in its life.
/// </para>
/// </summary>
public static class Owned
{
    /// <summary>Hands the dialog its owner if that owner has been shown, and returns the dialog.</summary>
    public static T By<T>(this T dialog, Window? owner) where T : Window
    {
        if (CanOwn(owner)) dialog.Owner = owner;
        return dialog;
    }

    /// <summary>
    /// Whether WPF will accept this window as an Owner.
    ///
    /// A window gets its handle from the operating system when it is first shown and keeps it
    /// until it is closed, which makes the handle the honest answer to "has this been shown?" —
    /// more so than IsVisible, which is false for a perfectly good owner that happens to be
    /// minimised behind the dialog it is opening.
    /// </summary>
    public static bool CanOwn(Window? owner) =>
        owner is not null && new WindowInteropHelper(owner).Handle != IntPtr.Zero;
}
