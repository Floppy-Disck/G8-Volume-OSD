using System;
using System.IO;

namespace G8VolumeOsd;

/// <summary>
/// Appends rare errors (unhandled exceptions, failed settings writes) to
/// %LOCALAPPDATA%\G8VolumeOSD\error.log. Never called on the per-volume-event path.
/// </summary>
internal static class ErrorLog
{
    private const long MaxBytes = 256 * 1024;

    public static void Write(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            string path = Path.Combine(AppSettings.DataDirectory, "error.log");
            var file = new FileInfo(path);
            if (file.Exists && file.Length > MaxBytes)
            {
                file.Delete();
            }

            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Logging must never take the app down.
        }
    }
}
