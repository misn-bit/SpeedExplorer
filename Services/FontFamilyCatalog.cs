using System;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;

namespace SpeedExplorer;

/// <summary>Lists locally installed text fonts and validates the persisted selection.</summary>
internal static class FontFamilyCatalog
{
    public const string DefaultFamilyName = "Segoe UI";

    private static readonly Lazy<string[]> InstalledFamilies = new(LoadInstalledFamilies);

    public static string[] TextFamilies => InstalledFamilies.Value.ToArray();

    public static string ResolveTextFamily(string? requestedFamily)
    {
        if (string.IsNullOrWhiteSpace(requestedFamily))
            return DefaultFamilyName;

        return InstalledFamilies.Value.FirstOrDefault(family =>
                   string.Equals(family, requestedFamily, StringComparison.OrdinalIgnoreCase))
               ?? DefaultFamilyName;
    }

    private static string[] LoadInstalledFamilies()
    {
        using var fonts = new InstalledFontCollection();

        return fonts.Families
            .Where(family => family.IsStyleAvailable(FontStyle.Regular))
            .Select(family => family.Name)
            .Where(name => !string.Equals(name, "Segoe UI Emoji", StringComparison.OrdinalIgnoreCase))
            .Append(DefaultFamilyName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
