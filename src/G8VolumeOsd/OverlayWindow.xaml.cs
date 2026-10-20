using System;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using G8VolumeOsd.Audio;
using G8VolumeOsd.Interop;

namespace G8VolumeOsd;

/// <summary>
/// Reused, non-activating, click-through, topmost volume overlay.
/// Created once at startup; shown/hidden, never recreated.
/// </summary>
public partial class OverlayWindow : Window
{
    private const double WindowWidthDip = 260;
    private const double WindowHeightDip = 52;
    private const double TrackWidthDip = 146;
    private const double BottomMarginDip = 32;
    private static readonly TimeSpan HideDelay = TimeSpan.FromSeconds(2);

    // Segoe Fluent Icons / MDL2 code points.
    private const string GlyphMute = "";
    private const string GlyphVolume0 = "";
    private const string GlyphVolume1 = "";
    private const string GlyphVolume2 = "";
    private const string GlyphVolume3 = "";

    private readonly DispatcherTimer _hideTimer;
    private readonly IntPtr _hwnd;
    private ThemeColors? _theme;
    private VolumeState? _rendered;

    public OverlayWindow()
    {
        InitializeComponent();

        _hideTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = HideDelay };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        // Create the HWND now (hidden) so the first real show is cheap.
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
    }

    internal void ShowVolume(VolumeState state)
    {
        bool wasHidden = !IsVisible;
        if (wasHidden)
        {
            ApplyTheme(); // Picks up light/dark or accent changes; only on hidden->visible.
        }

        Render(state);

        if (wasHidden)
        {
            PlaceOnPrimaryMonitor();
            Show(); // ShowActivated=false => SW_SHOWNA, never takes focus.
            PlaceOnPrimaryMonitor();
        }

        // Restart the one-shot hide timer. It is stopped whenever the overlay is hidden.
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    internal void HideNow()
    {
        _hideTimer.Stop();
        Hide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        long style = NativeMethods.GetWindowExStyle(hwnd);
        style |= NativeMethods.WsExNoActivate | NativeMethods.WsExTransparent |
                 NativeMethods.WsExToolWindow | NativeMethods.WsExLayered;
        style &= ~NativeMethods.WsExAppWindow;
        NativeMethods.SetWindowExStyle(hwnd, style);

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (IsVisible)
        {
            PlaceOnPrimaryMonitor();
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmMouseActivate)
        {
            handled = true;
            return new IntPtr(NativeMethods.MaNoActivate);
        }

        return IntPtr.Zero;
    }

    private void Render(VolumeState state)
    {
        if (_rendered == state)
        {
            return;
        }

        _rendered = state;
        PercentText.Text = state.Percent.ToString(CultureInfo.InvariantCulture);
        TrackFill.Width = TrackWidthDip * state.Percent / 100.0;
        GlyphText.Text = state.Muted ? GlyphMute
            : state.Percent == 0 ? GlyphVolume0
            : state.Percent < 34 ? GlyphVolume1
            : state.Percent < 67 ? GlyphVolume2
            : GlyphVolume3;

        if (_theme != null)
        {
            TrackFill.Background = state.Muted ? _theme.MutedFill : _theme.Accent;
            PercentText.Foreground = state.Muted ? _theme.SecondaryText : _theme.Text;
        }
    }

    private void ApplyTheme()
    {
        ThemeColors theme = ThemeColors.Read();
        if (theme.Equals(_theme))
        {
            return;
        }

        _theme = theme;
        Panel.Background = theme.Background;
        Panel.BorderBrush = theme.Border;
        TrackBackground.Background = theme.TrackBackground;
        GlyphText.Foreground = theme.Text;
        _rendered = null; // Force foreground/fill brushes to refresh.
    }

    /// <summary>Bottom-center of the primary monitor's work area, in physical pixels.</summary>
    private void PlaceOnPrimaryMonitor()
    {
        IntPtr monitor = NativeMethods.MonitorFromPoint(default, NativeMethods.MonitorDefaultToPrimary);
        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        double scale = 1.0;
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out uint dpiX, out _) == 0 && dpiX > 0)
        {
            scale = dpiX / 96.0;
        }

        int width = (int)Math.Round(WindowWidthDip * scale);
        int height = (int)Math.Round(WindowHeightDip * scale);
        NativeMethods.Rect work = info.Work;
        int x = work.Left + ((work.Right - work.Left) - width) / 2;
        int y = work.Bottom - height - (int)Math.Round(BottomMarginDip * scale);

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HwndTopmost, x, y, 0, 0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }
}
