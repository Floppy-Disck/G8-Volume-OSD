using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using G8VolumeOsd.Audio;
using Microsoft.Win32;

namespace G8VolumeOsd;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\G8VolumeOSD-3F6C2A61-8E4B-4B2D-9C8A-5D1E7B0A9F42";
    private static readonly TimeSpan ResumeSettleDelay = TimeSpan.FromSeconds(2);

    private Mutex? _instanceMutex;
    private AppSettings _settings = new();
    private OverlayWindow? _overlay;
    private VolumeMonitor? _monitor;
    private TrayIcon? _tray;
    private WindowsFlyoutTrigger? _flyout;
    private bool _systemEventsHooked;

    // Experimental Windows-flyout mode.
    private long _lastFlyoutTriggerMs = long.MinValue / 2;
    private bool _flyoutActive;

    internal bool UseWindowsFlyout => _settings.UseWindowsFlyout;

    internal void SetUseWindowsFlyout(bool enabled)
    {
        _settings.UseWindowsFlyout = enabled;
        _settings.Save();
        _flyoutActive = false;
        if (enabled)
        {
            _overlay?.HideNow();
        }
    }

    internal void SetOverlayEnabled(bool enabled)
    {
        _settings.OverlayEnabled = enabled;
        _settings.Save();
        if (!enabled)
        {
            _overlay?.HideNow();
        }
    }

    internal void SelectDevice(string? deviceId, string? deviceName)
    {
        _settings.DeviceId = deviceId;
        _settings.DeviceName = deviceName;
        _settings.Save();
        _monitor?.SelectDevice(deviceId);
    }

    internal void PreviewOverlay()
    {
        if (_overlay == null || _monitor == null)
        {
            return;
        }

        bool attached = _monitor.TryGetCurrentState(out VolumeState state);
        if (attached && _settings.UseWindowsFlyout && !state.Muted)
        {
            // Round down, never up, when the user only asked for a preview.
            _overlay.HideNow();
            TriggerWindowsFlyout(turningUp: false);
            return;
        }

        // Shows the current level if a device is attached; otherwise a sample value.
        _overlay.ShowVolume(attached ? state : new VolumeState(50, false));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            MessageBox.Show("G8 Volume OSD is already running in the notification area.",
                "G8 Volume OSD", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _settings = AppSettings.Load();
        _overlay = new OverlayWindow();
        _flyout = new WindowsFlyoutTrigger();
        _monitor = new VolumeMonitor(Dispatcher);
        _tray = new TrayIcon(this, _settings, _monitor);

        _monitor.VolumeChanged += OnVolumeChanged;
        _monitor.StatusChanged += _tray.UpdateStatus;
        _monitor.Start(_settings.DeviceId);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _systemEventsHooked = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_systemEventsHooked)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        _monitor?.Dispose();
        _tray?.Dispose();
        _flyout?.Dispose();
        _overlay?.Close();

        if (_instanceMutex != null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private void OnVolumeChanged(VolumeState previous, VolumeState current)
    {
        if (!_settings.OverlayEnabled)
        {
            return;
        }

        // Volume keys unmute in Windows, so mute changes (and anything while muted) always
        // use the custom overlay, even in Windows-flyout mode.
        if (_settings.UseWindowsFlyout && !previous.Muted && !current.Muted)
        {
            // The Windows flyout shows live values while open but closes ~2-3 s after it was
            // opened. Open it at the start of a turn and re-open it while the turn continues.
            // The step-and-back from our own trigger arrives within milliseconds, well inside
            // the refresh interval, so it can never retrigger itself.
            long sinceTrigger = Environment.TickCount64 - _lastFlyoutTriggerMs;
            if (!_flyoutActive || sinceTrigger >= _settings.WindowsFlyoutRefreshMs)
            {
                _overlay?.HideNow();
                TriggerWindowsFlyout(turningUp: current.Percent >= previous.Percent);
            }

            return;
        }

        _flyoutActive = false;
        _overlay?.ShowVolume(current);
    }

    private void TriggerWindowsFlyout(bool turningUp)
    {
        _flyout?.Show(turningUp);
        _lastFlyoutTriggerMs = Environment.TickCount64;
        _flyoutActive = true;
    }

    // SystemEvents may raise on its own thread; always hop to the dispatcher.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        PowerModes mode = e.Mode;
        Dispatcher.BeginInvoke(() =>
        {
            if (mode == PowerModes.Suspend)
            {
                _overlay?.HideNow();
            }
            else if (mode == PowerModes.Resume)
            {
                // Endpoints may be re-enumerated after resume; rebuild once after they settle.
                _monitor?.RestartAfter(ResumeSettleDelay);
            }
        });
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Dispatcher.BeginInvoke(() => _tray?.RefreshIcon());
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write("Unhandled exception", e.Exception);

        // Audio/COM failures must not take the tray app down; the monitor recovers on the next event.
        if (e.Exception is COMException or InvalidComObjectException or InvalidCastException)
        {
            e.Handled = true;
        }
    }
}
