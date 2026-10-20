using System;
using System.Runtime.InteropServices;

namespace G8VolumeOsd.Interop;

// Minimal Windows Core Audio (MMDevice API + IAudioEndpointVolume) declarations.
//
// Only the methods this app calls are declared with real signatures. Vtable slots
// for methods we never call (including every Set*/Mute writer on
// IAudioEndpointVolume) are declared as parameterless "VtblGap" placeholders: they
// exist only to keep the vtable layout correct and cannot be used to change audio
// state. This makes "the app never writes audio settings" checkable by reading
// this one file.

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;

    public PropertyKey(Guid formatId, int propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }
}

// PROPVARIANT, sized for both x86 (16 bytes) and x64 (24 bytes).
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort VarType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Data1;
    public IntPtr Data2;
}

internal static class CoreAudioConstants
{
    public const uint DeviceStateActive = 0x00000001;
    public const uint ClsctxAll = 0x1 | 0x2 | 0x4 | 0x10;
    public const uint StgmRead = 0x00000000;
    public const ushort VtLpwstr = 31;

    /// <summary>HRESULT_FROM_WIN32(ERROR_NOT_FOUND): no such device / no default device.</summary>
    public const int ENotFound = unchecked((int)0x80070490);

    public static readonly PropertyKey PKeyDeviceFriendlyName =
        new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant pvar);
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int Item(uint index, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [PreserveSig]
    int OpenPropertyStore(uint access, out IPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out uint state);
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, out PropertyKey key);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);

    // SetValue and Commit intentionally not declared (read-only use).
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    [PreserveSig]
    int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    [PreserveSig]
    int GetChannelCount(out uint channelCount);

    // Slot: SetMasterVolumeLevel - never called.
    [PreserveSig]
    int VtblGap_SetMasterVolumeLevel();

    // Slot: SetMasterVolumeLevelScalar - never called.
    [PreserveSig]
    int VtblGap_SetMasterVolumeLevelScalar();

    [PreserveSig]
    int GetMasterVolumeLevel(out float levelDb);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    // Slot: SetChannelVolumeLevel - never called.
    [PreserveSig]
    int VtblGap_SetChannelVolumeLevel();

    // Slot: SetChannelVolumeLevelScalar - never called.
    [PreserveSig]
    int VtblGap_SetChannelVolumeLevelScalar();

    // Slot: GetChannelVolumeLevel - unused.
    [PreserveSig]
    int VtblGap_GetChannelVolumeLevel();

    // Slot: GetChannelVolumeLevelScalar - unused.
    [PreserveSig]
    int VtblGap_GetChannelVolumeLevelScalar();

    // Slot: SetMute - never called.
    [PreserveSig]
    int VtblGap_SetMute();

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);

    // Remaining methods (GetVolumeStepInfo, VolumeStepUp/Down, QueryHardwareSupport,
    // GetVolumeRange) are intentionally not declared.
}

[ComImport]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    /// <param name="notifyData">Pointer to AUDIO_VOLUME_NOTIFICATION_DATA.</param>
    [PreserveSig]
    int OnNotify(IntPtr notifyData);
}
