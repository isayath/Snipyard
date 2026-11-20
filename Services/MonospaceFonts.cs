using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Media;

namespace Snipyard.Services;

/// <summary>
/// The fonts the terminal can be set to: those installed on this machine whose ASCII characters
/// all advance by the same width. The grid puts one character in each column, so a proportional
/// font cannot line anything up, and a font that is not installed is quietly drawn in some
/// other face the settings know nothing about.
///
/// A font is called Japanese when it carries its own kana and kanji at exactly two ASCII
/// widths. Only then does a full-width character fill its two columns; a Latin-only font hands
/// Japanese to the system fallback, whose glyphs are narrower than two of its columns and leave
/// a gap beside every character.
/// </summary>
public static class MonospaceFonts
{
    public sealed record Choice(string Name, bool Japanese)
    {
        // What the settings list shows; the setting itself stores Name
        public override string ToString() =>
            Japanese ? $"{Name}  ({Loc.Get("FontJapaneseCapable")})" : Name;
    }

    private static IReadOnlyList<Choice>? _installed;
    private static readonly Dictionary<string, string> _resolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every installed monospace font, Japanese ones first. Built on first use: reading every
    /// family takes over a tenth of a second on a Mac with a couple of hundred installed.
    /// </summary>
    public static IReadOnlyList<Choice> Installed => _installed ??= Scan();

    private static List<Choice> Scan()
    {
        var list = new List<Choice>();
        foreach (var family in FontManager.Current.SystemFonts)
        {
            // macOS lists its private UI faces (".SF NS Mono" and the like) under a leading dot
            if (family.Name.StartsWith('.')) continue;
            if (Probe(family.Name) is { } choice) list.Add(choice);
        }
        return list
            .OrderByDescending(c => c.Japanese)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The font to draw in for a setting of <paramref name="name"/>: the font itself when it is
    /// installed and monospace, otherwise a stand-in that is. The setting is left as it is; it
    /// is shared with other machines, where the font it names may well be installed.
    /// </summary>
    public static string Resolve(string name)
    {
        if (_resolved.TryGetValue(name, out var cached)) return cached;
        string resolved = Probe(name) != null ? name
            : Installed.FirstOrDefault(c => c.Japanese)?.Name
              ?? (OperatingSystem.IsWindows() ? "Consolas" : "Menlo");
        _resolved[name] = resolved;
        return resolved;
    }

    /// <summary>
    /// The list entry for <paramref name="name"/>. A setting may spell the font differently
    /// from the list: in another case, or by its English name where Japanese Windows lists
    /// the localized one ("MS Gothic" against "ＭＳ ゴシック"), so the installed faces are
    /// compared when the names are not the same.
    /// </summary>
    public static Choice? Find(string name)
    {
        var match = Installed.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match != null || !TryGetInstalled(name, out var g)) return match;
        return Installed.FirstOrDefault(c => TryGetInstalled(c.Name, out var other) && other.FamilyName == g.FamilyName);
    }

    /// <summary>
    /// The font installed under <paramref name="name"/>. FontManager does not fail for a family
    /// that is not installed: it hands back a stand-in (Helvetica on macOS), whose metrics have
    /// nothing to do with the font the text layout then falls through to. The resolved family
    /// has to carry the requested name; "Hiragino Sans" comes back as "Hiragino Sans W3".
    /// </summary>
    public static bool TryGetInstalled(string name, [NotNullWhen(true)] out GlyphTypeface? glyphTypeface)
    {
        if (FontManager.Current.TryGetGlyphTypeface(new Typeface(name), out glyphTypeface)
            && (glyphTypeface.FamilyName.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                // Japanese Windows may report a localized name first ("ＭＳ ゴシック" for MS Gothic)
                || glyphTypeface.FamilyNames.Values.Any(n => n.StartsWith(name, StringComparison.OrdinalIgnoreCase))))
            return true;
        glyphTypeface = null;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is an installed monospace font, and whether it is a
    /// Japanese one. Decided by the advance widths rather than the font's fixed-pitch flag:
    /// the flag is not set on Monaco, a monospace font, and a terminal only cares that the
    /// characters it draws one to a column all take the same width.
    /// </summary>
    private static Choice? Probe(string name)
    {
        if (!TryGetInstalled(name, out var g)) return null;

        int? width = null;
        for (int c = 0x20; c <= 0x7E; c++)
        {
            if (Advance(g, c) is not { } w || w == 0) return null;   // no ASCII glyph: not a terminal font
            if (width == null) width = w;
            else if (w != width) return null;
        }

        bool japanese = Advance(g, 'あ') == 2 * width && Advance(g, '漢') == 2 * width;
        return new Choice(name, japanese);
    }

    // Design units, so the comparison is exact rather than at some rendered size
    private static int? Advance(GlyphTypeface g, int codePoint)
    {
        if (!g.CharacterToGlyphMap.TryGetGlyph(codePoint, out var glyph) || glyph == 0) return null;
        return g.TryGetHorizontalGlyphAdvance(glyph, out var advance) ? advance : null;
    }
}
