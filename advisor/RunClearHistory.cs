using System.Text.Json;

namespace AlphaDefectAdvisor;

internal readonly record struct ClearState(int Ascension, int Floor, int Hp, int MaxHp, int Gold, string? Encounter = null);
internal readonly record struct ClearEstimate(double? Chance, bool Reference = false);

// Call once per game tick only after a ranking is available. The first call for
// each revision returns immediately, so the choices render before any forecast work begins.
internal sealed class AfterAdviceForecast
{
    long displayed = -1, calculating;
    Task<ClearEstimate>? pending;
    ClearEstimate? result;

    internal ClearEstimate? Poll(long revision, Func<Task<ClearEstimate>> start)
    {
        if (displayed != revision) {
            displayed = revision;
            result = null;
            return null;
        }
        if (result.HasValue) return result;
        if (pending != null) {
            if (!pending.IsCompleted) return null;
            var done = pending;
            pending = null;
            try {
                var value = done.GetAwaiter().GetResult();
                if (calculating == revision) return result = value;
            } catch (Exception ex) {
                Console.Error.WriteLine($"AlphaDefect Advisor forecast: {ex.Message}");
                if (calculating == revision) return result = new ClearEstimate(null);
            }
        }
        calculating = revision;
        try { pending = start(); }
        catch (Exception ex) {
            Console.Error.WriteLine($"AlphaDefect Advisor forecast: {ex.Message}");
            result = new ClearEstimate(null);
        }
        return result;
    }
}

internal sealed class RunClearHistory
{
    readonly record struct Room(int Hp, int MaxHp, int Gold, string? Encounter);
    sealed record Sample(int Ascension, bool Won, List<Room> Rooms);
    readonly List<Sample> runs = new();

    // W1: 1,000 solo A10 v24,782 EMA greedy runs, 684 clears. Entry i counts runs
    // reaching floor i (including losses on that floor). See REQUIREMENTS.md for source and holdout.
    static readonly int[] reached = [1000,1000,1000,1000,1000,1000,1000,1000,998,996,996,996,
        991,990,987,986,986,986,949,949,949,949,949,949,948,943,940,940,937,935,935,932,
        930,930,911,911,905,905,905,905,905,905,905,903,902,899,897,897,897,847];

    internal static RunClearHistory Load(IEnumerable<string> folders)
    {
        var result = new RunClearHistory();
        var seen = new HashSet<(string Seed, long Start)>();
        foreach (string folder in folders.Distinct()) {
            try {
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.EnumerateFiles(folder, "*.run")) {
                    try {
                        using var stream = File.OpenRead(file);
                        using var doc = JsonDocument.Parse(stream);
                        var r = doc.RootElement;
                        if (r.GetProperty("game_mode").GetString() != "standard" ||
                            Flag(r, "was_abandoned") || Flag(r, "is_custom_game") ||
                            (r.TryGetProperty("modifiers", out var mods) && mods.GetArrayLength() != 0)) continue;
                        var players = r.GetProperty("players");
                        if (players.GetArrayLength() != 1 || players[0].GetProperty("character").GetString() != "CHARACTER.IRONCLAD") continue;
                        var id = (r.GetProperty("seed").GetString()!, r.GetProperty("start_time").GetInt64());
                        if (seen.Contains(id)) continue;
                        var rooms = new List<Room>();
                        foreach (var act in r.GetProperty("map_point_history").EnumerateArray())
                            foreach (var room in act.EnumerateArray()) {
                                var p = room.GetProperty("player_stats")[0];
                                string? encounter = null;
                                if (room.TryGetProperty("rooms", out var visits) && visits.GetArrayLength() > 0 &&
                                    visits[0].TryGetProperty("model_id", out var model))
                                    encounter = model.GetString();
                                rooms.Add(new Room(p.GetProperty("current_hp").GetInt32(),
                                    p.GetProperty("max_hp").GetInt32(), p.GetProperty("current_gold").GetInt32(), encounter));
                            }
                        if (rooms.Count == 0) continue;
                        result.runs.Add(new Sample(r.GetProperty("ascension").GetInt32(),r.GetProperty("win").GetBoolean(),rooms));
                        seen.Add(id);
                    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                        InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException or FormatException) {
                        // Corrupt, partial or incompatible history files do not prevent other samples being used.
                    }
                }
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return result;
    }

    static bool Flag(JsonElement r, string name) => r.TryGetProperty(name, out var value) && value.GetBoolean();

    internal ClearEstimate Estimate(ClearState state)
    {
        var neighbors = new List<(double Distance, bool Won)>();
        foreach (var run in runs) {
            if (state.Ascension != run.Ascension || state.Floor > run.Rooms.Count) continue;
            double distance = 0;
            if (state.Floor >= 2) {
                // Use only rooms already completed at this decision. The current room's
                // end HP (especially zero on death) would leak its outcome into the estimate.
                var prior = run.Rooms[state.Floor - 2];
                distance = 4 * Math.Abs((double)state.Hp / Math.Max(1,state.MaxHp) -
                    (double)prior.Hp / Math.Max(1,prior.MaxHp)) +
                    Math.Abs(Math.Log((1.0 + Math.Max(0,state.Gold)) / (1.0 + Math.Max(0,prior.Gold)))) * .35 +
                    Math.Abs(state.MaxHp - prior.MaxHp) / 80.0;
            }
            if (state.Encounter != null && state.Floor > 0 && run.Rooms[state.Floor - 1].Encounter != state.Encounter)
                distance += .6;
            neighbors.Add((distance,run.Won));
        }
        if (neighbors.Count >= 12) {
            // Include ties at the cutoff so the result never depends on directory order.
            double cutoff = neighbors.OrderBy(n => n.Distance).ElementAt(Math.Min(30,neighbors.Count)-1).Distance;
            double total = 0, wins = 0, squares = 0;
            foreach (var n in neighbors.Where(n => n.Distance <= cutoff)) {
                double weight = Math.Exp(-n.Distance);
                total += weight; squares += weight * weight;
                if (n.Won) wins += weight;
            }
            if (total >= 5 && total * total >= 8 * squares) return new((wins + .5) / (total + 1));
        }
        return state.Ascension == 10 && state.Floor >= 0 && state.Floor < reached.Length
            ? new ClearEstimate(684.0 / reached[state.Floor], true) : new ClearEstimate(null);
    }
}
