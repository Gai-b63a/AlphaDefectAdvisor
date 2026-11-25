using System.Diagnostics;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using Sts2Headless;

namespace AlphaDefectAdvisor;

[ModInitializer(nameof(Start))]
public static class Entry
{
    static Task<string>? installation;
    static string? home;
    static AdviceOverlay overlay = null!;
    static RunSession? session;
    static Process? worker;
    static Task<string>? pending;
    static string? current;
    static long revision, requested;
    static ulong next, retry;
    public static void Start() => Callable.From(Attach).CallDeferred();

    static void Attach()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        overlay = new AdviceOverlay(tree);
        overlay.Hide();
        tree.ProcessFrame += Tick;
        tree.Root.TreeExiting += StopWorker;
    }

    static string PackageDirectory()
    {
        var assembly = typeof(Entry).Assembly;
        string? folder = Path.GetDirectoryName(assembly.Location);
        if (folder != null && File.Exists(Path.Combine(folder, "runtime.zip"))) return folder;

        // Some loaders read assemblies into memory. Use the active mod's registered path.
        var mod = ModManager.Mods.FirstOrDefault(m => m.assemblies?.Contains(assembly) == true)
            ?? ModManager.Mods.Single(m => m.manifest?.id == "AlphaDefectAdvisor");
        string path = ProjectSettings.GlobalizePath(mod.path);
        folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (folder == null || !File.Exists(Path.Combine(folder, "runtime.zip")))
            throw new FileNotFoundException("ワークショップの配布フォルダーに runtime.zip が見つかりません");
        return folder;
    }

    static void StopWorker()
    {
        if (worker != null) {
            try { if (!worker.HasExited) worker.Kill(); }
            finally { worker.Dispose(); worker = null; }
        }
        pending = null;
        current = null;
        revision++;
    }

    static void Tick()
    {
        var now = Time.GetTicksMsec();
        if (now < next) return;
        next = now + 250;
        try {
            if (home == null) {
                if (now < retry) return;
                if (installation == null) {
                    installation = AdvisorRuntime.Prepare(PackageDirectory());
                }
                if (!installation.IsCompleted) {
                    overlay.Show("AlphaDefect　初回データと推論環境を準備中");
                    return;
                }
                home = installation.GetAwaiter().GetResult();
            }
            if (NRun.Instance == null) {
                session = null;
                if (worker != null) StopWorker();
                overlay.Hide();
                return;
            }
            if (now < retry) return;
            var state = RunSession.State;
            if (state == null || state.Players.Count != 1 || state.Players[0].Character.Id.Entry != "IRONCLAD") {
                Wait("ソロのアイアンクラッドに対応しています"); return;
            }
            if (session == null) {
                var catalog = Path.Combine(home, "upstream", "engine", "catalog");
                Vocab.Load(Path.Combine(catalog, "vocab.txt"));
                if (Vocab.Count < 2557) throw new InvalidDataException("語彙表を読み込めません");
                AppContext.SetData("Sts2NoDownsideRelics", Path.Combine(catalog, "no_downside_relics.txt"));
                Observation.FormatOverride = 28;
                Observation.RouteSummary = true;
                session = RunSession.Live();
            }
            var d = session.Inspect();
            if (d == null || d.Choices.Count == 0) { Wait(session.WaitReason); return; }
            var labels = d.Choices.Select(RunSession.Describe).ToArray();
            var frame = Convert.ToBase64String(Observation.Build(session, d, false).Serialize());
            var request = JsonSerializer.Serialize(new { frame, labels, floor = state.TotalFloor, kind = d.Kind.ToString() });
            if (request != current) {
                current = request;
                revision++;
                overlay.Show("現在の盤面を評価中");
            }
            if (worker?.HasExited == true) throw new IOException("推論プロセスが終了しました");
            if (pending != null) {
                if (!pending.IsCompleted) return;
                var result = pending.GetAwaiter().GetResult();
                pending = null;
                if (requested == revision) {
                    overlay.Show(result);
                    return;
                }
            }
            if (requested != revision || worker == null) {
                worker ??= AdvisorRuntime.StartWorker(home);
                requested = revision;
                pending = Predict(worker, request, state.TotalFloor).WaitAsync(TimeSpan.FromMinutes(2));
            }
        } catch (Exception ex) {
            StopWorker();
            installation = null;
            overlay.Show((home == null ? "初回準備を再試行します" : "助言を表示できません") + "\n" + ex.GetBaseException().Message);
            retry = now + 5000;
        }
    }

    static void Wait(string message)
    {
        if (current != null) { current = null; revision++; }
        overlay.Show(message);
    }

    static async Task<string> Predict(Process process, string request, int floor)
    {
        await process.StandardInput.WriteLineAsync(request).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
        using var json = JsonDocument.Parse(line ?? throw new EndOfStreamException("推論が終了しました"));
        var rows = json.RootElement.EnumerateArray().ToArray();
        return $"{floor}階　推薦　{rows[0][0].GetString()}\nモデルの選択確率\n" +
            string.Join("\n", rows.Select(r => $"{r[1].GetDouble():P1}　{r[0].GetString()}"));
    }
}

