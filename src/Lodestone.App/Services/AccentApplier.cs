using System.Windows;
using System.Windows.Media;

namespace Lodestone.App.Services;

/// <summary>A selectable accent colour. The first entry is the free default; the rest are supporter perks.</summary>
public sealed record AccentOption(string Name, string Hex, bool SupporterOnly);

/// <summary>The curated accent palette. Adding one here makes it appear in Settings automatically.</summary>
public static class SupporterAccents
{
    public const string DefaultHex = "#FF5AC26D";

    public static IReadOnlyList<AccentOption> All { get; } =
    [
        new("Classic Green", DefaultHex, SupporterOnly: false),
        new("Amber", "#FFE3B341", SupporterOnly: true),
        new("Violet", "#FFB57BE0", SupporterOnly: true),
        new("Cyan", "#FF4FC4D6", SupporterOnly: true),
        new("Coral", "#FFE2719A", SupporterOnly: true),
    ];

    public static bool IsDefault(string? hex)
        => string.IsNullOrWhiteSpace(hex) || string.Equals(hex, DefaultHex, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Applies an accent colour at runtime by recolouring the shared accent brushes in
/// <c>Themes/Theme.xaml</c>. Those brushes aren't frozen, so mutating <see cref="SolidColorBrush.Color"/>
/// updates every <c>StaticResource</c> consumer live — no XAML changes needed. Non-supporters always get
/// the default; a stored custom accent is ignored unless the caller is a supporter.
/// </summary>
public static class AccentApplier
{
    public static void Apply(string? hex, bool isSupporter)
    {
        string effective = isSupporter && !SupporterAccents.IsDefault(hex) ? hex! : SupporterAccents.DefaultHex;
        Color accent = Parse(effective);

        // Fully qualified: bare "Application" would bind to the Lodestone.Application namespace here.
        ResourceDictionary? res = System.Windows.Application.Current?.Resources;
        if (res is null)
        {
            return;
        }

        SetBrush(res, "AccentBrush", accent);
        SetBrush(res, "AccentHoverBrush", Lighten(accent, 0.14));
        SetBrush(res, "AccentSoftBrush", WithAlpha(accent, 0x24));
        SetBrush(res, "AccentFaintBrush", WithAlpha(accent, 0x12));
        SetBrush(res, "AccentTextBrush", ContrastText(accent));
        SetBrush(res, "AccentBorderBrush", WithAlpha(accent, 0x66));

        // Gradient accent roles. The brushes carry literal default stops in Theme.xaml; here we recolour the
        // accent-driven stop(s) on the shared instance so every StaticResource consumer updates live too.
        // The logo/heart tile is a light→dark accent diagonal; the supporter hero only tints its top stop.
        SetGradientStops(res, "AccentTileBrush", (0, Lighten(accent, 0.12)), (1, Darken(accent, 0.18)));
        SetGradientStops(res, "AccentHeroBrush", (0, WithAlpha(accent, 0x22)));

        if (res.Contains("AccentColor"))
        {
            res["AccentColor"] = accent;
        }
    }

    /// <summary>The accent currently applied to the app resources (the default before any apply). Lets non-WPF
    /// surfaces such as the WebView2 description read the same accent the brushes use. Reads it back from
    /// <c>AccentBrush</c> — the one resource <see cref="Apply"/> always updates — rather than the
    /// <c>AccentColor</c> token, which is only refreshed when present at the top level.</summary>
    public static Color CurrentAccent()
    {
        ResourceDictionary? res = System.Windows.Application.Current?.Resources;
        return res?["AccentBrush"] is SolidColorBrush b ? b.Color : Parse(SupporterAccents.DefaultHex);
    }

    /// <summary>The applied accent as an opaque CSS hex (e.g. "#5AC26D"), for HTML/CSS surfaces.</summary>
    public static string CurrentAccentHex()
    {
        Color c = CurrentAccent();
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public static Color Parse(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex)!;
        }
        catch (FormatException)
        {
            return (Color)ColorConverter.ConvertFromString(SupporterAccents.DefaultHex)!;
        }
    }

    private static void SetBrush(ResourceDictionary res, string key, Color color)
    {
        if (res[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color; // shared instance → updates every StaticResource binding live
        }
        else
        {
            res[key] = new SolidColorBrush(color);
        }
    }

    // Recolours specific stops of a shared gradient brush in place; stops left unlisted (e.g. the hero's
    // card-coloured tail) keep their XAML value. A missing or frozen brush is left untouched so the literal
    // default in Theme.xaml still stands.
    private static void SetGradientStops(ResourceDictionary res, string key, params (int Index, Color Color)[] stops)
    {
        if (res[key] is LinearGradientBrush brush && !brush.IsFrozen)
        {
            foreach ((int index, Color color) in stops)
            {
                if (index >= 0 && index < brush.GradientStops.Count)
                {
                    brush.GradientStops[index].Color = color;
                }
            }
        }
    }

    private static Color Lighten(Color c, double amount) => Color.FromArgb(
        c.A,
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));

    private static Color Darken(Color c, double amount) => Color.FromArgb(
        c.A,
        (byte)(c.R * (1 - amount)),
        (byte)(c.G * (1 - amount)),
        (byte)(c.B * (1 - amount)));

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    // Dark text on light accents, light text on dark ones (matches the prototype's accent-text role).
    private static Color ContrastText(Color c)
    {
        double luminance = (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
        return luminance > 150 ? Color.FromRgb(0x10, 0x13, 0x0F) : Color.FromRgb(0xF2, 0xF2, 0xF4);
    }
}
