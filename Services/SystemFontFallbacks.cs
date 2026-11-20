using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using SkiaSharp;

namespace Snipyard.Services;

/// <summary>
/// The fonts the operating system itself draws Japanese, Chinese, Korean and emoji in, handed
/// to Avalonia as its fallbacks. The UI font (Inter) has none of those characters, and for a
/// character its font lacks Avalonia first takes any font it has already loaded that has
/// it, asking the system only when none does. Listing the installed monospace fonts loads
/// every font on the machine, and from then on the labels' kana came out in whichever one
/// turned up first - a brush calligraphy face on one Mac. Fallbacks named here are tried
/// before the loaded fonts.
///
/// The names come from the system (CoreText on macOS, DirectWrite on Windows) rather than
/// from a list kept here, so the labels look as the rest of the desktop does, in whatever
/// language and with whatever fonts the machine is set up with.
/// </summary>
public static class SystemFontFallbacks
{
    // One character per script, with the language that picks the system's font for it.
    // CoreText ignores the language and answers by the system's preferred languages, but the
    // kana sample can only come back as a Japanese font, so that leads on any Mac. Its font
    // covers the kanji Chinese shares, and those stay Japanese forms in a Japanese UI.
    private static readonly (int CodePoint, string Language)[] Samples =
    {
        (0x3042, "ja"),        // あ
        (0x4E2D, "ja"),        // 中
        (0xD55C, "ko"),        // 한
        (0x7B80, "zh-Hans"),   // 简
        (0x1F600, "und"),      // 😀
    };

    /// <summary>
    /// Asks Skia rather than Avalonia: Avalonia's own lookup is the one that answers from
    /// the fonts already loaded. Empty if the system cannot be asked, which leaves Avalonia
    /// choosing as it did before.
    /// </summary>
    public static IReadOnlyList<FontFallback> Query()
    {
        var names = new List<string>();
        try
        {
            foreach (var (codePoint, language) in Samples)
            {
                using var typeface = SKFontManager.Default.MatchCharacter(
                    null, SKFontStyle.Normal, new[] { language }, codePoint);
                if (typeface?.FamilyName is { Length: > 0 } name && !names.Contains(name))
                    names.Add(name);
            }
            System.Diagnostics.Trace.WriteLine($"System font fallbacks: {string.Join(", ", names)}");
            return names.Select(n => new FontFallback { FontFamily = new FontFamily(n) }).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"System font fallbacks unavailable: {ex.Message}");
            return Array.Empty<FontFallback>();
        }
    }
}
