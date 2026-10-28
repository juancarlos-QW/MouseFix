using System.Globalization;

namespace MouseFix.Core;

/// <summary>
/// The app's two languages. Every visible text is written as a Portuguese/English pair right where it is used,
/// so the two never drift apart. Portuguese on Portuguese Windows, English everywhere else, unless the user chose.
/// </summary>
public static class L
{
    public const string Portuguese = "pt";
    public const string EnglishCode = "en";

    public static bool English { get; private set; } = !IsPortuguese(CultureInfo.CurrentUICulture);

    /// <summary>Sets the language: "pt", "en", or null to follow Windows. Call before any window is created.</summary>
    public static void Use(string? language) => English = language switch
    {
        Portuguese => false,
        EnglishCode => true,
        _ => !IsPortuguese(CultureInfo.CurrentUICulture),
    };

    public static string T(string portuguese, string english) => English ? english : portuguese;

    public static bool IsPortuguese(CultureInfo culture) => culture.TwoLetterISOLanguageName == Portuguese;
}
