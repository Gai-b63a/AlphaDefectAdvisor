using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlphaDefectAdvisor;

static class LocalizationChecks
{
    public static void Run()
    {
        var catalogs = new Dictionary<string, Dictionary<string, string>>();
        foreach (var code in new[] { "en", "ja" }) {
            using var resource = typeof(AdvisorText).Assembly.GetManifestResourceStream($"AdvisorText.{code}.json")!;
            catalogs[code] = JsonSerializer.Deserialize<Dictionary<string, string>>(resource)!;
        }
        static string Fields(string value) => string.Join(",", Regex.Matches(value, @"\{\d+(?::[^}]*)?\}")
            .Select(m => m.Value).Order());
        if (!catalogs["en"].Keys.ToHashSet().SetEquals(catalogs["ja"].Keys) ||
            catalogs["en"].Any(pair => Fields(pair.Value) != Fields(catalogs["ja"][pair.Key])))
            throw new InvalidDataException("Translation keys or placeholders differ");
        foreach (var (language, expected) in new[] { ("jpn","ja"), ("ja_JP","ja"), ("eng","en"), ("en-US","en"), ("fra","en") }) {
            AdvisorText.SetLanguage(language);
            if (AdvisorText.T("move") != catalogs[expected]["move"]) throw new Exception(language);
        }
        // The same completed worker response must render in the current language,
        // even when two display names match and the language changes while it is in flight.
        RankedAction[] ranking = [new(1, .75), new(0, .25)];
        foreach (var (lang, heading, slot) in new[] {
                ("jpn", "モデルの選択確率", "枠2"), ("eng", "Model selection probabilities", "slot 2") }) {
            AdvisorText.SetLanguage(lang);
            var result = AdvisorText.FormatRanking(8,
                [AdvisorText.T("discard", 1, "Potion"), AdvisorText.T("discard", 2, "Potion")], ranking);
            var lines = result.Split('\n');
            if (lines.Length != 4 || !lines[0].Contains(slot) || lines[1] != heading ||
                !lines[2].Contains("75.0") || !lines[2].Contains(slot)) throw new Exception("Localized ranking: " + result);
        }
        // Exercise a missing translation in this test process only.
        AdvisorText.SetLanguage("ja");
        var field = typeof(AdvisorText).GetField("catalogs", BindingFlags.Static | BindingFlags.NonPublic)!;
        var ja = ((Dictionary<string, Dictionary<string,string>>)field.GetValue(null)!)["ja"];
        var saved = ja["move"];
        try {
            ja.Remove("move");
            if (AdvisorText.T("move") != catalogs["en"]["move"]) throw new Exception("Missing-key fallback");
        } finally { ja["move"] = saved; }
    }
}
