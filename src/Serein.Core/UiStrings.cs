using System.Globalization;
using System.Text.Json;

namespace Serein.Core;

public sealed record LanguageOptions(string Language = "en")
{
    public void Validate()
    {
        if (Language is not ("en" or "ru" or "de")) throw new InvalidDataException("Unsupported application language.");
    }
}

/// <summary>App-owned UI text. Hardware names, paths and Windows error details are never translated.</summary>
public static class UiStrings
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Resources = Load();
    private static string language = "en";
    public static string Language => language;
    public static IReadOnlyList<string> Languages { get; } = new[] { "en", "ru", "de" };
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(language switch { "ru" => "ru-RU", "de" => "de-DE", _ => "en-US" });
    public static void SetLanguage(string value) { new LanguageOptions(value).Validate(); language = value; }
    public static string Get(string key) => Resources[language].TryGetValue(key, out var value) ? value : Resources["en"].TryGetValue(key, out var english) ? english : throw new KeyNotFoundException("Missing UI string: " + key);
    public static string Format(string key, params object?[] values) => string.Format(Culture, Get(key), values);
    public static IReadOnlyDictionary<string, string> ForLanguage(string code) { new LanguageOptions(code).Validate(); return Resources[code]; }
    public static bool IsUserMessage(string text) => Resources.Values.Any(table => table.Values.Any(value =>
    {
        int format = value.IndexOf('{');
        return format < 0 ? text == value || value.Length > 15 && text.StartsWith(value, StringComparison.Ordinal) : format > 15 && text.StartsWith(value[..format], StringComparison.Ordinal);
    }));
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (string code in new[] { "en", "ru", "de" })
        {
            using var stream = typeof(UiStrings).Assembly.GetManifestResourceStream($"Serein.Core.Translations.{code}.json") ?? throw new InvalidDataException("Missing UI language: " + code);
            result.Add(code, JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException("Invalid UI language: " + code));
        }
        return result;
    }
}
