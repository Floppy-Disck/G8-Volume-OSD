using System;
using System.IO;
using System.Text.Json;

namespace G8VolumeOsd;

/// <summary>
/// User settings in %LOCALAPPDATA%\G8VolumeOSD\settings.json.
/// Written only when the user changes an option in the tray menu.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool OverlayEnabled { get; set; } = true;

    /// <summary>
    /// Experimental: show the built-in Windows volume flyout instead of the custom overlay.
    /// Unlike the default mode, this nudges the volume one step and back to open the flyout.
    /// </summary>
    public bool UseWindowsFlyout { get; set; }

    /// <summary>
    /// The Windows flyout closes about 2-3 s after it was opened, even while the volume keeps
    /// changing. While changes keep arriving, the flyout is re-opened once this many ms have
    /// passed since the last trigger. Keep it below the flyout's lifetime.
    /// </summary>
    public int WindowsFlyoutRefreshMs { get; set; } = 1200;

    /// <summary>Core Audio endpoint ID. Null means "follow the Windows default playback device".</summary>
    public string? DeviceId { get; set; }

    /// <summary>Last known friendly name of <see cref="DeviceId"/>, for display while it is unplugged.</summary>
    public string? DeviceName { get; set; }

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "G8VolumeOSD");

    private static string FilePath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // Corrupt or unreadable: start with defaults.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Saving settings failed", ex);
        }
    }
}
