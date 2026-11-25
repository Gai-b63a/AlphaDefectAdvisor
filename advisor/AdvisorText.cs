using System.Globalization;
using System.Text.Json;

namespace AlphaDefectAdvisor;

internal readonly record struct RankedAction(int Index, double Probability);

internal static class AdvisorText
{
    // Game language IDs and culture tags from v0.114.0 LocManager.
    internal static readonly IReadOnlyDictionary<string, string> GameLocales =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["eng"] = "en", ["ara"] = "ar", ["zhs"] = "zh-Hans", ["zht"] = "zh-Hant",
            ["deu"] = "de", ["esp"] = "es-419", ["fra"] = "fr", ["ind"] = "id",
            ["ita"] = "it", ["jpn"] = "ja", ["kor"] = "ko", ["pol"] = "pl",
            ["ptb"] = "pt-BR", ["rus"] = "ru", ["spa"] = "es-ES", ["tha"] = "th", ["tur"] = "tr"
        };
    static readonly Dictionary<string, Dictionary<string, string>> catalogs =
        GameLocales.Values.ToDictionary(code => code, Load, StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> reported = new();
    static string? previousCode;
    internal static string Language { get; private set; } = "en";
    static CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
    internal static bool RightToLeft => culture.TextInfo.IsRightToLeft;

    static Dictionary<string, string> Load(string code)
    {
        using var stream = typeof(AdvisorText).Assembly.GetManifestResourceStream($"AdvisorText.{code}.json")
            ?? throw new InvalidDataException($"Missing translation resource: {code}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    internal static void SetLanguage(string? code)
    {
        if (code == previousCode) return;
        previousCode = code;
        var tag = (code ?? "").Replace('_', '-').ToLowerInvariant();
        var parts = tag.Split('-');
        if (!GameLocales.TryGetValue(tag, out var resolved)) {
            resolved = parts[0] switch {
                "zh" when parts.Contains("hant") => "zh-Hant",
                "zh" when parts.Contains("hans") => "zh-Hans",
                "zh" when parts.Any(p => p is "tw" or "hk" or "mo") => "zh-Hant",
                "zh" => "zh-Hans",
                "es" when parts.Any(p => p is "419" or "latam" or "mx" or "ar" or "bo" or "cl"
                    or "co" or "cr" or "cu" or "do" or "ec" or "gt" or "hn" or "ni" or "pa"
                    or "pe" or "pr" or "py" or "sv" or "us" or "uy" or "ve") => "es-419",
                "es" => "es-ES", "pt" => "pt-BR",
                _ => GameLocales.GetValueOrDefault(parts[0]) ?? (catalogs.ContainsKey(parts[0]) ? parts[0] : "en")
            };
        }
        Language = resolved;
        culture = CultureInfo.GetCultureInfo(resolved switch { "en" => "en-US", "ja" => "ja-JP", _ => resolved });
    }

    internal static string T(string key, params object?[] args)
    {
        if (!catalogs[Language].TryGetValue(key, out var format)) {
            if (reported.Add(Language + "/" + key))
                Console.Error.WriteLine($"AlphaDefect Advisor: missing {Language}/{key}");
            if (!catalogs["en"].TryGetValue(key, out format)) return key;
        }
        return args.Length == 0 ? format : string.Format(culture, format, args);
    }

    internal static string FormatRanking(int floor, string[] labels, RankedAction[] rows) =>
        string.Join("\n", new[] { T("recommendation", floor, labels[rows[0].Index]), T("probabilities") }
            .Concat(rows.Select(r => T("rankRow", r.Probability, labels[r.Index]))));
}
