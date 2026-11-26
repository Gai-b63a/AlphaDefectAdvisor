using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlphaDefectAdvisor;

static class LocalizationChecks
{
    public static void Run()
    {
        var catalogs = new Dictionary<string, Dictionary<string, string>>();
        foreach (var code in AdvisorText.GameLocales.Values) {
            using var resource = typeof(AdvisorText).Assembly.GetManifestResourceStream($"AdvisorText.{code}.json")!;
            catalogs[code] = JsonSerializer.Deserialize<Dictionary<string, string>>(resource)!;
        }
        static string Fields(string value) => string.Join(",", Regex.Matches(value, @"\{\d+(?::[^}]*)?\}")
            .Select(m => m.Value).Order());
        // Snapshot from the supported-language table in retail v0.114.0, including hidden Arabic.
        var expectedIds = "ara deu eng esp fra ind ita jpn kor pol ptb rus spa tha tur zhs zht".Split(' ');
        if (!expectedIds.ToHashSet().SetEquals(AdvisorText.GameLocales.Keys))
            throw new InvalidDataException("Retail language coverage changed");
        var english = catalogs["en"];
        foreach (var (gameId, code) in AdvisorText.GameLocales) {
            var strings = catalogs[code];
            if (!english.Keys.ToHashSet().SetEquals(strings.Keys) ||
                english.Any(pair => string.IsNullOrWhiteSpace(strings[pair.Key]) ||
                    Fields(pair.Value) != Fields(strings[pair.Key])))
                throw new InvalidDataException($"Translation keys or placeholders differ in {code}");
            AdvisorText.SetLanguage(gameId);
            if (AdvisorText.Language != code || AdvisorText.RightToLeft != (gameId == "ara"))
                throw new Exception($"Incorrect language or direction for {gameId}");
            foreach (var key in strings.Keys) AdvisorText.T(key, 1, 2, 3);
            var labels = new[] { AdvisorText.T("discard", 1, "Potion"), AdvisorText.T("discard", 2, "Potion") };
            var lines = AdvisorText.FormatRanking(8, labels, [new(1, .75), new(0, .25)]).Split('\n');
            if (labels[0] == labels[1] || !lines[0].Contains(labels[1]) || lines[1] != strings["probabilities"] ||
                lines[2] != AdvisorText.T("rankRow", .75, labels[1]) || lines[3] != AdvisorText.T("rankRow", .25, labels[0]))
                throw new Exception($"Incorrect ranking for {gameId}");
        }
        foreach (var (tag, expected) in new[] { ("ja_JP","ja"), ("en-US","en"), ("fra","fr"), ("sv-SE","en"),
                ("zh-TW","zh-Hant"), ("zh_Hans","zh-Hans"), ("zh-Hant-HK","zh-Hant"), ("es_LATAM","es-419"),
                ("es-ES","es-ES"), ("es-MX","es-419"), ("pt_BR","pt-BR") }) {
            AdvisorText.SetLanguage(tag);
            if (AdvisorText.Language != expected) throw new Exception($"Incorrect locale for {tag}");
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
