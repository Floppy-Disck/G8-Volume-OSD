using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using G8VolumeOsd.Interop;

namespace G8VolumeOsd.Audio;

internal readonly record struct VolumeState(int Percent, bool Muted);

internal readonly record struct PlaybackDevice(string Id, string Name);

/// <summary>
/// Observes the master volume / mute of one playback endpoint via
/// IAudioEndpointVolumeCallback and follows device changes via IMMNotificationClient.
///
/// Read-only: this class never changes volume, mute, defaults or any other audio state.
///
/// Threading: every COM call and all state changes happen on the UI dispatcher thread.
/// Audio callbacks arrive on system worker threads; they only store the latest value
/// in a lock-free mailbox and post at most one dispatcher operation at a time, so fast
/// knob movement collapses into a single pending UI update.
/// </summary>
internal sealed class VolumeMonitor : IDisposable
{
    /// <summary>Ignore notifications immediately after (re)attaching so a reconnect never shows the overlay.</summary>
    private const int AttachGraceMs = 300;

    /// <summary>Bounded one-shot retries after failures; afterwards wait for the next device event.</summary>
    private static readonly int[] RetryDelaysMs = { 2000, 5000, 15000 };

    private readonly Dispatcher _dispatcher;
    private readonly Action _processVolume;
    private readonly Action _processDeviceChange;
    private readonly DispatcherTimer _restartTimer;

    private IMMDeviceEnumerator? _enumerator;
    private DeviceNotificationClient? _deviceClient;
    private IMMDevice? _device;
    private IAudioEndpointVolume? _endpointVolume;
    private EndpointVolumeCallback? _volumeCallback;

    private string? _selectedDeviceId;
    private string? _attachedDeviceId;
    private int _generation;
    private VolumeState _lastState;
    private long _attachedAtMs;
    private int _retryAttempt;
    private bool _disposed;

    // Cross-thread mailbox written by audio callbacks.
    // Layout: (generation << 32) | (muted ? 0x100 : 0) | percent
    private long _pendingVolume;
    private int _volumeDispatchQueued;
    private int _deviceDispatchQueued;
    private int _attachedDeviceInvalidated;

    public VolumeMonitor(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _processVolume = ProcessVolume;
        _processDeviceChange = ProcessDeviceChange;
        _restartTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _restartTimer.Tick += (_, _) =>
        {
            _restartTimer.Stop();
            Restart();
        };
    }

    /// <summary>Raised on the UI thread when the observed volume or mute state changes.</summary>
    public event Action<VolumeState>? VolumeChanged;

    /// <summary>Raised on the UI thread when the attached device (or lack of one) may have changed.</summary>
    public event Action? StatusChanged;

    public bool IsAttached => _endpointVolume != null;

    public string? AttachedDeviceName { get; private set; }

    /// <summary>Null means "follow the Windows default playback device".</summary>
    public string? SelectedDeviceId => _selectedDeviceId;

    public void Start(string? selectedDeviceId)
    {
        _selectedDeviceId = selectedDeviceId;
        Restart();
    }

    public void SelectDevice(string? deviceId)
    {
        if (_disposed)
        {
            return;
        }

        _selectedDeviceId = deviceId;
        _retryAttempt = 0;
        if (_enumerator == null)
        {
            Restart();
        }
        else
        {
            Reconcile();
        }
    }

    /// <summary>Schedules a full rebuild (used after resume from sleep).</summary>
    public void RestartAfter(TimeSpan delay)
    {
        if (_disposed)
        {
            return;
        }

        _retryAttempt = 0;
        _restartTimer.Stop();
        _restartTimer.Interval = delay;
        _restartTimer.Start();
    }

    /// <summary>
    /// Cheap health check, called when the user opens the tray menu. If the audio service
    /// was restarted, our COM objects are dead; rebuild them. No polling is involved.
    /// </summary>
    public void Heal()
    {
        if (_disposed)
        {
            return;
        }

        if (_endpointVolume != null)
        {
            int hr;
            try
            {
                hr = _endpointVolume.GetMasterVolumeLevelScalar(out _);
            }
            catch (Exception)
            {
                hr = -1;
            }

            if (hr >= 0)
            {
                return;
            }
        }

        _retryAttempt = 0;
        Restart();
    }

