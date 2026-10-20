using System;
using System.Windows.Forms;
using G8VolumeOsd.Interop;

namespace G8VolumeOsd;

/// <summary>
/// EXPERIMENTAL. Opens the built-in Windows volume flyout.
///
/// Windows has no API to show the flyout; it appears only when the shell itself handles a
/// volume command. We send APPCOMMAND_VOLUME_DOWN + APPCOMMAND_VOLUME_UP (or the reverse) to
/// our own hidden top-level window. DefWindowProc forwards unhandled app commands to the shell
/// (HSHELL_APPCOMMAND), which changes the volume one step each way and shows its flyout.
///
/// Nothing is injected into the input stream, so the foreground game never receives the
/// commands. Side effect: the shell snaps to its 2% grid, so an odd level ends on the
/// neighboring even step. The order is chosen so that rounding goes in the direction the
/// knob is turning.
/// </summary>
internal sealed class WindowsFlyoutTrigger : NativeWindow, IDisposable
{
    private const int WmAppCommand = 0x0319;
    private const int AppCommandVolumeDown = 9;
    private const int AppCommandVolumeUp = 10;
    private const int WsExToolWindow = 0x00000080;

    public WindowsFlyoutTrigger()
    {
        // Plain, never-shown top-level window. It must not be a child or message-only window,
        // because only top-level windows hand unhandled app commands to the shell.
        CreateHandle(new CreateParams { Caption = "G8VolumeOSD flyout trigger", ExStyle = WsExToolWindow });
    }

    public void Show(bool turningUp)
    {
        if (Handle == IntPtr.Zero)
        {
            return;
        }

        if (turningUp)
        {
            // Ends on the even step at or above the current level.
            Send(AppCommandVolumeDown);
            Send(AppCommandVolumeUp);
        }
        else
        {
            // Ends on the even step at or below the current level.
            Send(AppCommandVolumeUp);
            Send(AppCommandVolumeDown);
        }
    }

    public void Dispose() => DestroyHandle();

    private void Send(int command) =>
        NativeMethods.SendMessageW(Handle, WmAppCommand, Handle, new IntPtr(command << 16));
}
