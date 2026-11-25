using System.Globalization;
using System.Text.Json;

namespace AlphaDefectAdvisor;

internal readonly record struct RankedAction(int Index, double Probability);

internal static class AdvisorText
{
    static readonly Dictionary<string, Dictionary<string, string>> catalogs = new() {
        ["en"] = Load("en"), ["ja"] = Load("ja")
    };
    static readonly HashSet<string> reported = new();
    static string language = "en";
    static CultureInfo culture = CultureInfo.GetCultureInfo("en-US");

    static Dictionary<string, string> Load(string code)
    {
        using var stream = typeof(AdvisorText).Assembly.GetManifestResourceStream($"AdvisorText.{code}.json")
            ?? throw new InvalidDataException($"Missing translation resource: {code}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    internal static void SetLanguage(string? code)
    {
        var tag = (code ?? "").Replace('_', '-').ToLowerInvariant();
        var basic = tag.Split('-')[0];
        var resolved = catalogs.ContainsKey(tag) ? tag : basic switch {
            "jpn" => "ja", "eng" => "en", _ => catalogs.ContainsKey(basic) ? basic : "en"
        };
        if (resolved == language) return;
        language = resolved;
        culture = CultureInfo.GetCultureInfo(language == "ja" ? "ja-JP" : "en-US");
    }

    internal static string T(string key, params object?[] args)
    {
        if (!catalogs[language].TryGetValue(key, out var format)) {
            if (reported.Add(language + "/" + key))
                Console.Error.WriteLine($"AlphaDefect Advisor: missing {language}/{key}");
            if (!catalogs["en"].TryGetValue(key, out format)) return key;
        }
        return args.Length == 0 ? format : string.Format(culture, format, args);
    }

    internal static string FormatRanking(int floor, string[] labels, RankedAction[] rows) =>
        string.Join("\n", new[] { T("recommendation", floor, labels[rows[0].Index]), T("probabilities") }
            .Concat(rows.Select(r => T("rankRow", r.Probability, labels[r.Index]))));
}