    public bool TryGetCurrentState(out VolumeState state)
    {
        state = default;
        if (_endpointVolume == null)
        {
            return false;
        }

        try
        {
            if (_endpointVolume.GetMasterVolumeLevelScalar(out float level) < 0 ||
                _endpointVolume.GetMute(out bool muted) < 0)
            {
                return false;
            }

            state = ToState(level, muted);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public List<PlaybackDevice> GetActivePlaybackDevices()
    {
        var result = new List<PlaybackDevice>();
        if (_enumerator == null)
        {
            return result;
        }

        IMMDeviceCollection? collection = null;
        try
        {
            if (_enumerator.EnumAudioEndpoints(EDataFlow.Render, CoreAudioConstants.DeviceStateActive, out collection) < 0 ||
                collection.GetCount(out uint count) < 0)
            {
                return result;
            }

            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    if (collection.Item(i, out device) >= 0 && device.GetId(out string id) >= 0)
                    {
                        result.Add(new PlaybackDevice(id, GetFriendlyName(device)));
                    }
                }
                finally
                {
                    Release(device);
                }
            }
        }
        catch (Exception)
        {
            // Audio service unavailable; return what we have.
        }
        finally
        {
            Release(collection);
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _restartTimer.Stop();
        Detach();
        ReleaseEnumerator();
    }

    // ---------------------------------------------------------------- UI thread

    /// <summary>Recreates the enumerator and device subscription from scratch.</summary>
    private void Restart()
    {
        if (_disposed)
        {
            return;
        }

        _restartTimer.Stop();
        Detach();
        ReleaseEnumerator();

        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            var client = new DeviceNotificationClient(this);
            ThrowIfFailed(_enumerator.RegisterEndpointNotificationCallback(client));
            _deviceClient = client;
        }
        catch (Exception)
        {
            ReleaseEnumerator();
            ScheduleRetry();
            StatusChanged?.Invoke();
            return;
        }

        Reconcile();
    }

    /// <summary>Attaches to the endpoint that should currently be observed, if any.</summary>
    private void Reconcile()
    {
        if (_disposed || _enumerator == null)
        {
            return;
        }

        try
        {
            string? targetId = ResolveTargetId(_enumerator);
            if (targetId == null)
            {
                // Selected device unplugged, or no playback device at all.
                // Wait for a device notification; never fall back to another device.
                Detach();
            }
            else if (_endpointVolume == null ||
                     !string.Equals(targetId, _attachedDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                Detach();
                Attach(targetId);
            }

            _retryAttempt = 0;
        }
        catch (Exception)
        {
            Detach();
            ScheduleRetry();
        }

        StatusChanged?.Invoke();
    }

    private string? ResolveTargetId(IMMDeviceEnumerator enumerator)
    {
        IMMDevice? device = null;
        try
        {
            int hr;
            if (_selectedDeviceId != null)
            {
                hr = enumerator.GetDevice(_selectedDeviceId, out device);
                if (hr == CoreAudioConstants.ENotFound)
                {
                    return null;
                }

                ThrowIfFailed(hr);
                ThrowIfFailed(device.GetState(out uint state));
                return state == CoreAudioConstants.DeviceStateActive ? _selectedDeviceId : null;
            }

            hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out device);
            if (hr == CoreAudioConstants.ENotFound)
            {
                return null;
            }

            ThrowIfFailed(hr);
            ThrowIfFailed(device.GetId(out string id));
            return id;
        }
        finally
        {
            Release(device);
        }
    }

    private void Attach(string deviceId)
    {
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        try
        {
            ThrowIfFailed(_enumerator!.GetDevice(deviceId, out device));

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            ThrowIfFailed(device.Activate(ref iid, CoreAudioConstants.ClsctxAll, IntPtr.Zero, out object instance));
            volume = (IAudioEndpointVolume)instance;

            ThrowIfFailed(volume.GetMasterVolumeLevelScalar(out float level));
            ThrowIfFailed(volume.GetMute(out bool muted));

            int generation = ++_generation;
            var callback = new EndpointVolumeCallback(this, generation);
            ThrowIfFailed(volume.RegisterControlChangeNotify(callback));

            _device = device;
            _endpointVolume = volume;
            _volumeCallback = callback;
            _lastState = ToState(level, muted);
            _attachedAtMs = Environment.TickCount64;
            AttachedDeviceName = GetFriendlyName(device);
            Volatile.Write(ref _attachedDeviceId, deviceId);
        }
        catch
        {
            Release(volume);
            Release(device);
            throw;
        }
    }

    private void Detach()
    {
        _generation++; // Invalidates any notification still in flight.
        Volatile.Write(ref _attachedDeviceId, null);
        AttachedDeviceName = null;

        if (_endpointVolume != null && _volumeCallback != null)
        {
            try
            {
                _endpointVolume.UnregisterControlChangeNotify(_volumeCallback);
            }
            catch (Exception)
            {
                // Service gone; nothing to unregister from.
            }
        }

        Release(_endpointVolume);
        Release(_device);
        _endpointVolume = null;
        _device = null;
        _volumeCallback = null;
    }

