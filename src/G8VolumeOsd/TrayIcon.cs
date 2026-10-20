using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;
using G8VolumeOsd.Audio;
using G8VolumeOsd.Interop;

namespace G8VolumeOsd;

/// <summary>
/// Notification-area icon and menu. The menu is rebuilt each time it opens, so device
/// lists and checkmarks are always current without any background refresh.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const string AppName = "G8 Volume OSD";

    private readonly App _app;
    private readonly AppSettings _settings;
    private readonly VolumeMonitor _monitor;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private Icon? _icon;

    public TrayIcon(App app, AppSettings settings, VolumeMonitor monitor)
    {
        _app = app;
        _settings = settings;
        _monitor = monitor;

        _menu = new ContextMenuStrip();
        _menu.Opening += OnMenuOpening;

        _notifyIcon = new NotifyIcon
        {
            Text = AppName,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _app.PreviewOverlay();
            }
        };

        RefreshIcon();
        _notifyIcon.Visible = true;
    }

    /// <summary>Updates the tooltip after the monitored device changes.</summary>
    public void UpdateStatus()
    {
        string text = $"{AppName}\n{StatusText()}";
        if (text.Length > 127)
        {
            text = text[..127];
        }

        if (_notifyIcon.Text != text)
        {
            _notifyIcon.Text = text;
        }
    }

    /// <summary>Redraws the icon to match the taskbar's light/dark mode.</summary>
    public void RefreshIcon()
    {
        Icon? old = _icon;
        _icon = CreateIcon(ThemeColors.IsSystemLightTheme());
        _notifyIcon.Icon = _icon;
        old?.Dispose();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon?.Dispose();
    }

    private string StatusText()
    {
        bool followDefault = _settings.DeviceId == null;
        if (_monitor.IsAttached)
        {
            return followDefault
                ? $"Default: {_monitor.AttachedDeviceName}"
                : $"Device: {_monitor.AttachedDeviceName}";
        }

        return followDefault
            ? "No playback device available"
            : $"Waiting for {_settings.DeviceName ?? "selected device"} (disconnected)";
    }

    private void OnMenuOpening(object? sender, CancelEventArgs e)
    {
        // Self-heal if the audio service was restarted since the last check.
        _monitor.Heal();
        UpdateStatus();

        while (_menu.Items.Count > 0)
        {
            _menu.Items[0].Dispose();
        }

        _menu.Items.Add(new ToolStripMenuItem(Escape(StatusText())) { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());

        var overlay = new ToolStripMenuItem("Show volume overlay") { Checked = _settings.OverlayEnabled };
        overlay.Click += (_, _) => _app.SetOverlayEnabled(!_settings.OverlayEnabled);
        _menu.Items.Add(overlay);

        var windowsFlyout = new ToolStripMenuItem("Use Windows volume popup (experimental)")
        {
            Checked = _app.UseWindowsFlyout,
            Enabled = _settings.OverlayEnabled,
            ToolTipText = "Opens the built-in Windows 11 volume popup when the knob starts turning. " +
                          "Briefly steps the volume down and up (snaps to the 2% grid). Mute still uses the custom overlay.",
        };
        windowsFlyout.Click += (_, _) => _app.SetUseWindowsFlyout(!_app.UseWindowsFlyout);
        _menu.Items.Add(windowsFlyout);

        _menu.Items.Add(BuildDeviceMenu());

        var preview = new ToolStripMenuItem("Preview overlay");
        preview.Click += (_, _) => _app.PreviewOverlay();
        _menu.Items.Add(preview);

        var startup = new ToolStripMenuItem("Launch at login") { Checked = StartupRegistration.IsEnabled() };
        startup.Click += (_, _) => StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled());
        _menu.Items.Add(startup);

        _menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => _app.Shutdown();
        _menu.Items.Add(exit);

        e.Cancel = false;
    }

    private ToolStripMenuItem BuildDeviceMenu()
    {
        var root = new ToolStripMenuItem("Playback device");

        var followDefault = new ToolStripMenuItem("Default playback device (follow Windows)")
        {
            Checked = _settings.DeviceId == null,
        };
        followDefault.Click += (_, _) => _app.SelectDevice(null, null);
        root.DropDownItems.Add(followDefault);
        root.DropDownItems.Add(new ToolStripSeparator());

        bool selectedPresent = false;
        foreach (PlaybackDevice device in _monitor.GetActivePlaybackDevices())
        {
            bool isSelected = string.Equals(device.Id, _settings.DeviceId, StringComparison.OrdinalIgnoreCase);
            selectedPresent |= isSelected;

            var item = new ToolStripMenuItem(Escape(device.Name)) { Checked = isSelected };
            string id = device.Id;
            string name = device.Name;
            item.Click += (_, _) => _app.SelectDevice(id, name);
            root.DropDownItems.Add(item);
        }

        if (_settings.DeviceId != null && !selectedPresent)
        {
            // Keep showing the explicit choice while it is unplugged; we never switch away silently.
            root.DropDownItems.Add(new ToolStripMenuItem(
                Escape($"{_settings.DeviceName ?? "Selected device"} (disconnected)"))
            {
                Checked = true,
                Enabled = false,
            });
        }

        return root;
    }

    private static string Escape(string text) => text.Replace("&", "&&");

    private static Icon CreateIcon(bool lightTaskbar)
    {
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        Color color = lightTaskbar ? Color.FromArgb(0x1C, 0x1C, 0x1C) : Color.White;

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using Font? font = CreateGlyphFont(size);
            using var brush = new SolidBrush(color);
            if (font != null)
            {
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                };
                g.DrawString("", font, brush, new RectangleF(0, 0, size, size), format);
            }
            else
            {
                DrawFallbackSpeaker(g, brush, size);
            }
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static Font? CreateGlyphFont(int size)
    {
        foreach (string family in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            var font = new Font(family, size * 0.8f, FontStyle.Regular, GraphicsUnit.Pixel);
            if (string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            font.Dispose();
        }

        return null;
    }

    private static void DrawFallbackSpeaker(Graphics g, Brush brush, int size)
    {
        float s = size / 16f;
        g.FillPolygon(brush, new[]
        {
            new PointF(2 * s, 6 * s), new PointF(5 * s, 6 * s), new PointF(9 * s, 2 * s),
            new PointF(9 * s, 14 * s), new PointF(5 * s, 10 * s), new PointF(2 * s, 10 * s),
        });
        using var pen = new Pen(brush, 1.3f * s);
        g.DrawArc(pen, 7 * s, 5 * s, 6 * s, 6 * s, -60, 120);
    }
}
