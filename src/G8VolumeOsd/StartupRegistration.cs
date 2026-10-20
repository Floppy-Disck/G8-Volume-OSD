using System;
using Microsoft.Win32;

namespace G8VolumeOsd;

/// <summary>
/// Per-user "launch at login" via HKCU\...\Run. No admin rights, no scheduled tasks.
/// Off by default; turning it off deletes the value again.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "G8 Volume OSD";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>True only if the Run entry points at this executable.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                key.SetValue(ValueName, Command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Updating launch-at-login failed", ex);
        }
    }
}
