using System.Text.Json;
using AlphaDefectAdvisor;

static class ClearHistoryChecks
{
    public static void Run()
    {
        string folder = Directory.CreateTempSubdirectory("advisor-forecast-").FullName;
        try {
            var state = new ClearState(6, 2, 70, 80, 100, "ENCOUNTER.TEST");
            ClearEstimate Get() => RunClearHistory.Load([folder]).Estimate(state);
            static void Check(bool valid) { if (!valid) throw new Exception("Run clear forecast regression"); }
            var order = new AfterAdviceForecast();
            int calls = 0;
            var delayed = new TaskCompletionSource<ClearEstimate>();
            Task<ClearEstimate> Start() { calls++; return delayed.Task; }
            Check(order.Poll(1,Start) == null && calls == 0); // rankings render first
            Check(order.Poll(1,Start) == null && calls == 1);
            Check(order.Poll(2,Start) == null && calls == 1); // the next ranking never waits
            delayed.SetResult(new ClearEstimate(.9));
            Check(order.Poll(2,() => Task.FromResult(new ClearEstimate(.2))) == null);
            Check(order.Poll(2,Start)?.Chance == .2 && calls == 1);
            Check(order.Poll(3,Start) == null);
            Check(order.Poll(3,() => Task.FromException<ClearEstimate>(new IOException("Test failure"))) == null);
            Check(order.Poll(3,Start) is { Chance: null } && calls == 1);
            var empty = RunClearHistory.Load([]);
            Check(empty.Estimate(state).Chance == null);
            var baseline = empty.Estimate(state with { Ascension = 10, Floor = 1 });
            Check(baseline.Reference && baseline.Chance == .684);
            Check(empty.Estimate(state with { Ascension = 10, Floor = 52 }).Chance == null);
            void Write(int i, bool won, int health, int finalHp, bool abandoned = false) {
                object Room(int hp) => new { rooms = new[] { new {model_id = "ENCOUNTER.TEST"} },
                    player_stats = new[] { new { current_hp = hp, max_hp = 80, current_gold = 100 } } };
                var run = new { seed = "s"+i, start_time = i, game_mode = "standard", was_abandoned = abandoned,
                    players = new[] {new {character = "CHARACTER.IRONCLAD"}}, ascension = 6, win = won,
                    map_point_history = new[] { new[] { Room(health), Room(finalHp) } } };
                File.WriteAllText(Path.Combine(folder,i+".run"),JsonSerializer.Serialize(run));
            }
            for (int i = 0; i < 11; i++) Write(i, i%2==0, i%2==0?70:10, 1);
            File.Copy(Path.Combine(folder,"0.run"),Path.Combine(folder,"copied.run"));
            File.WriteAllText(Path.Combine(folder,"broken.run"),"{");
            Write(100,false,70,0,true);
            Check(Get().Chance == null);  // copied, abandoned and malformed files do not count
            for (int i = 11; i < 30; i++) Write(i, i%2==0, i%2==0?70:10, 1);
            var loaded = RunClearHistory.Load([folder]);
            var healthy = loaded.Estimate(state);
            Check(!healthy.Reference && healthy.Chance > loaded.Estimate(state with {Hp = 10}).Chance);
            for (int i = 0; i < 30; i++) Write(i, i%2==0, i%2==0?70:10, i%2==0?80:0);
            Check(Get() == healthy); // outcomes and HP of the unfinished room cannot be used as features
            Check(loaded.Estimate(state with {Floor = 3}).Chance == null);
        } finally { Directory.Delete(folder, recursive: true); }
    }
}
