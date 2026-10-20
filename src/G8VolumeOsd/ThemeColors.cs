using System;
using System.Windows.Media;
using Microsoft.Win32;

namespace G8VolumeOsd;

/// <summary>Windows 11-like flyout colors derived from the system light/dark mode and accent color.</summary>
internal sealed record ThemeColors(
    bool Light,
    Color AccentColor,
    SolidColorBrush Background,
    SolidColorBrush Border,
    SolidColorBrush Text,
    SolidColorBrush SecondaryText,
    SolidColorBrush TrackBackground,
    SolidColorBrush MutedFill,
    SolidColorBrush Accent)
{
    public bool Equals(ThemeColors? other) =>
        other is not null && other.Light == Light && other.AccentColor == AccentColor;

    public override int GetHashCode() => HashCode.Combine(Light, AccentColor);

    public static ThemeColors Read()
    {
        bool light = IsSystemLightTheme();
        Color accent = ReadAccent(light);

        return light
            ? new ThemeColors(
                true,
                accent,
                Brush(0xF5, 0xF9, 0xF9, 0xF9),
                Brush(0x26, 0x00, 0x00, 0x00),
                Brush(0xFF, 0x1A, 0x1A, 0x1A),
                Brush(0xFF, 0x70, 0x70, 0x70),
                Brush(0x45, 0x00, 0x00, 0x00),
                Brush(0xFF, 0x90, 0x90, 0x90),
                Brush(accent))
            : new ThemeColors(
                false,
                accent,
                Brush(0xF5, 0x2C, 0x2C, 0x2C),
                Brush(0x30, 0xFF, 0xFF, 0xFF),
                Brush(0xFF, 0xFF, 0xFF, 0xFF),
                Brush(0xFF, 0xA0, 0xA0, 0xA0),
                Brush(0x60, 0xFF, 0xFF, 0xFF),
                Brush(0xFF, 0x80, 0x80, 0x80),
                Brush(accent));
    }

    /// <summary>Taskbar / system flyouts follow "SystemUsesLightTheme", not the app theme.</summary>
    public static bool IsSystemLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Color ReadAccent(bool light)
    {
        try
        {
            // AccentPalette: 8 RGBA entries, Light3..Dark3. Windows 11 fills sliders with
            // Light2 in dark mode and Dark1 in light mode.
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (key?.GetValue("AccentPalette") is byte[] palette && palette.Length >= 32)
            {
                int index = light ? 4 : 1;
                return Color.FromRgb(palette[index * 4], palette[index * 4 + 1], palette[index * 4 + 2]);
            }
        }
        catch (Exception)
        {
            // Fall through to defaults.
        }

        return light ? Color.FromRgb(0x00, 0x5F, 0xB8) : Color.FromRgb(0x60, 0xCD, 0xFF);
    }

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) => Brush(Color.FromArgb(a, r, g, b));

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
