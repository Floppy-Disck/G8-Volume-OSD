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
    private bool _systemEventsHooked;

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

        // Shows the current level if a device is attached; otherwise a sample value.
        _overlay.ShowVolume(_monitor.TryGetCurrentState(out VolumeState state) ? state : new VolumeState(50, false));
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
        _overlay?.Close();

        if (_instanceMutex != null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private void OnVolumeChanged(VolumeState state)
    {
        if (_settings.OverlayEnabled)
        {
            _overlay?.ShowVolume(state);
        }
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