    private void ReleaseEnumerator()
    {
        if (_enumerator != null && _deviceClient != null)
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(_deviceClient);
            }
            catch (Exception)
            {
                // Service gone.
            }
        }

        Release(_enumerator);
        _enumerator = null;
        _deviceClient = null;
    }

    private void ScheduleRetry()
    {
        if (_disposed || _retryAttempt >= RetryDelaysMs.Length)
        {
            return; // Give up until the next device event, resume, or tray menu open.
        }

        _restartTimer.Stop();
        _restartTimer.Interval = TimeSpan.FromMilliseconds(RetryDelaysMs[_retryAttempt++]);
        _restartTimer.Start();
    }

    private void ProcessVolume()
    {
        // Full fence before reading, so a notification that lands after this point posts again.
        Interlocked.Exchange(ref _volumeDispatchQueued, 0);
        long packed = Interlocked.Read(ref _pendingVolume);

        if (_disposed || _endpointVolume == null || (int)(packed >> 32) != _generation)
        {
            return;
        }

        int bits = (int)(packed & 0xFFFFFFFF);
        var state = new VolumeState(bits & 0xFF, (bits & 0x100) != 0);
        if (state == _lastState)
        {
            return;
        }

        _lastState = state;
        if (Environment.TickCount64 - _attachedAtMs < AttachGraceMs)
        {
            return;
        }

        VolumeChanged?.Invoke(state);
    }

    private void ProcessDeviceChange()
    {
        Interlocked.Exchange(ref _deviceDispatchQueued, 0);
        if (_disposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref _attachedDeviceInvalidated, 0) == 1)
        {
            Detach(); // Our endpoint changed state or was removed; re-resolve from scratch.
        }

        _retryAttempt = 0;
        if (_enumerator == null)
        {
            Restart();
        }
        else
        {
            Reconcile();
        }
    }

    // ------------------------------------------------------- audio worker threads

    private void OnVolumeNotification(int generation, IntPtr data)
    {
        // AUDIO_VOLUME_NOTIFICATION_DATA: GUID guidEventContext; BOOL bMuted; float fMasterVolume; ...
        bool muted = Marshal.ReadInt32(data, 16) != 0;
        float level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
        int percent = ToPercent(level);

        long packed = ((long)generation << 32) | (uint)(percent | (muted ? 0x100 : 0));
        Interlocked.Exchange(ref _pendingVolume, packed);

        if (Interlocked.Exchange(ref _volumeDispatchQueued, 1) == 0)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, _processVolume);
        }
    }

    private void OnDeviceNotification(string? deviceId, bool mayInvalidateAttached)
    {
        if (mayInvalidateAttached && deviceId != null &&
            string.Equals(deviceId, Volatile.Read(ref _attachedDeviceId), StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Exchange(ref _attachedDeviceInvalidated, 1);
        }

        if (Interlocked.Exchange(ref _deviceDispatchQueued, 1) == 0)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, _processDeviceChange);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static VolumeState ToState(float level, bool muted) => new(ToPercent(level), muted);

    private static int ToPercent(float level)
    {
        if (float.IsNaN(level))
        {
            return 0;
        }

        return (int)MathF.Round(Math.Clamp(level, 0f, 1f) * 100f);
    }

    private static string GetFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            if (device.OpenPropertyStore(CoreAudioConstants.StgmRead, out store) < 0)
            {
                return "Unknown device";
            }

            PropertyKey key = CoreAudioConstants.PKeyDeviceFriendlyName;
            if (store.GetValue(ref key, out PropVariant value) < 0)
            {
                return "Unknown device";
            }

            try
            {
                return value.VarType == CoreAudioConstants.VtLpwstr
                    ? Marshal.PtrToStringUni(value.Data1) ?? "Unknown device"
                    : "Unknown device";
            }
            finally
            {
                CoreAudioConstants.PropVariantClear(ref value);
            }
        }
        catch (Exception)
        {
            return "Unknown device";
        }
        finally
        {
            Release(store);
        }
    }

    private static void ThrowIfFailed(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject == null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
        catch (Exception)
        {
            // Already released or service gone.
        }
    }

    // ------------------------------------------------------------- COM callbacks

    private sealed class EndpointVolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly VolumeMonitor _owner;
        private readonly int _generation;

        public EndpointVolumeCallback(VolumeMonitor owner, int generation)
        {
            _owner = owner;
            _generation = generation;
        }

        public int OnNotify(IntPtr notifyData)
        {
            try
            {
                if (notifyData != IntPtr.Zero)
                {
                    _owner.OnVolumeNotification(_generation, notifyData);
                }
            }
            catch (Exception)
            {
                // Never let an exception cross back into the audio service.
            }

            return 0;
        }
    }

    private sealed class DeviceNotificationClient : IMMNotificationClient
    {
        private readonly VolumeMonitor _owner;

        public DeviceNotificationClient(VolumeMonitor owner) => _owner = owner;

        public int OnDeviceStateChanged(string deviceId, uint newState) => Notify(deviceId, true);

        public int OnDeviceAdded(string deviceId) => Notify(deviceId, false);

        public int OnDeviceRemoved(string deviceId) => Notify(deviceId, true);

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) =>
            flow == EDataFlow.Render && role == ERole.Console ? Notify(null, false) : 0;

        // Fires frequently (e.g. on every property tweak); not relevant to us.
        public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;

        private int Notify(string? deviceId, bool mayInvalidateAttached)
        {
            try
            {
                _owner.OnDeviceNotification(deviceId, mayInvalidateAttached);
            }
            catch (Exception)
            {
                // Never let an exception cross back into the audio service.
            }

            return 0;
        }
    }
}
